using System.Text.Json;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Application.CodingRule;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Application.DynamicProperty;
using IndustrialPlatform.ReferenceData.Application.Metadata;
using IndustrialPlatform.ReferenceData.Application.Parameter;
using IndustrialPlatform.ReferenceData.Application.StateMachine;
using IndustrialPlatform.ReferenceData.Application.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Contracts.CodingRule;
using IndustrialPlatform.ReferenceData.Contracts.Dictionary;
using IndustrialPlatform.ReferenceData.Contracts.DynamicProperty;
using IndustrialPlatform.ReferenceData.Contracts.Metadata;
using IndustrialPlatform.ReferenceData.Contracts.Parameter;
using IndustrialPlatform.ReferenceData.Contracts.StateMachine;
using IndustrialPlatform.ReferenceData.Contracts.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Domain.CodingRule;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Dictionary;
using IndustrialPlatform.ReferenceData.Domain.DynamicProperty;
using IndustrialPlatform.ReferenceData.Domain.Metadata;
using IndustrialPlatform.ReferenceData.Domain.Parameter;
using IndustrialPlatform.ReferenceData.Domain.StateMachine;
using IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndustrialPlatform.ReferenceData.Tests.Caching;

public sealed class ReferenceDataCacheTests
{
    private static readonly ReferenceDataActor TenantA = new("TENANT-A", "USER-A", false, "trace");

    [Fact]
    public void Keys_and_ttls_match_the_reference_data_cache_contract()
    {
        Assert.Equal("tenant-TENANT-A", ReferenceDataCacheKeys.TenantKey("TENANT-A"));
        Assert.NotEqual(ReferenceDataCacheKeys.TenantKey(null), ReferenceDataCacheKeys.TenantKey("platform"));
        Assert.Equal("referencedata:v1:tenant-TENANT-A:dictionary:STATUS",
            ReferenceDataCacheKeys.Dictionary(ReferenceDataCacheKeys.TenantKey("TENANT-A"), "STATUS").Value);
        Assert.Equal("referencedata:v1:TENANT-A:configuration:_:MES:MODE",
            ReferenceDataCacheKeys.Configuration("TENANT-A", "_", "MES", "MODE").Value);
        Assert.Equal("referencedata:v1:TENANT-A:configuration-domain:_:MES:current",
            ReferenceDataCacheKeys.ConfigurationDomain("TENANT-A", "_", "MES", "current").Value);
        Assert.Equal("referencedata:dynamic-property:v1:TENANT-A:dynamic-config:FORM:3:page-1",
            ReferenceDataCacheKeys.DynamicConfiguration("TENANT-A", "FORM", "3", "page-1").Value);
        Assert.Equal("referencedata:v1:TENANT-A:metadata:MATERIAL",
            ReferenceDataCacheKeys.Metadata("TENANT-A", "MATERIAL").Value);
        Assert.Equal("referencedata:v1:TENANT-A:coding-rule:ORDER",
            ReferenceDataCacheKeys.CodingRule("TENANT-A", "ORDER").Value);
        Assert.Equal("referencedata:v1:Tenant:TENANT-A:unit-of-measure:MASS:current",
            ReferenceDataCacheKeys.UnitOfMeasure("Tenant", "TENANT-A", "MASS", "current").Value);
        Assert.Equal("referencedata:v1:Tenant:TENANT-A:state-machine:ORDER:2",
            ReferenceDataCacheKeys.StateMachine("Tenant", "TENANT-A", "ORDER", "2").Value);

        Assert.Equal(TimeSpan.FromMinutes(30), ReferenceDataCacheTtl.Dictionary);
        Assert.Equal(TimeSpan.FromMinutes(5), ReferenceDataCacheTtl.Parameter);
        Assert.Equal(TimeSpan.FromMinutes(15), ReferenceDataCacheTtl.DynamicSchema);
        Assert.Equal(TimeSpan.FromMinutes(5), ReferenceDataCacheTtl.DynamicRecords);
        Assert.Equal(TimeSpan.FromMinutes(15), ReferenceDataCacheTtl.Metadata);
        Assert.Equal(TimeSpan.FromMinutes(15), ReferenceDataCacheTtl.CodingRule);
        Assert.Equal(TimeSpan.FromMinutes(5), ReferenceDataCacheTtl.UnitCurrent);
        Assert.Equal(TimeSpan.FromMinutes(30), ReferenceDataCacheTtl.UnitRevision);
        Assert.Equal(TimeSpan.FromMinutes(5), ReferenceDataCacheTtl.StateMachineCurrent);
        Assert.Equal(TimeSpan.FromMinutes(30), ReferenceDataCacheTtl.StateMachineRevision);
    }

