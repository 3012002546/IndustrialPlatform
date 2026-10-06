using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Collaboration.Infrastructure;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using IndustrialPlatform.Collaboration.Infrastructure.Persistence;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.CodingRule;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.CodingRule;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Dictionary;
using IndustrialPlatform.ReferenceData.Infrastructure.CodingRule;
using IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;
using System.Security.Cryptography;
using System.Text.Json;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Application.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Application.Metadata;
using IndustrialPlatform.ReferenceData.Application.StateMachine;
using IndustrialPlatform.ReferenceData.Application.Parameter;
using IndustrialPlatform.ReferenceData.Application.DynamicProperty;
using IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Infrastructure.Metadata;
using IndustrialPlatform.ReferenceData.Infrastructure.StateMachine;
using IndustrialPlatform.ReferenceData.Infrastructure.Parameter;
using IndustrialPlatform.ReferenceData.Infrastructure.DynamicProperty;
using IndustrialPlatform.ReferenceData.Contracts.Metadata;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;
using IndustrialPlatform.Collaboration.EmbeddedHost.Configuration;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;
using IndustrialPlatform.SystemData.Testing;

namespace IndustrialPlatform.IntegrationTests;

/// <summary>显式启用后，在受控隔离库验证四个服务的完整物理结构；不启动宿主、不修改运行配置。</summary>
[Trait("Category", "SqlServer")]
public sealed class SqlServerWholeSchemaTests
{
#if PF06A_SQLSERVER_ENABLED
    [Fact]
#else
    [Fact(Skip = "真实 SQL Server 验收需 PF06A_SQLSERVER_ENABLED=1 构建，并设置 PF06A_SQLSERVER_ACCEPTANCE=1。")]
#endif
    public async Task All_service_tables_migrate_and_second_application_preserves_ledgers()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("PF06A_SQLSERVER_ACCEPTANCE"));
        using var db = OpenAcceptanceContext();
        var sql = db.SqlSugar;
        var actualDatabase = await sql.Ado.GetStringAsync("SELECT DB_NAME()");
        Assert.Equal("industrial_platform_sql" + AcceptanceYear, actualDatabase);
        Assert.Equal(AcceptanceYear == "2019" ? 15 : 16, await sql.Ado.GetIntAsync("SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int)"));
        var legacyObjects = new Dictionary<string, (int Id, int Count)>();
        foreach (var name in new[] { "service_call_nonce", "step_up_grant" })
        {
            var objectId = await sql.Ado.GetIntAsync($"SELECT COALESCE(OBJECT_ID(N'dbo.pf05_{name}',N'U'),0)");
            if (objectId != 0)
                legacyObjects[name] = (objectId, await sql.Ado.GetIntAsync($"SELECT COUNT(*) FROM dbo.pf05_{name}"));
        }

        var identity = new IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations.SchemaMigrationRunner(
            db, IdentitySchemaMigrations.All,
            NullLogger<IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations.SchemaMigrationRunner>.Instance);
        var systemData = new IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations.SchemaMigrationRunner(
            db, SystemDataSchemaMigrations.All,
            NullLogger<IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations.SchemaMigrationRunner>.Instance);
        var reference = new ReferenceDataInitializationLedger(db);
        var collaboration = new CollaborationServiceInitializer(db);
        var refContext = Context("referencedata", actualDatabase);
        var collContext = Context("collaboration", actualDatabase);
        await identity.ApplyPendingAsync();
        foreach (var name in new[] { "service_call_nonce", "step_up_grant" })
        {
            Assert.Equal(0, await sql.Ado.GetIntAsync($"SELECT COALESCE(OBJECT_ID(N'dbo.pf05_{name}',N'U'),0)"));
            Assert.NotEqual(0, await sql.Ado.GetIntAsync($"SELECT COALESCE(OBJECT_ID(N'dbo.identity_{name}',N'U'),0)"));
            if (legacyObjects.TryGetValue(name, out var original))
            {
                Assert.Equal(original.Id, await sql.Ado.GetIntAsync($"SELECT OBJECT_ID(N'dbo.identity_{name}',N'U')"));
                Assert.Equal(original.Count, await sql.Ado.GetIntAsync($"SELECT COUNT(*) FROM dbo.identity_{name}"));
            }
        }
        await VerifySecurityStoresAsync(db);
        await systemData.ApplyPendingAsync();
        await reference.ApplyAsync(refContext, CancellationToken.None);
        var inspection = await collaboration.InspectAsync(collContext, CancellationToken.None);
        var plan = await collaboration.PlanAsync(collContext, inspection, CancellationToken.None);
        var result = await collaboration.ApplyAsync(collContext, plan, CancellationToken.None);
        Assert.True(result.Ready, result.Reason);
        Assert.True(await reference.MigrationValidAsync(CancellationToken.None));
        Assert.True(await reference.UnitOfMeasureSeedDataReadyAsync(CancellationToken.None));
        Assert.Equal(109, await sql.Ado.GetIntAsync("SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0"));
        var before = await LedgerSnapshotAsync(sql);

        await identity.ApplyPendingAsync();
        await systemData.ApplyPendingAsync();
        await reference.ApplyAsync(refContext, CancellationToken.None);
        await collaboration.ApplyAsync(collContext, plan, CancellationToken.None);
        Assert.Equal(before, await LedgerSnapshotAsync(sql));
        Assert.Equal(26, await sql.Ado.GetIntAsync("SELECT COUNT(*) FROM dbo.identity_schema_migrations WHERE migration_id <> 'SQLSERVER-999-Descriptions'"));
        Assert.Equal(59, await sql.Ado.GetIntAsync("SELECT COUNT(*) FROM dbo.system_data_schema_migrations WHERE migration_id <> 'SQLSERVER-999-Descriptions'"));
        Assert.Equal(1886, await sql.Ado.GetIntAsync("SELECT COUNT(*) FROM sys.extended_properties WHERE class=1 AND name=N'MS_Description'"));
        Assert.Equal(0, await sql.Ado.GetIntAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1"));

        // 执行与 UnifiedHost 相同的四服务初始化链：结构、系统种子、控制面事实都必须 Ready。
        foreach (var initializer in UnifiedHost.UnifiedHostLegacyPostgreSqlStartupChainTests.CreateInitializers(db))
        {
            var context = Context(initializer.ServiceKey, actualDatabase);
            var initial = await initializer.InspectAsync(context, CancellationToken.None);
            var initializationPlan = await initializer.PlanAsync(context, initial, CancellationToken.None);
            if (initializationPlan.RequiresApply)
                await initializer.ApplyAsync(context, initializationPlan, CancellationToken.None);
            var verified = await initializer.VerifyAsync(context, CancellationToken.None);
            Assert.True(verified.Ready, $"{initializer.ServiceKey}: {verified.Reason}");
            var repeat = await initializer.PlanAsync(context, verified, CancellationToken.None);
            Assert.False(repeat.RequiresApply, $"{initializer.ServiceKey}: observed={verified.ObservedVersion}, desired={repeat.DesiredVersion}; steps={string.Join(',', repeat.Steps)}");
        }
        await VerifyBusinessWritesAsync(db);
        await VerifyReferenceModulesAsync(db);
        await VerifySystemDataLocksAsync(db);
        await VerifyFileReferencesAsync(db);
        await VerifyTargetAdapterAsync(db);
    }

    private static async Task VerifyBusinessWritesAsync(SqlSugarDbContext db)
    {
        var tenant = "sql-check-" + Guid.NewGuid().ToString("N");
        var users = new UserRepository(db);
        var user = User.Create(tenant, "OPERATOR", "operator", "中文操作员", null, null, "acceptance-only-hash");
        await users.AddAsync(user);
        var stored = await users.GetByIdAsync(user.Id);
        Assert.NotNull(stored);
        Assert.Equal("中文操作员", stored.Name);
        Assert.Equal(user.CreatedOn, stored.CreatedOn);
        await Assert.ThrowsAsync<ConcurrencyException>(() => users.UpdateAsync(user, user.OptimisticVersion + 1, user.ConcurrencyVersion));
        await Assert.ThrowsAsync<SqlException>(() => users.AddAsync(User.Create(tenant, "OPERATOR2", "OPERATOR", "同登录名", null, null, "acceptance-only-hash")));

        var dictionary = new DictionaryRepository(db);
        var definition = new DictionaryDefinition("ACCEPTANCE", "中文字典", "人工接手说明", ReferenceScopeType.Tenant, tenant, null);
        definition.Update(definition.Name, definition.Description, [new DictionaryItem("READY", "已就绪", null, 0, true)]);
        await dictionary.CreateAsync(definition, null, CancellationToken.None);
        var loaded = await dictionary.GetAsync(tenant, definition.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("中文字典", loaded.Name);
        Assert.Equal(definition.CreatedOn, loaded.CreatedOn);
        Assert.Equal("已就绪", Assert.Single(loaded.Items).Name);
        var page = await dictionary.SearchAsync(tenant, new(Keyword: "中文"), CancellationToken.None);
        Assert.Equal(1, page.Total);
        var duplicate = new DictionaryDefinition("ACCEPTANCE", "重复", null, ReferenceScopeType.Tenant, tenant, null);
        var duplicateError = await Assert.ThrowsAsync<ReferenceDataException>(() => dictionary.CreateAsync(duplicate, null, CancellationToken.None));
        Assert.Equal("REF-DICT-DUPLICATE-NID", duplicateError.ErrorCode);

        var actor = new ReferenceDataActor(tenant, "OPERATOR", false, "sql-check");
        Exception? lastSqlError = null;
        db.SqlSugar.Aop.OnError = error => lastSqlError = error;
        var coding = new CodingRuleService(new CodingRuleRepository(db), TimeProvider.System);
        var rule = await coding.CreateAsync(actor, new("Tenant", "ACCEPTANCECODE", "生产编码", "WorkOrder", "SQL-{SEQ:6}", "Never"), CancellationToken.None);
        try
        {
            rule = await coding.PublishAsync(actor, rule.Id, new PublishOrDisableRequest(rule.OptimisticVersion, rule.ConcurrencyVersion, "SQL验收"), CancellationToken.None);
        }
        catch (ReferenceDataException) when (lastSqlError?.InnerException is SqlException)
        {
            var error = (SqlException)lastSqlError.InnerException;
            throw new InvalidOperationException($"SQL Server write error {error.Number}: {error.Message}");
        }
        var request = new GenerateCodeRequest("Tenant", tenant, rule.Revision);
        async Task<long> Generate(string key)
        {
            using var independent = OpenAcceptanceContext();
            var service = new CodingRuleService(new CodingRuleRepository(independent), TimeProvider.System);
            return (await service.GenerateAsync(actor, rule.NId, request, key, CancellationToken.None)).Sequence;
        }
        var sequences = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Generate("unique-" + i)));
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (long)i), sequences.Order());
        var replays = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Generate("same-request")));
        Assert.All(replays, sequence => Assert.Equal(9, sequence));

        var window = DateTimeOffset.UtcNow;
        var reservations = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var independent = OpenAcceptanceContext();
            return await new SqlCollaborationRepository(independent).ReserveComplianceViewBudgetAsync(tenant, "OPERATOR", window, 30, CancellationToken.None);
        }));
        Assert.Equal(6, reservations.Count(result => result.ReservedCount == 30));
        Assert.Equal(180, await db.SqlSugar.Ado.GetIntAsync("SELECT result_count FROM dbo.collaboration_compliance_view_budget WHERE tenant_n_id=@tenant", new SugarParameter("@tenant", tenant)));
        var collaboration = new SqlCollaborationRepository(db);
        Assert.True(await collaboration.HideMessageForUserAsync(tenant, "CONV", "MESSAGE", "OPERATOR", CancellationToken.None));
        Assert.False(await collaboration.HideMessageForUserAsync(tenant, "CONV", "MESSAGE", "OPERATOR", CancellationToken.None));
    }

    private static async Task VerifyReferenceModulesAsync(SqlSugarDbContext db)
    {
        var actor = new ReferenceDataActor("sql-check-" + Guid.NewGuid().ToString("N"), "OPERATOR", false, "sql-check");
        var ct = CancellationToken.None;
        var units = new UnitDimensionService(new UnitDimensionRepository(db));
        var unit = await units.CreateAsync(actor, new("Tenant", null, "VOLTAGE", "电压", null,
            "Ratio", "V", [new("V", "伏特", "V", "1", "0", 3, "ToEven", true, 0)]), ct);
        unit = await units.PublishAsync(actor, unit.Id, new(unit.OptimisticVersion, unit.ConcurrencyVersion, "验收"), ct);
        Assert.Equal("电压", (await units.GetAsync(actor, unit.Id, ct)).Name);

        var metadata = new MetadataSchemaService(new MetadataSchemaRepository(db), new DictionaryService(new DictionaryRepository(db)), units);
        var attribute = new MetadataAttributeRequest("CODE", "条码", "String", false, false, true, 0,
            null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        var schema = await metadata.CreateAsync(actor, new("Tenant", "MATERIAL", "物料", null, [attribute]), ct);
        schema = await metadata.PublishAsync(actor, schema.Id, new(schema.OptimisticVersion, schema.ConcurrencyVersion, "验收"), ct);
        Assert.Equal("条码", Assert.Single((await metadata.GetAsync(actor, schema.Id, ct)).Attributes).Name);

        var machines = new StateMachineService(new StateMachineRepository(db));
        var machine = await machines.CreateAsync(actor, new("Tenant", "WORKFLOW", "工单状态", null,
            [new("DRAFT", "草稿", null, true, false, "None", "#0088FF", 0), new("DONE", "完成", null, false, true, "Success", "#0088FF", 1)],
            [new("DRAFT", "FINISH", "完成", "DONE", null)]), ct);
        machine = await machines.PublishAsync(actor, machine.Id, new(machine.OptimisticVersion, machine.ConcurrencyVersion, "验收"), ct);
        Assert.Equal(2, (await machines.GetAsync(actor, machine.Id, ct)).Nodes.Count);

        var dictionaries = new DictionaryService(new DictionaryRepository(db));
        var parameters = new ParameterService(new ParameterRepository(db), dictionaries, NullLogger<ParameterService>.Instance);
        var domain = await parameters.CreateAsync(actor, new("Tenant", null, "PARAMS", "生产参数", null, "验收"), ct);
        using var value = JsonDocument.Parse("{\"中文\":true,\"count\":123}");
        domain = await parameters.AddKeyAsync(actor, domain.Id, new("SETTINGS", "设置", null, "Json", "Single", value.RootElement.Clone(),
            null, false, false, null, null, "Active", 0, "验收", domain.OptimisticVersion, domain.ConcurrencyVersion), ct);
        Assert.Single((await parameters.GetAsync(actor, domain.Id, ct)).Keys);

        var dynamic = new DynamicConfigurationService(new DynamicConfigurationRepository(db), dictionaries);
        var definition = await dynamic.CreateAsync(actor, new("Tenant", null, "TYPED", "类型值", null,
            [new("DECIMAL", "精确小数", "Decimal", false, true, 0, null) { Scale = 10 },
             new("INTEGER", "整数", "Integer", false, true, 1, null), new("JSON", "对象", "Json", false, true, 2, null),
             new("TIME", "时间", "DateTime", false, true, 3, null)]), ct);
        static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
        var mutation = await dynamic.AddRecordAsync(actor, definition.Id, new("FIRST", "记录", null, 0, true,
            new Dictionary<string, JsonElement>
            {
                ["DECIMAL"] = Json("999999999999999999.9999999999"), ["INTEGER"] = Json("9223372036854775807"),
                ["JSON"] = value.RootElement.Clone(), ["TIME"] = Json("\"2026-09-05T08:09:10+08:00\"")
            }, definition.OptimisticVersion, definition.ConcurrencyVersion), ct);
        definition = await dynamic.PublishAsync(actor, definition.Id, new(mutation.OptimisticVersion, mutation.ConcurrencyVersion, "验收"), ct);
        var record = await dynamic.GetRecordAsync(actor, definition.NId, "FIRST", definition.Revision, "Tenant", actor.TenantNId, null, ct);
        Assert.Equal(999999999999999999.9999999999m, record.Values["DECIMAL"].GetDecimal());
        Assert.Equal(long.MaxValue, record.Values["INTEGER"].GetInt64());
        Assert.True(record.Values["JSON"].GetProperty("中文").GetBoolean());
        Assert.Equal(TimeSpan.Zero, record.Values["TIME"].GetDateTimeOffset().Offset);
    }

    private static async Task VerifySystemDataLocksAsync(SqlSugarDbContext db)
    {
        var ct = CancellationToken.None;
        var tenant = "sql-check-" + Guid.NewGuid().ToString("N");
        var store = new DatabaseOrchestrationStore(db);
        var operation = DatabaseProvisionOperation.Enqueue(tenant, "PROBE", OperationKind.Plan, "Test", "systemdata", null,
            "1", "idempotency", new string('a', 64), DateTimeOffset.UtcNow.AddMinutes(30), "trace", "OPERATOR");
        await store.AddOperationAsync(operation, ct);
        var now = DateTimeOffset.UtcNow;
        var claims = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            using var independent = OpenAcceptanceContext();
            return await new DatabaseOrchestrationStore(independent).ClaimNextOperationAsync("sql-runner-" + i, now, TimeSpan.FromMinutes(5), ct);
        }));
        Assert.Single(claims, item => item?.Id == operation.Id);

        using var second = OpenAcceptanceContext();
        await using var firstLock = await new UserAssignmentAdvisoryLock(db).AcquireAsync(tenant, "OPERATOR", ct);
        var waiting = new UserAssignmentAdvisoryLock(second).AcquireAsync(tenant, "OPERATOR", ct);
        await Task.Delay(50, ct);
        Assert.False(waiting.IsCompleted);
        await firstLock.CommitAsync(ct);
        await using var secondLock = await waiting.WaitAsync(TimeSpan.FromSeconds(3), ct);
        await secondLock.CommitAsync(ct);

        var locker = new StandaloneInitializationLock(Options.Create(new SqlSugarOptions
        { DbType = DbType.SqlServer, ConnectionString = db.SqlSugar.CurrentConnectionConfig.ConnectionString }));
        await using var standalone = await locker.AcquireAsync(Context("identity", "industrial_platform_sql" + AcceptanceYear).DatabaseTarget, ct);
    }

    private static async Task VerifyTargetAdapterAsync(SqlSugarDbContext db)
    {
        var builder = new SqlConnectionStringBuilder(db.SqlSugar.CurrentConnectionConfig.ConnectionString);
        var parts = builder.DataSource.Split(',');
        var connection = new TargetDatabaseConnection("SqlServer", parts[0], parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1433,
            builder.InitialCatalog, "dbo", builder.UserID, builder.Password, builder.Encrypt != SqlConnectionEncryptOption.Optional, builder.TrustServerCertificate);
        var adapter = new SqlServerTargetDatabaseAdapter();
        var module = "pf06a_probe_" + Guid.NewGuid().ToString("N")[..12];
        var probe = module + "_values";
        var target = new ResolvedDatabaseTarget("Test", DatabaseTopologyMode.PerService, module, DatabaseProvider.SqlServer,
            module + "_db", builder.InitialCatalog, false, "dbo");
        var key = DatabaseTargetLockKey.FromTarget("Test", module, target);
        try
        {
            using (var first = await adapter.AcquireAsync(key, connection, TimeSpan.FromSeconds(1), CancellationToken.None))
            {
                Assert.NotNull(first);
                Assert.Null(await adapter.AcquireAsync(key, connection, TimeSpan.FromMilliseconds(30), CancellationToken.None));
            }
            using var reacquired = await adapter.AcquireAsync(key, connection, TimeSpan.FromSeconds(1), CancellationToken.None);
            Assert.NotNull(reacquired);
            Assert.True((await adapter.EnsureDatabaseAsync(target, connection, CancellationToken.None)).DatabaseExisted);
            var artifact = new DatabaseMigrationArtifact("sql-check", "1", "test-artifact", null, false,
                [new(1, "probe-create", $"CREATE TABLE dbo.{probe}(id int PRIMARY KEY,value nvarchar(200) NOT NULL); INSERT INTO dbo.{probe} VALUES(1,N'中文');", null, false)]);
            Assert.Equal(1, (await adapter.ApplyAsync(target, artifact, connection, module, CancellationToken.None)).AppliedStepCount);
            Assert.Equal(0, (await adapter.ApplyAsync(target, artifact, connection, module, CancellationToken.None)).AppliedStepCount);
            var inspected = await adapter.InspectAsync(target, new(null, connection, connection), module, CancellationToken.None);
            Assert.Equal("1", inspected.CurrentVersion);
            Assert.Single(inspected.AppliedStepIds);
            await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ApplyAsync(target,
                artifact with { Steps = [artifact.Steps[0] with { Sql = "SELECT 2" }] }, connection, module, CancellationToken.None));
            var failed = artifact with { Version = "2", Steps = [new(1, "probe-rollback", $"INSERT INTO dbo.{probe} VALUES(2,N'回滚'); THROW 51002,N'acceptance rollback',1;", null, false)] };
            await Assert.ThrowsAsync<SqlException>(() => adapter.ApplyAsync(target, failed, connection, module, CancellationToken.None));
            Assert.Equal(1, await db.SqlSugar.Ado.GetIntAsync($"SELECT COUNT(*) FROM dbo.{probe}"));
            var spec = TestInitializableService.ModuleASpec;
            var seedSpec = spec.Seeds[0];
            var seedArtifact = new SeedArtifact(seedSpec.SeedArtifactId, seedSpec.SeedVersion, new string('b',64), null,
                SeedExecutorKind.SqlBundle, [new(1, "seed", $"INSERT INTO dbo.{probe} VALUES(3,N'种子');", null)]);
            var seed = TestSeedSets.Build(spec, seedSpec, seedArtifact);
            var seedRequest = new SeedExecutionRequest(seed, seedArtifact, target, connection, module, "sql-check", "Test", null, "OP", "trace");
            var executor = new SqlSeedBundleExecutor();
            var seeds = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => executor.ExecuteAsync(seedRequest, CancellationToken.None)));
            Assert.All(seeds, item => Assert.True(item.Succeeded));
            Assert.Equal(2, await db.SqlSugar.Ado.GetIntAsync($"SELECT COUNT(*) FROM dbo.{probe}"));
            Assert.Equal(1, await db.SqlSugar.Ado.GetIntAsync($"SELECT COUNT(*) FROM dbo.{module}_seed_ledger"));
            var drift = seedArtifact with { Checksum = new string('c', 64) };
            Assert.False((await executor.ExecuteAsync(seedRequest with { Seed = TestSeedSets.Build(spec, seedSpec, drift), Artifact = drift }, CancellationToken.None)).Succeeded);
        }
        finally
        {
            // 只删除本方法生成的两个探针表；服务表、迁移账本和业务数据保留。
            await db.SqlSugar.Ado.ExecuteCommandAsync($"DROP TABLE IF EXISTS dbo.{probe}; DROP TABLE IF EXISTS dbo.{module}_schema_migrations; DROP TABLE IF EXISTS dbo.{module}_seed_ledger;");
        }
    }

    private static async Task VerifyFileReferencesAsync(SqlSugarDbContext db)
    {
        var tenant = "sql-check-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var ct = CancellationToken.None;
        // 仅验证持久化与删除互斥；这条隔离样本不是一次实际上传/病毒扫描验收。
        await db.SqlSugar.Insertable(new FileObjectTable
        {
            Id = Guid.NewGuid(), TenantNId = tenant, FileNId = "FILE", UploadSessionNId = "UPLOAD", OwnerUserNId = "OPERATOR",
            FileName = "中文测试.txt", ContentType = "text/plain", ContentLength = 1, Sha256 = new string('a', 64), StorageKey = "acceptance/FILE",
            ScanStatus = "Clean", DeletionStatus = "Active", CreatedOn = now, LastUpdatedOn = now
        }).ExecuteCommandAsync(ct);
        var store = new SystemDataOperationalStore(db);
        var file = await store.GetFileAsync(tenant, "FILE", ct);
        Assert.NotNull(file);
        await store.InsertBusinessReferenceAsync(new(tenant, "REFERENCE", "FILE", "CONV", "MESSAGE", "ATTACHMENT", "OPERATOR", "Chat", "collaboration", "Active", 1, now, null), ct);
        await store.InsertLegalHoldAsync(new(tenant, "CASE", "FILE", new string('b',64), 1, "collaboration", "Active", now, now, null), ct);
        Assert.True(await store.HasActiveReferencesAsync(tenant, "FILE", ct));
        Assert.True(await store.HasActiveLegalHoldsAsync(tenant, "FILE", ct));
        Assert.False(await store.TryRequestDeletionAsync(file with { DeletionStatus = "DeletionRequested" }, ct));
        Assert.True(await store.ReleaseBusinessReferenceAsync(tenant, "FILE", "REFERENCE", 1, now, ct));
        Assert.False(await store.TryRequestDeletionAsync(file with { DeletionStatus = "DeletionRequested" }, ct));
        Assert.True(await store.ReleaseLegalHoldAsync(tenant, "CASE", "FILE", new string('b',64), 1, now, ct));
        Assert.True(await store.TryRequestDeletionAsync(file with { DeletionStatus = "DeletionRequested" }, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InsertLegalHoldAsync(new(tenant, "CASE2", "FILE", new string('b',64), 1, "collaboration", "Active", now, now, null), ct));
    }

    private static async Task VerifySecurityStoresAsync(SqlSugarDbContext db)
    {
        var marker = "rename-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var proof = new string('c', 32) + Guid.NewGuid().ToString("N");
        var nonce = new IndustrialPlatform.Identity.Infrastructure.Security.TrustedServiceCallNonceStore(db);
        var grants = new IndustrialPlatform.Identity.Infrastructure.Authentication.StepUpGrantStore(db);
        try
        {
            Assert.True(await nonce.TryRegisterAsync(marker, marker, now.AddMinutes(5), CancellationToken.None));
            Assert.False(await nonce.TryRegisterAsync(marker, marker, now.AddMinutes(5), CancellationToken.None));
            await grants.IssueAsync(new(marker, "user", "session", "1", "action", marker, proof, proof, proof, proof, now, now.AddMinutes(5)), CancellationToken.None);
            var consumed = await grants.ConsumeAsync(proof, marker, "user", "session", "1", "action", marker, proof, proof, "collaboration", now, CancellationToken.None);
            Assert.NotNull(consumed);
            Assert.Equal(consumed, await grants.ConsumeAsync(proof, marker, "user", "session", "1", "action", marker, proof, proof, "collaboration", now, CancellationToken.None));
            Assert.Null(await grants.ConsumeAsync(proof, marker, "user", "session", "1", "other-action", marker, proof, proof, "collaboration", now, CancellationToken.None));
        }
        finally
        {
            await db.SqlSugar.Ado.ExecuteCommandAsync("DELETE FROM dbo.identity_service_call_nonce WHERE issuer=@marker; DELETE FROM dbo.identity_step_up_grant WHERE tenant_n_id=@marker", new SugarParameter("@marker", marker));
        }
    }

    internal static SqlSugarDbContext OpenAcceptanceContext()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props"))) directory = directory.Parent;
        Assert.NotNull(directory);
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "src/backend/appsettings.Development.local.json")));
        var connectionString = config.RootElement.GetProperty("SqlServerAcceptance").GetProperty(AcceptanceYear).GetProperty("ConnectionString").GetString();
        var builder = new SqlConnectionStringBuilder(connectionString);
        // 防止误用日常库；测试不输出连接串、服务器、用户名或密码。
        Assert.Equal("industrial_platform_sql" + AcceptanceYear, builder.InitialCatalog);
        builder.ConnectTimeout = 15;
        return new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        { DbType = DbType.SqlServer, ConnectionString = builder.ConnectionString, CommandTimeoutSeconds = 30 }));
    }

    private static string AcceptanceYear
    {
        get
        {
            var year = Environment.GetEnvironmentVariable("PF06A_SQLSERVER_ENGINE_YEAR") ?? "2019";
            Assert.True(year is "2019" or "2022", "验收年份仅允许 2019 或 2022。");
            return year;
        }
    }

    private static ServiceInitializationContext Context(string service, string database) => new(
        "Development", "tenant-sql-acceptance", "sql-acceptance", service, service,
        new ResolvedDatabaseTarget("Development", DatabaseTopologyMode.Shared, service, DatabaseProvider.SqlServer,
            $"{service}_db", database, true, "dbo"), "", ServiceInitializationPolicy.Standard, "trace-sql-acceptance");

    private static async Task<string> LedgerSnapshotAsync(ISqlSugarClient sql)
    {
        var counts = new List<int>();
        foreach (var table in new[] { "identity_schema_migrations", "system_data_schema_migrations", "reference_data_schema_migrations", "collaboration_schema_migrations", "reference_data_seed_ledger" })
            counts.Add(await sql.Ado.GetIntAsync($"SELECT COUNT(*) FROM dbo.{table}"));
        return string.Join(',', counts);
    }
}
