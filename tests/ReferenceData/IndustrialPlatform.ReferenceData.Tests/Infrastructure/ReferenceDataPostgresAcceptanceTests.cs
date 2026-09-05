using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Caching;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Application.CodingRule;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Application.DynamicProperty;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.CodingRule;
using IndustrialPlatform.ReferenceData.Domain.DynamicProperty;
using IndustrialPlatform.ReferenceData.Domain.Metadata;
using IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Contracts.CodingRule;
using IndustrialPlatform.ReferenceData.Infrastructure.Caching;
using IndustrialPlatform.ReferenceData.Infrastructure.CodingRule;
using IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;
using IndustrialPlatform.ReferenceData.Infrastructure.DynamicProperty;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.ReferenceData.Infrastructure.Metadata;
using IndustrialPlatform.ReferenceData.Infrastructure.Persistence;
using IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SqlSugar;
using StackExchange.Redis;

namespace IndustrialPlatform.ReferenceData.Tests.Infrastructure;

public sealed class ReferenceDataPostgresAcceptanceTests
{
    [Fact]
    public async Task Owned_redis_generation_fence_rejects_a_fill_started_before_invalidation()
    {
        var redisConnection = Environment.GetEnvironmentVariable("PF03_REDIS_CONNECTION");
        if (string.IsNullOrWhiteSpace(redisConnection)) return; // Explicitly exercised by the PF-03 infrastructure gate.

        var moduleKey = $"pf03-race-{Guid.NewGuid():N}";
        var key = new ReferenceDataCacheKey(moduleKey, $"pf03:race:{Guid.NewGuid():N}");
        var generationKey = RedisReferenceDataCache.PhysicalGenerationKey(moduleKey);
        var valueKey = RedisReferenceDataCache.PhysicalValueKey(key);
        Assert.Equal(HashTag(generationKey), HashTag(valueKey));
        var options = ConfigurationOptions.Parse(redisConnection);
        options.AbortOnConnectFail = true;
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        var cache = new RedisReferenceDataCache(multiplexer,
            Options.Create(new RedisOptions { DefaultDatabase = 0 }),
            NullLogger<RedisReferenceDataCache>.Instance);
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var racedRead = cache.GetOrLoadAsync(key, TimeSpan.FromMinutes(1), async () =>
            {
                loadStarted.SetResult();
                await releaseLoad.Task;
                return new DatabaseProbe("stale");
            }, CancellationToken.None);
            await loadStarted.Task;
            await cache.InvalidateAsync(new(key.Module, key.Value), CancellationToken.None);
            releaseLoad.SetResult();

            Assert.Equal("stale", (await racedRead).Value);
            Assert.Null(await cache.GetAsync<DatabaseProbe>(key, CancellationToken.None));
            var fresh = await cache.GetOrLoadAsync(key, TimeSpan.FromMinutes(1),
                () => Task.FromResult(new DatabaseProbe("fresh")), CancellationToken.None);
            Assert.Equal("fresh", fresh.Value);
            Assert.Equal("fresh", (await cache.GetAsync<DatabaseProbe>(key, CancellationToken.None))?.Value);
        }
        finally
        {
            await multiplexer.GetDatabase().KeyDeleteAsync([valueKey, generationKey]);
        }
    }

    [Fact]
    public async Task Owned_redis_new_cache_instance_rotates_generation_before_accepting_old_values()
    {
        var redisConnection = Environment.GetEnvironmentVariable("PF03_REDIS_CONNECTION");
        if (string.IsNullOrWhiteSpace(redisConnection)) return; // Explicitly exercised by the PF-03 infrastructure gate.

        var moduleKey = $"pf03-recovery-{Guid.NewGuid():N}";
        var key = new ReferenceDataCacheKey(moduleKey, $"pf03:recovery:{Guid.NewGuid():N}");
        var generationKey = RedisReferenceDataCache.PhysicalGenerationKey(moduleKey);
        var valueKey = RedisReferenceDataCache.PhysicalValueKey(key);
        var options = ConfigurationOptions.Parse(redisConnection);
        options.AbortOnConnectFail = true;
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        try
        {
            var first = new RedisReferenceDataCache(multiplexer,
                Options.Create(new RedisOptions { DefaultDatabase = 0 }),
                NullLogger<RedisReferenceDataCache>.Instance);
            var generation = await first.GetGenerationAsync(key.GenerationKey, CancellationToken.None);
            Assert.True(await first.SetIfGenerationAsync(key, new DatabaseProbe("old"),
                TimeSpan.FromMinutes(1), generation, CancellationToken.None));
            Assert.Equal("old", (await first.GetAsync<DatabaseProbe>(key, CancellationToken.None))?.Value);

            var restarted = new RedisReferenceDataCache(multiplexer,
                Options.Create(new RedisOptions { DefaultDatabase = 0 }),
                NullLogger<RedisReferenceDataCache>.Instance);

            Assert.Null(await restarted.GetAsync<DatabaseProbe>(key, CancellationToken.None));
        }
        finally
        {
            await multiplexer.GetDatabase().KeyDeleteAsync([valueKey, generationKey]);
        }
    }

    [Fact]
    public async Task Owned_redis_invalidation_only_rotates_the_changed_definition_scope()
    {
        var redisConnection = Environment.GetEnvironmentVariable("PF03_REDIS_CONNECTION");
        if (string.IsNullOrWhiteSpace(redisConnection)) return; // Explicitly exercised by the PF-03 infrastructure gate.

        var suffix = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var changed = ReferenceDataCacheKeys.Dictionary("tenant-PF03", $"CHANGED-{suffix}");
        var untouched = ReferenceDataCacheKeys.Dictionary("tenant-PF03", $"UNTOUCHED-{suffix}");
        var options = ConfigurationOptions.Parse(redisConnection);
        options.AbortOnConnectFail = true;
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        var cache = new RedisReferenceDataCache(multiplexer,
            Options.Create(new RedisOptions { DefaultDatabase = 0 }),
            NullLogger<RedisReferenceDataCache>.Instance);
        var database = multiplexer.GetDatabase();
        try
        {
            await cache.SetAsync(changed, new DatabaseProbe("changed-old"),
                TimeSpan.FromMinutes(1), CancellationToken.None);
            await cache.SetAsync(untouched, new DatabaseProbe("untouched"),
                TimeSpan.FromMinutes(1), CancellationToken.None);

            await cache.InvalidateAsync(
                ReferenceDataCacheKeys.Dictionaries($"CHANGED-{suffix}"), CancellationToken.None);

            Assert.Null(await cache.GetAsync<DatabaseProbe>(changed, CancellationToken.None));
            Assert.Equal("untouched",
                (await cache.GetAsync<DatabaseProbe>(untouched, CancellationToken.None))?.Value);
        }
        finally
        {
            await database.KeyDeleteAsync(
            [
                RedisReferenceDataCache.PhysicalValueKey(changed),
                RedisReferenceDataCache.PhysicalGenerationKey(changed.GenerationKey),
                RedisReferenceDataCache.PhysicalValueKey(untouched),
                RedisReferenceDataCache.PhysicalGenerationKey(untouched.GenerationKey),
            ]);
        }
    }

    [Fact]
    public async Task Durable_generation_rejects_old_value_when_a_replica_misses_redis_invalidation()
    {
        var redisConnection = Environment.GetEnvironmentVariable("PF03_REDIS_CONNECTION");
        if (string.IsNullOrWhiteSpace(redisConnection)) return; // Explicitly exercised by the PF-03 infrastructure gate.

        var path = Path.Combine(Path.GetTempPath(), $"pf03-cache-generation-{Guid.NewGuid():N}.db");
        var definitionNId = $"ASYMMETRIC-{Guid.NewGuid():N}";
        var key = ReferenceDataCacheKeys.Dictionary("tenant-PF03", definitionNId);
        var pattern = ReferenceDataCacheKeys.Dictionaries(definitionNId);
        var options = ConfigurationOptions.Parse(redisConnection);
        options.AbortOnConnectFail = true;
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        var redis = multiplexer.GetDatabase();
        try
        {
            using var context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
            {
                DbType = DbType.Sqlite,
                ConnectionString = $"Data Source={path};Pooling=False",
            }));
            await context.SqlSugar.Ado.ExecuteCommandAsync(ReferenceDataCacheGenerationMigration.Sql(false));
            var cache = new RedisReferenceDataCache(multiplexer,
                Options.Create(new RedisOptions { DefaultDatabase = 0 }),
                NullLogger<RedisReferenceDataCache>.Instance,
                context);

            await cache.SetAsync(key, new DatabaseProbe("old"), TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.Equal("old", (await cache.GetAsync<DatabaseProbe>(key, CancellationToken.None))?.Value);

            await context.SqlSugar.Ado.BeginTranAsync();
            try
            {
                await ReferenceDataCacheGenerationStore.AdvanceAsync(
                    context.SqlSugar, [pattern], CancellationToken.None);
                await context.SqlSugar.Ado.CommitTranAsync();
            }
            catch
            {
                await context.SqlSugar.Ado.RollbackTranAsync();
                throw;
            }

            Assert.Null(await cache.GetAsync<DatabaseProbe>(key, CancellationToken.None));

            var generationBeforeLoad = await cache.GetGenerationAsync(
                key.GenerationKey, CancellationToken.None);
            await ReferenceDataCacheGenerationStore.AdvanceAsync(
                context.SqlSugar, [pattern], CancellationToken.None);
            Assert.False(await cache.SetIfGenerationAsync(key, new DatabaseProbe("stale-fill"),
                TimeSpan.FromMinutes(1), generationBeforeLoad, CancellationToken.None));
            Assert.Null(await cache.GetAsync<DatabaseProbe>(key, CancellationToken.None));

            var fresh = await cache.GetOrLoadAsync(key, TimeSpan.FromMinutes(1),
                () => Task.FromResult(new DatabaseProbe("fresh")), CancellationToken.None);
            Assert.Equal("fresh", fresh.Value);
            Assert.Equal("fresh", (await cache.GetAsync<DatabaseProbe>(key, CancellationToken.None))?.Value);
        }
        finally
        {
            try
            {
                await redis.KeyDeleteAsync(
                [
                    RedisReferenceDataCache.PhysicalValueKey(key),
                    RedisReferenceDataCache.PhysicalGenerationKey(key.GenerationKey),
                ]);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Unavailable_redis_adapter_returns_real_postgres_value()
    {
        var connectionString = Environment.GetEnvironmentVariable("PF03_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return; // Explicitly exercised by the PF-03 infrastructure gate.

        var redisConfiguration = ConfigurationOptions.Parse("127.0.0.1:59248");
        redisConfiguration.AbortOnConnectFail = false;
        redisConfiguration.ConnectTimeout = 500;
        redisConfiguration.AsyncTimeout = 1_000;
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(redisConfiguration);
        Assert.False(multiplexer.IsConnected);

        var cache = new RedisReferenceDataCache(multiplexer,
            Options.Create(new RedisOptions { DefaultDatabase = 0 }),
            NullLogger<RedisReferenceDataCache>.Instance);
        var probe = await cache.GetOrLoadAsync(
            new ReferenceDataCacheKey("dictionary", $"pf03:unavailable:{Guid.NewGuid():N}"),
            TimeSpan.FromMinutes(1),
            async () =>
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(
                    "SELECT name FROM reference_data.dictionary_definition WHERE n_id='PF03_REAL_STATUS' LIMIT 1",
                    connection);
                return new DatabaseProbe((string?)await command.ExecuteScalarAsync());
            },
            CancellationToken.None);

        Assert.Equal("PF-03 real statuses", probe.Value);
    }

    [Fact]
    public async Task Owned_postgres_applies_full_stream_and_enforces_dynamic_value_columns()
    {
        var connectionString = Environment.GetEnvironmentVariable("PF03_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return; // Explicitly exercised by the PF-03 infrastructure gate.
        var options = new SqlSugarOptions { DbType = DbType.PostgreSQL, ConnectionString = connectionString };
        using var context = new SqlSugarDbContext(Options.Create(options));
        var initializer = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(context));
        var target = new ResolvedDatabaseTarget("Test", DatabaseTopologyMode.PerService, "referencedata", DatabaseProvider.PostgreSQL,
            "referencedata_db", "pf03_reference_data", false);
        var initialization = new ServiceInitializationContext("Test", "TENANT-PG", "OP", "referencedata", "referencedata", target,
            ReferenceDataServiceInitializer.CurrentVersion, ServiceInitializationPolicy.Standard, "pf03-postgres-acceptance");
        var inspection = await initializer.InspectAsync(initialization, CancellationToken.None);
        var result = await initializer.ApplyAsync(initialization, await initializer.PlanAsync(initialization, inspection, CancellationToken.None), CancellationToken.None);
        Assert.True(result.Ready);
        Assert.Equal(ReferenceDataServiceInitializer.CurrentVersion, result.ObservedVersion);

        var generationPattern = ReferenceDataCacheKeys.Dictionaries(
            $"PG-GENERATION-{Guid.NewGuid():N}");
        await ReferenceDataCacheGenerationStore.AdvanceAsync(
            context.SqlSugar, [generationPattern], CancellationToken.None);
        var firstGeneration = await ReferenceDataCacheGenerationStore.ReadAsync(
            context, generationPattern.GenerationKey, CancellationToken.None);
        await ReferenceDataCacheGenerationStore.AdvanceAsync(
            context.SqlSugar, [generationPattern], CancellationToken.None);
        var secondGeneration = await ReferenceDataCacheGenerationStore.ReadAsync(
            context, generationPattern.GenerationKey, CancellationToken.None);
        Assert.Equal(64, firstGeneration.Length);
        Assert.Equal(64, secondGeneration.Length);
        Assert.NotEqual(firstGeneration, secondGeneration);

        var repository = new DynamicConfigurationRepository(context);
        var dictionaries = new DictionaryService(new DictionaryRepository(context));
        var service = new DynamicConfigurationService(repository, dictionaries);
        var actor = new ReferenceDataActor("TENANT-PG", "PF03", true, "pf03-pg");
        var nId = "PG" + Guid.NewGuid().ToString("N")[..20];
        var root = new DynamicConfigDefinition(nId, "PostgreSQL acceptance", null, ReferenceScopeType.Tenant, actor.TenantNId, null);
        root.Update(root.Name, null,
        [
            new("AMOUNT", Field(ReferenceDataType.Decimal)),
            new("AT", Field(ReferenceDataType.DateTime)),
            new("BODY", Field(ReferenceDataType.Json)),
        ]);
        await repository.CreateAsync(root, null, CancellationToken.None);
        var before = (root.OptimisticVersion, root.ConcurrencyVersion);
        var record = root.AddRecord("ONE", new(null, "PG", 0, true),
        [
            new("AMOUNT", Scalar(ReferenceDataType.Decimal, "999999999999999999.1234567890")),
            new("AT", Scalar(ReferenceDataType.DateTime, "\"2026-09-05T01:02:03.1234567Z\"")),
            new("BODY", Scalar(ReferenceDataType.Json, "{\"ordered\":true,\"value\":1}")),
        ]);
        await repository.SaveAsync([new(root, before.OptimisticVersion, before.ConcurrencyVersion, true, record.Id)], CancellationToken.None);
        var stored = await repository.GetAsync(actor.TenantNId, root.Id, true, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("999999999999999999.123456789", stored.Record(record.Id).Values.Single(value => value.FieldNId == "AMOUNT").Value.CanonicalValue);
        Assert.Equal("2026-09-05T01:02:03.1234560Z", stored.Record(record.Id).Values.Single(value => value.FieldNId == "AT").Value.JsonValue.GetString());

        var valueId = stored.Record(record.Id).Values.Single(value => value.FieldNId == "AMOUNT").Id;
        var postgres = await Assert.ThrowsAsync<PostgresException>(() => context.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE reference_data.dynamic_property_value SET boolean_value=true WHERE id=@id", new SugarParameter("@id", valueId)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
        var constraintCount = await context.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_schema='reference_data' AND table_name='dynamic_property_value' AND constraint_type='FOREIGN KEY'");
        Assert.True(constraintCount >= 3);

        var unitRepository = new UnitDimensionRepository(context);
        var unitDimension = new UnitDimension("PG" + Guid.NewGuid().ToString("N")[..20],
            "PostgreSQL numeric acceptance", null, ReferenceScopeType.Tenant, actor.TenantNId, null);
        unitDimension.Update(unitDimension.Name, null, UnitConversionKind.AbsoluteTemperature, "BASE",
        [
            new("BASE", "Base", "b", 1m, 0m, 12, UnitRoundingMode.ToEven, true, 0),
            new("EXACT", "Exact", "e", 0.123456789012m, -9999999999999999.999999999999m,
                12, UnitRoundingMode.AwayFromZero, true, 1),
        ]);
        await unitRepository.CreateAsync(unitDimension, null, CancellationToken.None);
        var storedUnitDimension = await unitRepository.GetAsync(actor.TenantNId, unitDimension.Id, CancellationToken.None);
        Assert.NotNull(storedUnitDimension);
        var exactUnit = storedUnitDimension.Units.Single(unit => unit.NId == "EXACT");
        Assert.Equal(0.123456789012m, exactUnit.FactorToBase);
        Assert.Equal(-9999999999999999.999999999999m, exactUnit.OffsetToBase);
        var factorConstraint = await Assert.ThrowsAsync<PostgresException>(() => context.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE reference_data.unit_of_measure_unit SET factor_to_base=0 WHERE id=@id",
            new SugarParameter("@id", exactUnit.Id)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, factorConstraint.SqlState);
        var unitForeignKeys = await context.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_schema='reference_data' AND table_name='unit_of_measure_unit' AND constraint_type='FOREIGN KEY'");
        Assert.Equal(1, unitForeignKeys);
        var numericShape = await context.SqlSugar.Ado.SqlQuerySingleAsync<NumericShape>(
            "SELECT numeric_precision AS \"Precision\", numeric_scale AS \"Scale\" FROM information_schema.columns WHERE table_schema='reference_data' AND table_name='unit_of_measure_unit' AND column_name='factor_to_base'");
        Assert.Equal(28, numericShape.Precision);
        Assert.Equal(12, numericShape.Scale);

        var metadataRepository = new MetadataSchemaRepository(context);
        var schema = new EntitySchema("PG" + Guid.NewGuid().ToString("N")[..20], "PG metadata", null,
            ReferenceScopeType.Tenant, actor.TenantNId, null);
        schema.Update(schema.Name, null,
        [
            new("AMOUNT", new("Amount", ReferenceDataType.Decimal, false, false, true, 0, null,
                null, null, -9999999999999999.999999999999m, 9999999999999999.999999999999m,
                null, null, null, 28, 12, null, null, null, null, null, null)),
        ]);
        await metadataRepository.CreateAsync(schema, null, CancellationToken.None);
        var storedSchema = await metadataRepository.GetAsync(actor.TenantNId, schema.Id, CancellationToken.None);
        Assert.NotNull(storedSchema);
        Assert.Equal(-9999999999999999.999999999999m, storedSchema.Attributes.Single().Settings.MinValue);
        Assert.Equal(9999999999999999.999999999999m, storedSchema.Attributes.Single().Settings.MaxValue);

        var codingNId = "PG" + Guid.NewGuid().ToString("N")[..20];
        var codingService = new CodingRuleService(new CodingRuleRepository(context));
        var coding = await codingService.CreateAsync(actor,
            new("Tenant", codingNId, "PG coding", "WorkOrder", "PG-{SEQ:6}", "Never"),
            CancellationToken.None);
        coding = await codingService.PublishAsync(actor, coding.Id,
            new(coding.OptimisticVersion, coding.ConcurrencyVersion, "PG acceptance"), CancellationToken.None);
        var outboxShape = await context.SqlSugar.Ado.SqlQuerySingleAsync<OutboxJsonShape>("""
            SELECT jsonb_typeof(payload) AS "PayloadType",
                   (payload->>'eventVersion')::integer AS "EventVersion",
                   payload ? 'traceId' AS "HasTraceId",
                   payload ? 'scopeId' AS "HasScopeId",
                   payload ? 'tenantNId' AS "HasTenantNId"
            FROM reference_data.outbox_message
            WHERE module_key='coding-rule' AND aggregate_id=@id
            """, new SugarParameter("@id", coding.Id));
        Assert.Equal("object", outboxShape.PayloadType);
        Assert.Equal(1, outboxShape.EventVersion);
        Assert.True(outboxShape.HasTraceId && outboxShape.HasScopeId && outboxShape.HasTenantNId);
        var generated = await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
        {
            using var isolated = new SqlSugarDbContext(Options.Create(options));
            return await new CodingRuleService(new CodingRuleRepository(isolated)).GenerateAsync(actor, codingNId,
                new GenerateCodeRequest("Tenant", actor.TenantNId, coding.Revision), $"pg-key-{index}",
                CancellationToken.None);
        }));
        Assert.Equal(12, generated.Select(item => item.Sequence).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 12).Select(value => (long)value),
            generated.Select(item => item.Sequence).Order());

        var replayed = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var isolated = new SqlSugarDbContext(Options.Create(options));
            return await new CodingRuleService(new CodingRuleRepository(isolated)).GenerateAsync(actor, codingNId,
                new GenerateCodeRequest("Tenant", actor.TenantNId, coding.Revision), "pg-shared-key",
                CancellationToken.None);
        }));
        Assert.Single(replayed.Select(item => item.Code).Distinct());
        Assert.All(replayed, item => Assert.Equal(13, item.Sequence));

        var mismatchedRevision = await Assert.ThrowsAsync<PostgresException>(() =>
            context.SqlSugar.Ado.ExecuteCommandAsync("""
                INSERT INTO reference_data.coding_rule_sequence
                    (coding_rule_id,rule_revision,period_key,context_hash,last_value,created_on,last_updated_on)
                VALUES (@id,999,'ALL',@hash,1,now(),now())
                """, new SugarParameter("@id", coding.Id), new SugarParameter("@hash", new string('A', 64))));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, mismatchedRevision.SqlState);
    }

    private static DynamicFieldSettings Field(ReferenceDataType type) => new(type.ToString(), type, true, true, 0,
        null, null, null, null, null, type == ReferenceDataType.Decimal ? 10 : null, null, null, null, null);
    private static ReferenceScalar Scalar(ReferenceDataType type, string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return ReferenceScalar.Parse(type, document.RootElement);
    }

    private sealed class NumericShape
    {
        public int Precision { get; set; }
        public int Scale { get; set; }
    }

    private sealed class OutboxJsonShape
    {
        public string PayloadType { get; set; } = string.Empty;
        public int EventVersion { get; set; }
        public bool HasTraceId { get; set; }
        public bool HasScopeId { get; set; }
        public bool HasTenantNId { get; set; }
    }

    private sealed record DatabaseProbe(string? Value);

    private static string HashTag(string key)
    {
        var start = key.IndexOf('{') + 1;
        var end = key.IndexOf('}', start);
        return key[start..end];
    }
}