    [Fact]
    public async Task Dictionary_effective_reads_use_cache_aside_and_isolate_request_tenants()
    {
        var repository = new DictionaryRepositoryStub(PublishedDictionary());
        var cache = new RecordingCache();
        var service = new DictionaryService(repository, cache);

        var first = await service.GetEffectiveAsync(TenantA, "status", null, CancellationToken.None);
        var second = await service.GetEffectiveAsync(TenantA, "STATUS", null, CancellationToken.None);
        var tenantB = await service.GetEffectiveAsync(TenantA with { TenantNId = "TENANT-B" }, "STATUS", null,
            CancellationToken.None);

        Assert.Equal("STATUS", first.NId);
        Assert.Equal(first, second);
        Assert.Equal("STATUS", tenantB.NId);
        Assert.Equal(2, repository.EffectiveReads);
        Assert.Contains("referencedata:v1:tenant-TENANT-A:dictionary:STATUS", cache.Values.Keys);
        Assert.Contains("referencedata:v1:tenant-TENANT-B:dictionary:STATUS", cache.Values.Keys);
        Assert.All(cache.Writes, write => Assert.Equal(ReferenceDataCacheTtl.Dictionary, write.Ttl));
    }

    [Fact]
    public async Task Dictionary_not_found_is_never_cached()
    {
        var repository = new DictionaryRepositoryStub(null);
        var cache = new RecordingCache();
        var service = new DictionaryService(repository, cache);

        await Assert.ThrowsAsync<ReferenceDataException>(() =>
            service.GetEffectiveAsync(TenantA, "missing", null, CancellationToken.None));
        await Assert.ThrowsAsync<ReferenceDataException>(() =>
            service.GetEffectiveAsync(TenantA, "missing", null, CancellationToken.None));

        Assert.Equal(2, repository.EffectiveReads);
        Assert.Empty(cache.Writes);
    }

    [Fact]
    public async Task Cache_fill_started_before_invalidation_cannot_restore_the_stale_value()
    {
        var cache = new RecordingCache();
        var key = ReferenceDataCacheKeys.Dictionary(
            ReferenceDataCacheKeys.TenantKey(TenantA.TenantNId), "STATUS");
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var read = cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.Dictionary, async () =>
        {
            loadStarted.SetResult();
            await releaseLoad.Task;
            return "stale-database-snapshot";
        }, CancellationToken.None);
        await loadStarted.Task;

        await cache.InvalidateAsync(new(key.Module, key.Value, key.GenerationScope), CancellationToken.None);
        releaseLoad.SetResult();

        Assert.Equal("stale-database-snapshot", await read);
        Assert.DoesNotContain(key.Value, cache.Values.Keys);
        Assert.Empty(cache.Writes);
    }

    [Fact]
    public async Task Invalidation_generation_is_scoped_to_the_changed_logical_definition()
    {
        var cache = new RecordingCache();
        var status = ReferenceDataCacheKeys.Dictionary(
            ReferenceDataCacheKeys.TenantKey(TenantA.TenantNId), "STATUS");
        var category = ReferenceDataCacheKeys.Dictionary(
            ReferenceDataCacheKeys.TenantKey(TenantA.TenantNId), "CATEGORY");
        await cache.SetAsync(status, "status-v1", TimeSpan.FromMinutes(1), CancellationToken.None);
        await cache.SetAsync(category, "category-v1", TimeSpan.FromMinutes(1), CancellationToken.None);

        await cache.InvalidateAsync(ReferenceDataCacheKeys.Dictionaries("STATUS"), CancellationToken.None);

        Assert.Null(await cache.GetAsync<string>(status, CancellationToken.None));
        Assert.Equal("category-v1", await cache.GetAsync<string>(category, CancellationToken.None));
        Assert.NotEqual(status.GenerationKey, category.GenerationKey);
    }

    [Fact]
    public async Task Dictionary_write_invalidates_after_repository_transaction_completes()
    {
        var definition = DraftDictionary();
        var order = new List<string>();
        var repository = new DictionaryRepositoryStub(definition, order);
        var cache = new RecordingCache(order);
        var service = new DictionaryService(repository, cache);
        var request = new UpdateDictionaryRequest("Changed", null,
            [new("READY", "Ready", null, 0, true)], definition.OptimisticVersion,
            definition.ConcurrencyVersion);

        await service.UpdateAsync(TenantA, definition.Id, request, CancellationToken.None);

        Assert.Equal(["repository-save", "cache-invalidate"], order);
        Assert.Equal("referencedata:v1:*:dictionary:STATUS", Assert.Single(cache.Invalidations).Value);
    }

    [Fact]
    public async Task Parameter_key_and_domain_reads_are_cached_with_the_same_domain_invalidation_boundary()
    {
        using var json = JsonDocument.Parse("\"AUTO\"");
        var domain = new ConfigurationAppDomain("MES", "MES", null, ReferenceScopeType.Tenant,
            TenantA.TenantNId, null);
        domain.AddKey("MODE", new("Mode", null, ReferenceDataType.String,
            ConfigurationValueMode.Single, ConfigurationScalar.Parse(ReferenceDataType.String,
                json.RootElement.Clone()), null, false, false, null, null, ConfigurationStatus.Active, 0));
        var repository = new ParameterRepositoryStub(domain);
        var cache = new RecordingCache();
        var service = new ParameterService(repository,
            new DictionaryService(new DictionaryRepositoryStub(null)), NullLogger<ParameterService>.Instance, cache);

        await service.ResolveAsync(TenantA, "mes", "mode", null, CancellationToken.None);
        await service.ResolveAsync(TenantA, "MES", "MODE", null, CancellationToken.None);
        await service.ResolveDomainAsync(TenantA, "MES", null, CancellationToken.None);
        await service.ResolveDomainAsync(TenantA, "mes", null, CancellationToken.None);

        Assert.Equal(2, repository.ActiveScopeReads);
        Assert.Contains(cache.Writes, item => item.Key.Value ==
            "referencedata:v1:tenant-TENANT-A:configuration:_:MES:MODE" && item.Ttl == ReferenceDataCacheTtl.Parameter);
        Assert.Contains(cache.Writes, item => item.Key.Value ==
            "referencedata:v1:tenant-TENANT-A:configuration-domain:_:MES:current" && item.Ttl == ReferenceDataCacheTtl.Parameter);
    }

    [Fact]
    public async Task Dynamic_schema_and_revision_record_page_use_their_distinct_ttls()
    {
        var definition = new DynamicConfigDefinition("FORM", "Form", null, ReferenceScopeType.Tenant,
            TenantA.TenantNId, null);
        definition.Update(definition.Name, null,
            [new("CODE", new("Code", ReferenceDataType.String, false, true, 0, null, null, null,
                null, null, null, null, null, null, null))]);
        definition.AddRecord("ROW1", new(null, null, 0, true), []);
        definition.Publish(TenantA.UserNId);
        var repository = new DynamicRepositoryStub(definition);
        var cache = new RecordingCache();
        var service = new DynamicConfigurationService(repository,
            new DictionaryService(new DictionaryRepositoryStub(null)), cache);

        await service.GetSchemaAsync(TenantA, "form", null, CancellationToken.None);
        await service.GetSchemaAsync(TenantA, "FORM", null, CancellationToken.None);
        await service.GetRecordsAsync(TenantA, "FORM", 1, "Tenant", TenantA.TenantNId, null,
            new(), CancellationToken.None);
        await service.GetRecordsAsync(TenantA, "form", 1, "Tenant", TenantA.TenantNId, null,
            new(), CancellationToken.None);

        Assert.Equal(1, repository.EffectiveReads);
        Assert.Equal(2, repository.SnapshotReads);
        Assert.Equal(1, repository.RecordReads);
        Assert.Contains(cache.Writes, item => item.Key.Value.EndsWith(":FORM:current:schema", StringComparison.Ordinal)
            && item.Ttl == ReferenceDataCacheTtl.DynamicSchema);
        Assert.Contains(cache.Writes, item => item.Key.Value.Contains(":FORM:1:", StringComparison.Ordinal)
            && item.Ttl == ReferenceDataCacheTtl.DynamicRecords);
    }

    [Fact]
    public async Task Metadata_effective_schema_is_cached_per_request_tenant()
    {
        var schema = new EntitySchema("MATERIAL", "Material", null, ReferenceScopeType.Tenant,
            TenantA.TenantNId, null);
        schema.Update(schema.Name, null,
            [new("NAME", new("Name", ReferenceDataType.String, false, false, true, 0, null,
                null, null, null, null, null, null, null, null, null, null, null, null, null,
                null, null))]);
        schema.Publish(TenantA.UserNId);
        var repository = new MetadataRepositoryStub(schema);
        var cache = new RecordingCache();
        var service = new MetadataSchemaService(repository,
            new DictionaryService(new DictionaryRepositoryStub(null)),
            new UnitDimensionService(new UnitRepositoryStub(null)), cache);

        await service.GetEffectiveAsync(TenantA, "material", null, CancellationToken.None);
        await service.GetEffectiveAsync(TenantA, "MATERIAL", null, CancellationToken.None);

        Assert.Equal(1, repository.EffectiveReads);
        var write = Assert.Single(cache.Writes);
        Assert.Equal("referencedata:v1:tenant-TENANT-A:metadata:MATERIAL", write.Key.Value);
        Assert.Equal(ReferenceDataCacheTtl.Metadata, write.Ttl);
    }

    [Fact]
    public async Task Coding_preview_caches_definition_but_generate_always_uses_repository_sequence_and_idempotency()
    {
        var rule = new CodingRuleDefinition("ORDER", "Order", ReferenceScopeType.Tenant,
            TenantA.TenantNId, null);
        rule.Update(rule.Name, "SALESORDER", "SO-{YYYY}-{SEQ:4}", CodingResetPolicy.Yearly);
        rule.Publish(TenantA.UserNId);
        var repository = new CodingRepositoryStub(rule);
        var cache = new RecordingCache();
        var service = new CodingRuleService(repository, TimeProvider.System, cache);
        var preview = new PreviewCodeRequest("Tenant", TenantA.TenantNId, 1);

        await service.PreviewAsync(TenantA, "order", preview, CancellationToken.None);
        await service.PreviewAsync(TenantA, "ORDER", preview, CancellationToken.None);
        var cacheReadsBeforeGenerate = cache.Reads;
        await service.GenerateAsync(TenantA, "ORDER",
            new("Tenant", TenantA.TenantNId, 1), "request-1", CancellationToken.None);

        Assert.Equal(2, repository.RevisionReads);
        Assert.Equal(1, repository.GenerationCalls);
        Assert.Equal(cacheReadsBeforeGenerate, cache.Reads);
        var write = Assert.Single(cache.Writes);
        Assert.Equal("referencedata:v1:tenant-TENANT-A:coding-rule:ORDER", write.Key.Value);
        Assert.Equal(ReferenceDataCacheTtl.CodingRule, write.Ttl);
    }

    [Fact]
    public async Task Unit_current_list_and_fixed_revision_have_separate_cache_lifetimes()
    {
        var dimension = PublishedDimension();
        var repository = new UnitRepositoryStub(dimension);
        var cache = new RecordingCache();
        var service = new UnitDimensionService(repository, cache);

        await service.GetCurrentAsync(TenantA, "mass", "Tenant", TenantA.TenantNId,
            CancellationToken.None);
        await service.GetCurrentAsync(TenantA, "MASS", "Tenant", TenantA.TenantNId,
            CancellationToken.None);
        await service.GetRevisionAsync(TenantA, "MASS", 1, "Tenant", TenantA.TenantNId,
            CancellationToken.None);
        await service.GetRevisionAsync(TenantA, "mass", 1, "Tenant", TenantA.TenantNId,
            CancellationToken.None);
        await service.ListAvailableAsync(TenantA, new(), CancellationToken.None);
        await service.ListAvailableAsync(TenantA, new(), CancellationToken.None);

        Assert.Equal(1, repository.CurrentReads);
        Assert.Equal(1, repository.RevisionReads);
        Assert.Equal(1, repository.ListReads);
        Assert.Contains(cache.Writes, item => item.Key.Value.EndsWith(":MASS:current", StringComparison.Ordinal)
            && item.Ttl == ReferenceDataCacheTtl.UnitCurrent);
        Assert.Contains(cache.Writes, item => item.Key.Value.EndsWith(":MASS:1", StringComparison.Ordinal)
            && item.Ttl == ReferenceDataCacheTtl.UnitRevision);
        Assert.Contains(cache.Writes, item => item.Key.Value.Contains(":unit-of-measure:list:", StringComparison.Ordinal)
            && item.Ttl == ReferenceDataCacheTtl.UnitCurrent);
    }

    [Fact]
    public async Task Unit_publish_invalidates_the_warm_previous_revision_snapshot()
    {
        var previous = PublishedDimension();
        var draft = previous.Clone(2);
        var repository = new UnitRepositoryStub(previous, draft);
        var cache = new RecordingCache();
        var service = new UnitDimensionService(repository, cache);
        Assert.Equal("Published", (await service.GetRevisionAsync(TenantA, "MASS", 1,
            "Tenant", TenantA.TenantNId, CancellationToken.None)).Status);

        await service.PublishAsync(TenantA, draft.Id,
            new(draft.OptimisticVersion, draft.ConcurrencyVersion, "Publish revision two"),
            CancellationToken.None);

        Assert.Contains(cache.Invalidations, pattern =>
            pattern.Value.EndsWith(":MASS:1", StringComparison.Ordinal));
        Assert.Equal("Superseded", (await service.GetRevisionAsync(TenantA, "MASS", 1,
            "Tenant", TenantA.TenantNId, CancellationToken.None)).Status);
        Assert.Equal(2, repository.RevisionReads);
    }

    [Fact]
    public async Task State_machine_current_list_and_evaluation_reuse_revision_cache()
    {
        var definition = PublishedStateMachine();
        var repository = new StateMachineRepositoryStub(definition);
        var cache = new RecordingCache();
        var service = new StateMachineService(repository, cache);

        await service.GetCurrentAsync(TenantA, "order", "Tenant", TenantA.TenantNId,
            CancellationToken.None);
        await service.GetCurrentAsync(TenantA, "ORDER", "Tenant", TenantA.TenantNId,
            CancellationToken.None);
        await service.GetRevisionAsync(TenantA, "ORDER", 1, "Tenant", TenantA.TenantNId,
            CancellationToken.None);
        var evaluation = await service.EvaluateAsync(TenantA, "order",
            new("Tenant", TenantA.TenantNId, 1, "draft", "submit"), CancellationToken.None);
        await service.ListAvailableAsync(TenantA, new(), CancellationToken.None);
        await service.ListAvailableAsync(TenantA, new(), CancellationToken.None);

        Assert.True(evaluation.AllowedByDefinition);
        Assert.Equal("SUBMITTED", evaluation.ToStatusNId);
        Assert.Equal(1, repository.CurrentReads);
        Assert.Equal(1, repository.RevisionReads);
        Assert.Equal(1, repository.ListReads);
        Assert.Contains(cache.Writes, item => item.Key.Value.EndsWith(":ORDER:current", StringComparison.Ordinal)
            && item.Ttl == ReferenceDataCacheTtl.StateMachineCurrent);
        Assert.Contains(cache.Writes, item => item.Key.Value.EndsWith(":ORDER:1", StringComparison.Ordinal)
            && item.Ttl == ReferenceDataCacheTtl.StateMachineRevision);
    }

    [Fact]
    public async Task State_machine_publish_invalidates_the_warm_previous_revision_snapshot()
    {
        var previous = PublishedStateMachine();
        var draft = previous.Clone(2);
        var repository = new StateMachineRepositoryStub(previous, draft);
        var cache = new RecordingCache();
        var service = new StateMachineService(repository, cache);
        Assert.Equal("Published", (await service.GetRevisionAsync(TenantA, "ORDER", 1,
            "Tenant", TenantA.TenantNId, CancellationToken.None)).Status);

        await service.PublishAsync(TenantA, draft.Id,
            new(draft.OptimisticVersion, draft.ConcurrencyVersion, "Publish revision two"),
            CancellationToken.None);

        Assert.Contains(cache.Invalidations, pattern =>
            pattern.Value.EndsWith(":ORDER:1", StringComparison.Ordinal));
        Assert.Equal("Superseded", (await service.GetRevisionAsync(TenantA, "ORDER", 1,
            "Tenant", TenantA.TenantNId, CancellationToken.None)).Status);
        Assert.Equal(2, repository.RevisionReads);
    }

    private static DictionaryDefinition DraftDictionary()
    {
        var definition = new DictionaryDefinition("STATUS", "Status", null, ReferenceScopeType.Tenant,
            TenantA.TenantNId, null);
        definition.Update(definition.Name, null, [new("READY", "Ready", null, 0, true)]);
        return definition;
    }

    private static DictionaryDefinition PublishedDictionary()
    {
        var definition = DraftDictionary();
        definition.Publish(TenantA.UserNId);
        return definition;
    }

    private static UnitDimension PublishedDimension()
    {
        var dimension = new UnitDimension("MASS", "Mass", null, ReferenceScopeType.Tenant,
            TenantA.TenantNId, null);
        dimension.Update(dimension.Name, null, UnitConversionKind.Ratio, "KG",
            [new("KG", "Kilogram", "kg", 1m, 0m, 3, UnitRoundingMode.ToEven, true, 0)]);
        dimension.Publish(TenantA.UserNId);
        return dimension;
    }

    private static StateMachineDefinition PublishedStateMachine()
    {
        var definition = new StateMachineDefinition("ORDER", "Order", null, ReferenceScopeType.Tenant,
            TenantA.TenantNId, null);
        definition.Update(definition.Name, null,
            [
                new("DRAFT", "Draft", null, true, false, StateOutcome.None, null, 0),
                new("SUBMITTED", "Submitted", null, false, true, StateOutcome.Success, null, 1),
            ],
            [new("DRAFT", "SUBMIT", "Submit", "SUBMITTED", null)]);
        definition.Publish(TenantA.UserNId);
        return definition;
    }

    private sealed class RecordingCache(List<string>? order = null) : IReferenceDataCache
    {
        private readonly Dictionary<string, string> generations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> valueGenerations = new(StringComparer.Ordinal);
        public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);
        public List<(ReferenceDataCacheKey Key, TimeSpan Ttl)> Writes { get; } = [];
        public List<ReferenceDataCachePattern> Invalidations { get; } = [];
        public int Reads { get; private set; }

        public Task<T?> GetAsync<T>(ReferenceDataCacheKey key, CancellationToken cancellationToken = default)
            where T : class
        {
            Reads++;
            return Task.FromResult(Values.TryGetValue(key.Value, out var value)
                && valueGenerations.GetValueOrDefault(key.Value) == Generation(key.GenerationKey)
                    ? (T?)value
                    : null);
        }

        public Task SetAsync<T>(ReferenceDataCacheKey key, T value, TimeSpan ttl,
            CancellationToken cancellationToken = default) where T : class
        {
            return SetIfGenerationAsync(key, value, ttl, Generation(key.GenerationKey),
                cancellationToken);
        }

        public Task<string?> GetGenerationAsync(string moduleKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(Generation(moduleKey));

        public Task<bool> SetIfGenerationAsync<T>(ReferenceDataCacheKey key, T value, TimeSpan ttl,
            string? expectedGeneration, CancellationToken cancellationToken = default) where T : class
        {
            if (Generation(key.GenerationKey) != expectedGeneration)
                return Task.FromResult(false);
            Values[key.Value] = value;
            valueGenerations[key.Value] = expectedGeneration!;
            Writes.Add((key, ttl));
            return Task.FromResult(true);
        }

        public Task InvalidateAsync(ReferenceDataCachePattern pattern,
            CancellationToken cancellationToken = default)
        {
            order?.Add("cache-invalidate");
            Invalidations.Add(pattern);
            generations[pattern.GenerationKey] = Guid.NewGuid().ToString("N");
            return Task.CompletedTask;
        }

        private string Generation(string generationKey) =>
            generations.GetValueOrDefault(generationKey) ?? "0";
    }

    private sealed class DictionaryRepositoryStub(
        DictionaryDefinition? definition,
        List<string>? order = null) : IDictionaryRepository
    {
        public int EffectiveReads { get; private set; }

        public Task<(IReadOnlyList<DictionarySummaryDto> Items, long Total)> SearchAsync(string tenantNId,
            DictionaryQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DictionaryDefinition?> GetAsync(string tenantNId, Guid id,
            CancellationToken cancellationToken) => Task.FromResult(definition?.Id == id ? definition : null);

        public Task<DictionaryDefinition?> GetEffectiveAsync(string tenantNId, string nId,
            CancellationToken cancellationToken)
        {
            EffectiveReads++;
            return Task.FromResult(definition);
        }

        public Task<DictionaryDefinition?> GetPublishedInScopeAsync(DictionaryDefinition value,
            CancellationToken cancellationToken) => Task.FromResult<DictionaryDefinition?>(null);

        public Task<DictionaryDefinition?> GetLastPublishedAsync(DictionaryDefinition value,
            CancellationToken cancellationToken) => Task.FromResult<DictionaryDefinition?>(null);

        public Task<int> GetNextRevisionAsync(DictionaryDefinition value, CancellationToken cancellationToken) =>
            Task.FromResult(2);

        public Task CreateAsync(DictionaryDefinition value, DictionaryChange? source,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveAsync(IReadOnlyList<DictionaryChange> changes, CancellationToken cancellationToken)
        {
            order?.Add("repository-save");
            return Task.CompletedTask;
        }
    }

    private sealed class ParameterRepositoryStub(ConfigurationAppDomain domain) : IParameterRepository
    {
        public int ActiveScopeReads { get; private set; }

        public Task<(IReadOnlyList<ConfigurationDomainSummaryDto> Items, long Total)> SearchAsync(string tenantNId,
            ParameterQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ConfigurationAppDomain?> GetAsync(string tenantNId, Guid id,
            CancellationToken cancellationToken) => Task.FromResult<ConfigurationAppDomain?>(domain);

        public Task<IReadOnlyList<ConfigurationAppDomain>> GetActiveScopesAsync(string tenantNId, string nId,
            CancellationToken cancellationToken)
        {
            ActiveScopeReads++;
            return Task.FromResult<IReadOnlyList<ConfigurationAppDomain>>([domain]);
        }

        public Task CreateAsync(ConfigurationAppDomain value, ConfigurationHistoryDto history,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveAsync(ConfigurationAppDomain value, long expectedVersion, Guid expectedToken,
            ConfigurationHistoryDto history, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<(IReadOnlyList<ConfigurationHistoryDto> Items, long Total)> HistoryAsync(Guid domainId,
            Guid? keyId, int pageIndex, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class DynamicRepositoryStub(DynamicConfigDefinition definition) : IDynamicConfigurationRepository
    {
        public int EffectiveReads { get; private set; }
        public int SnapshotReads { get; private set; }
        public int RecordReads { get; private set; }

        public Task<(IReadOnlyList<DynamicConfigurationSummaryDto> Items, long Total)> SearchAsync(string tenantNId,
            DynamicConfigurationQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DynamicConfigDefinition?> GetAsync(string tenantNId, Guid id, bool includeRecords,
            CancellationToken cancellationToken) => Task.FromResult<DynamicConfigDefinition?>(definition);
        public Task<(int Records, long Values)> CountAsync(Guid definitionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DynamicConfigDefinition?> GetEffectiveAsync(string tenantNId, string nId,
            CancellationToken cancellationToken)
        {
            EffectiveReads++;
            return Task.FromResult<DynamicConfigDefinition?>(definition);
        }
        public Task<DynamicConfigDefinition?> GetSnapshotAsync(string tenantNId, string nId, string sourceScope,
            int revision, CancellationToken cancellationToken)
        {
            SnapshotReads++;
            return Task.FromResult<DynamicConfigDefinition?>(definition);
        }
        public Task<DynamicConfigDefinition?> GetPublishedInScopeAsync(DynamicConfigDefinition value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DynamicConfigDefinition?> GetLastPublishedAsync(DynamicConfigDefinition value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextRevisionAsync(DynamicConfigDefinition value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<DynamicConfigRecord> Items, long Total)> SearchRecordsAsync(Guid definitionId,
            DynamicRecordQuery query, bool enabledOnly, CancellationToken cancellationToken)
        {
            RecordReads++;
            return Task.FromResult<(IReadOnlyList<DynamicConfigRecord>, long)>((definition.Records, definition.Records.Count));
        }
        public Task CreateAsync(DynamicConfigDefinition value, DynamicConfigurationChange? source,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(IReadOnlyList<DynamicConfigurationChange> changes,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class MetadataRepositoryStub(EntitySchema schema) : IMetadataSchemaRepository
    {
        public int EffectiveReads { get; private set; }

        public Task<(IReadOnlyList<MetadataSchemaSummaryDto> Items, long Total)> SearchAsync(string tenantNId,
            MetadataSchemaQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntitySchema?> GetAsync(string tenantNId, Guid id,
            CancellationToken cancellationToken) => Task.FromResult<EntitySchema?>(schema);
        public Task<EntitySchema?> GetEffectiveAsync(string tenantNId, string nId,
            CancellationToken cancellationToken)
        {
            EffectiveReads++;
            return Task.FromResult<EntitySchema?>(schema);
        }
        public Task<EntitySchema?> GetRevisionAsync(string tenantNId, string nId, string sourceScope,
            int revision, CancellationToken cancellationToken) => Task.FromResult<EntitySchema?>(schema);
        public Task<EntitySchema?> GetPublishedInScopeAsync(EntitySchema value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EntitySchema?> GetLastPublishedAsync(EntitySchema value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextRevisionAsync(EntitySchema value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateAsync(EntitySchema value, MetadataSchemaChange? source,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(IReadOnlyList<MetadataSchemaChange> changes,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CodingRepositoryStub(CodingRuleDefinition rule) : ICodingRuleRepository
    {
        public int RevisionReads { get; private set; }
        public int GenerationCalls { get; private set; }

        public Task<(IReadOnlyList<CodingRuleSummaryDto> Items, long Total)> SearchAsync(string tenantNId,
            CodingRuleQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CodingRuleDefinition?> GetAsync(string tenantNId, Guid id,
            CancellationToken cancellationToken) => Task.FromResult<CodingRuleDefinition?>(rule);
        public Task<CodingRuleDefinition?> GetRevisionAsync(string tenantNId, string nId, string sourceScope,
            int revision, CancellationToken cancellationToken)
        {
            RevisionReads++;
            return Task.FromResult<CodingRuleDefinition?>(rule);
        }
        public Task<CodingRuleDefinition?> GetPublishedInScopeAsync(CodingRuleDefinition value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetNextRevisionAsync(CodingRuleDefinition value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateAsync(CodingRuleDefinition value, CodingRuleChange? source,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(IReadOnlyList<CodingRuleChange> changes,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CodingRuleGeneratedValue> GenerateAsync(CodingRuleGeneration generation,
            CancellationToken cancellationToken)
        {
            GenerationCalls++;
            var rendered = generation.Rule.Render(generation.GeneratedOn, generation.TenantNId,
                generation.FactoryId, 1);
            return Task.FromResult(new CodingRuleGeneratedValue(rendered.Code, 1, rendered.PeriodKey,
                generation.GeneratedOn));
        }
    }

    private sealed class UnitRepositoryStub(UnitDimension? dimension, UnitDimension? writable = null) : IUnitDimensionRepository
    {
        public int CurrentReads { get; private set; }
        public int RevisionReads { get; private set; }
        public int ListReads { get; private set; }

        public Task<(IReadOnlyList<UnitDimensionSummaryDto> Items, long Total)> SearchAsync(string tenantNId,
            UnitDimensionQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<AvailableUnitDimensionDto> Items, long Total)> ListAvailableAsync(string tenantNId,
            AvailableUnitDimensionQuery query, CancellationToken cancellationToken)
        {
            ListReads++;
            IReadOnlyList<AvailableUnitDimensionDto> items = dimension is null
                ? []
                : [new(dimension.NId, dimension.Name, dimension.ScopeType.ToString(), dimension.TenantNId,
                    dimension.Revision, dimension.PublishedOn!.Value, dimension.IsSystemDefined,
                    dimension.ConversionKind.ToString(), dimension.BaseUnitNId, dimension.Units.Count)];
            return Task.FromResult((items, (long)items.Count));
        }
        public Task<UnitDimension?> GetAsync(string tenantNId, Guid id,
            CancellationToken cancellationToken) => Task.FromResult(writable ?? dimension);
        public Task<UnitDimension?> GetCurrentAsync(string tenantNId, string nId, string sourceScope,
            CancellationToken cancellationToken)
        {
            CurrentReads++;
            return Task.FromResult(dimension);
        }
        public Task<UnitDimension?> GetRevisionAsync(string tenantNId, string nId, string sourceScope,
            int revision, CancellationToken cancellationToken)
        {
            RevisionReads++;
            return Task.FromResult(dimension);
        }
        public Task<UnitDimension?> GetPublishedInScopeAsync(UnitDimension value,
            CancellationToken cancellationToken) => Task.FromResult(dimension);
        public Task<int> GetNextRevisionAsync(UnitDimension value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateAsync(UnitDimension value, UnitDimensionChange? source,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(IReadOnlyList<UnitDimensionChange> changes,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StateMachineRepositoryStub(StateMachineDefinition definition,
        StateMachineDefinition? writable = null) : IStateMachineRepository
    {
        public int CurrentReads { get; private set; }
        public int RevisionReads { get; private set; }
        public int ListReads { get; private set; }

        public Task<(IReadOnlyList<StateMachineSummaryDto> Items, long Total)> SearchAsync(string tenantNId,
            StateMachineQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(IReadOnlyList<AvailableStateMachineDto> Items, long Total)> ListAvailableAsync(string tenantNId,
            AvailableStateMachineQuery query, CancellationToken cancellationToken)
        {
            ListReads++;
            IReadOnlyList<AvailableStateMachineDto> items =
            [
                new(definition.NId, definition.Name, definition.ScopeType.ToString(), definition.TenantNId,
                    definition.Revision, definition.PublishedOn!.Value, definition.Nodes.Count,
                    definition.Transitions.Count),
            ];
            return Task.FromResult((items, (long)items.Count));
        }
        public Task<StateMachineDefinition?> GetAsync(string tenantNId, Guid id,
            CancellationToken cancellationToken) => Task.FromResult<StateMachineDefinition?>(writable ?? definition);
        public Task<StateMachineDefinition?> GetCurrentAsync(string tenantNId, string nId, string sourceScope,
            CancellationToken cancellationToken)
        {
            CurrentReads++;
            return Task.FromResult<StateMachineDefinition?>(definition);
        }
        public Task<StateMachineDefinition?> GetRevisionAsync(string tenantNId, string nId, string sourceScope,
            int revision, CancellationToken cancellationToken)
        {
            RevisionReads++;
            return Task.FromResult<StateMachineDefinition?>(definition);
        }
        public Task<StateMachineDefinition?> GetPublishedInScopeAsync(StateMachineDefinition value,
            CancellationToken cancellationToken) => Task.FromResult<StateMachineDefinition?>(definition);
        public Task<StateMachineDefinition?> GetLastPublishedAsync(StateMachineDefinition value,
            CancellationToken cancellationToken) => Task.FromResult<StateMachineDefinition?>(definition);
        public Task<int> GetNextRevisionAsync(StateMachineDefinition value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateAsync(StateMachineDefinition value, StateMachineChange? source,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(IReadOnlyList<StateMachineChange> changes,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
