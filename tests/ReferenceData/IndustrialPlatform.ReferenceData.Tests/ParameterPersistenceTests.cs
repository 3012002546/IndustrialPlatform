using System.Text.Json;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Application.Parameter;
using IndustrialPlatform.ReferenceData.Contracts.Parameter;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.ReferenceData.Infrastructure.Parameter;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class ParameterPersistenceTests : IAsyncLifetime, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pf03-parameter-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext _context;
    private readonly ParameterService _service;
    private static readonly ReferenceDataActor Tenant = new("TENANT-A", "USER-A", false, "parameter-test-trace");
    private static readonly ReferenceDataActor Platform = Tenant with { CanManagePlatform = true };
    private static readonly JsonSerializerOptions SerializationOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
    private static CancellationToken Ct => CancellationToken.None;

    public ParameterPersistenceTests()
    {
        _context = OpenContext();
        _service = Service(_context);
    }

    public async Task InitializeAsync()
    {
        var initializer = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(_context));
        var context = new ServiceInitializationContext("Test", Tenant.TenantNId, "OP-PARAMETER", "referencedata", "referencedata",
            new ResolvedDatabaseTarget("Test", DatabaseTopologyMode.PerService, "referencedata", DatabaseProvider.Sqlite, "referencedata_db", _path, false),
            ReferenceDataServiceInitializer.CurrentVersion, ServiceInitializationPolicy.Standard, Tenant.TraceId);
        var inspection = await initializer.InspectAsync(context, Ct);
        Assert.True((await initializer.ApplyAsync(context, await initializer.PlanAsync(context, inspection, Ct), Ct)).Ready);
    }

    [Fact]
    public async Task Tenant_overrides_each_key_and_disabling_key_or_domain_restores_platform_fallback()
    {
        var platform = await CreateAsync(Platform, "Platform", "Weighting");
        platform = await AddKeyAsync(Platform, platform, "RequireCheck", "Boolean", value: "true");
        await AddKeyAsync(Platform, platform, "Tolerance", "Integer", value: "7");
        var tenant = await CreateAsync(Tenant, "Tenant", "Weighting");
        tenant = await AddKeyAsync(Tenant, tenant, "RequireCheck", "Boolean", value: "false");
        var key = Key(tenant, "RequireCheck");

        var effective = await _service.ResolveDomainAsync(Tenant, "weighting", null, Ct);
        Assert.Equal(2, effective.Keys.Count);
        var overridden = Assert.Single(effective.Keys, item => item.KeyNId == "REQUIRECHECK");
        Assert.Equal("Tenant", overridden.SourceScope);
        Assert.Equal(Tenant.TenantNId, overridden.SourceTenantNId);
        Assert.Equal("false", overridden.ValueJson);
        Assert.Equal(tenant.Revision, overridden.Revision);
        var inherited = Assert.Single(effective.Keys, item => item.KeyNId == "TOLERANCE");
        Assert.Equal("Platform", inherited.SourceScope);
        Assert.Null(inherited.SourceTenantNId);
        Assert.Equal("7", inherited.ValueJson);

        tenant = await _service.SetKeyStatusAsync(Tenant, tenant.Id, key.Id, false, ChildVersion(tenant), Ct);
        Assert.Equal("Platform", (await ResolveAsync("Weighting", "RequireCheck")).SourceScope);
        tenant = await _service.SetKeyStatusAsync(Tenant, tenant.Id, key.Id, true, ChildVersion(tenant), Ct);
        Assert.Equal("false", (await ResolveAsync("Weighting", "RequireCheck")).ValueJson);
        tenant = await _service.SetStatusAsync(Tenant, tenant.Id, false, DomainVersion(tenant), Ct);
        Assert.Equal("true", (await ResolveAsync("Weighting", "RequireCheck")).ValueJson);
        await _service.SetStatusAsync(Tenant, tenant.Id, true, DomainVersion(tenant), Ct);
        Assert.Equal("Tenant", (await ResolveAsync("Weighting", "RequireCheck")).SourceScope);
    }

    [Theory]
    [InlineData("Single")]
    [InlineData("Multi")]
    public async Task Optional_null_or_empty_multi_stops_fallback_and_survives_reload(string mode)
    {
        var platform = await CreateAsync(Platform, "Platform", "Trace");
        await _service.AddKeyAsync(Platform, platform.Id, NewKey(platform, "Sources", "String", mode) with
        {
            Value = mode == "Single" ? Json("\"platform-value\"") : null,
            InitialValues = mode == "Multi" ? [new("SourceA", null, Json("\"platform-value\""), 0, false, true)] : null,
        }, Ct);
        var tenant = await CreateAsync(Tenant, "Tenant", "Trace");
        await _service.AddKeyAsync(Tenant, tenant.Id, NewKey(tenant, "Sources", "String", mode), Ct);

        using var freshContext = OpenContext();
        var effective = await Service(freshContext).ResolveAsync(Tenant, "trace", "sources", null, Ct);
        Assert.Equal("Tenant", effective.SourceScope);
        Assert.Equal(mode, effective.ValueMode);
        Assert.True(effective.BlocksInheritance);
        Assert.False(effective.UsesDefaultValue);
        Assert.Null(effective.Value);
        Assert.Null(effective.ValueJson);
        Assert.Empty(effective.MultiValues);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(effective, SerializationOptions));
        Assert.Equal(JsonValueKind.Null, serialized.RootElement.GetProperty("value").ValueKind);
        Assert.Equal(0, serialized.RootElement.GetProperty("multiValues").GetArrayLength());
    }

    [Theory]
    [InlineData("Boolean", "false", "true")]
    [InlineData("Integer", "0", "9")]
    public async Task False_and_zero_defaults_are_values_and_explicit_values_take_precedence(string type, string fallback, string explicitValue)
    {
        var domain = await CreateAsync(Tenant, "Tenant", "Defaults");
        domain = await _service.AddKeyAsync(Tenant, domain.Id, NewKey(domain, "Setting", type) with
        {
            DefaultValue = Json(fallback), IsMandatory = true,
        }, Ct);
        var effective = await ResolveAsync("Defaults", "Setting");
        Assert.Equal(fallback, effective.ValueJson);
        Assert.True(effective.UsesDefaultValue);
        Assert.False(effective.BlocksInheritance);

        var key = Key(domain, "Setting");
        domain = await _service.UpdateKeyAsync(Tenant, domain.Id, key.Id, EditKey(domain, key) with { Value = Json(explicitValue) }, Ct);
        effective = await ResolveAsync("Defaults", "Setting");
        Assert.Equal(explicitValue, effective.ValueJson);
        Assert.False(effective.UsesDefaultValue);
        key = Key(domain, "Setting");
        await _service.UpdateKeyAsync(Tenant, domain.Id, key.Id, EditKey(domain, key) with { Value = null }, Ct);
        Assert.Equal(fallback, (await ResolveAsync("Defaults", "Setting")).ValueJson);
    }

    [Fact]
    public async Task Multi_values_are_stably_sorted_and_duplicate_or_invalid_default_writes_leave_no_history()
    {
        var domain = await CreateAsync(Tenant, "Tenant", "Limits");
        domain = await _service.AddKeyAsync(Tenant, domain.Id, NewKey(domain, "Ranges", "Decimal", "Multi") with
        {
            IsMandatory = true,
            InitialValues =
            [
                new("Later", null, Json("3"), 2, false, true),
                new("Bravo", null, Json("2"), 1, false, true),
                new("Alpha", null, Json("1.00"), 1, true, true),
                new("Hidden", null, Json("4"), 0, false, false),
            ],
        }, Ct);
        var key = Key(domain, "Ranges");
        var effective = await ResolveAsync("Limits", "Ranges");
        Assert.Collection(effective.MultiValues,
            item => Assert.Equal("ALPHA", item.NId),
            item => Assert.Equal("BRAVO", item.NId),
            item => Assert.Equal("LATER", item.NId));
        Assert.Equal("1", effective.MultiValues[0].ValueJson);
        var count = (await HistoryAsync(domain)).Total;

        var duplicate = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.AddValueAsync(Tenant, domain.Id, key.Id,
            NewValue(domain, "Duplicate", "1e0"), Ct));
        Assert.Equal("REF-CONFIG-DUPLICATE-VALUE", duplicate.ErrorCode);
        var invalidDefault = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.SetValueEnabledAsync(Tenant, domain.Id, key.Id,
            key.MultiValues.Single(item => item.NId == "ALPHA").Id, false, ChildVersion(domain), Ct));
        Assert.Equal("REF-CONFIG-DEFAULT-MUST-BE-ENABLED", invalidDefault.ErrorCode);
        AssertUnchanged(domain, await _service.GetAsync(Tenant, domain.Id, Ct));
        Assert.Equal(count, (await HistoryAsync(domain)).Total);
    }

    [Fact]
    public async Task Every_child_write_advances_root_versions_and_both_stale_tokens_are_rejected()
    {
        var domain = await CreateAsync(Tenant, "Tenant", "Versions");
        domain = await AddKeyAsync(Tenant, domain, "Value", "String", value: "\"first\"");
        var old = domain;
        var key = Key(domain, "Value");
        domain = await _service.UpdateKeyAsync(Tenant, domain.Id, key.Id, EditKey(domain, key) with { Value = Json("\"second\"") }, Ct);
        Assert.Equal(old.Revision + 1, domain.Revision);
        Assert.Equal(old.OptimisticVersion + 1, domain.OptimisticVersion);
        Assert.NotEqual(old.ConcurrencyVersion, domain.ConcurrencyVersion);
        Assert.True(domain.LastUpdatedOn >= old.LastUpdatedOn);
        var request = EditKey(domain, Key(domain, "Value")) with { Value = Json("\"must-not-save\"") };
        var oldVersion = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.UpdateKeyAsync(Tenant, domain.Id, key.Id,
            request with { ExpectedAppDomainOptimisticVersion = old.OptimisticVersion }, Ct));
        var oldToken = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.UpdateKeyAsync(Tenant, domain.Id, key.Id,
            request with { ExpectedAppDomainConcurrencyVersion = old.ConcurrencyVersion }, Ct));
        Assert.Equal("REF-CONCURRENCY-CONFLICT", oldVersion.ErrorCode);
        Assert.Equal(409, oldVersion.Status);
        Assert.Equal("REF-CONCURRENCY-CONFLICT", oldToken.ErrorCode);
        Assert.Equal(409, oldToken.Status);
        AssertUnchanged(domain, await _service.GetAsync(Tenant, domain.Id, Ct));
        Assert.Equal(3L, (await HistoryAsync(domain)).Total);
    }

    [Fact]
    public async Task Repository_compare_and_swap_rejects_a_second_loaded_writer_without_appending_history()
    {
        var domain = await CreateAsync(Tenant, "Tenant", "Writers");
        var repository = new ParameterRepository(_context);
        var winner = await repository.GetAsync(Tenant.TenantNId, domain.Id, Ct);
        var loser = await repository.GetAsync(Tenant.TenantNId, domain.Id, Ct);
        Assert.NotNull(winner);
        Assert.NotNull(loser);
        winner.Update(winner.NId, "Winner", null);
        loser.Update(loser.NId, "Loser", null);
        var history = new ConfigurationHistoryDto(Guid.NewGuid(), domain.Id, null, "AppDomain", domain.Id, domain.NId,
            "Updated", "Concurrent write probe", "before", "after", winner.Revision, Tenant.UserNId, Tenant.TraceId, DateTimeOffset.UtcNow);
        await repository.SaveAsync(winner, domain.OptimisticVersion, domain.ConcurrencyVersion, history, Ct);
        var error = await Assert.ThrowsAsync<ReferenceDataException>(() => repository.SaveAsync(loser, domain.OptimisticVersion,
            domain.ConcurrencyVersion, history with { Id = Guid.NewGuid() }, Ct));
        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
        Assert.Equal(409, error.Status);
        var stored = await _service.GetAsync(Tenant, domain.Id, Ct);
        Assert.Equal("Winner", stored.Name);
        Assert.Equal(winner.Revision, stored.Revision);
        Assert.Equal(2L, (await HistoryAsync(stored)).Total);
    }

    [Fact]
    public async Task History_is_append_only_has_paths_and_actor_and_omits_original_values()
    {
        const string first = "unlogged-value-alpha";
        const string second = "unlogged-value-bravo";
        const string fallback = "unlogged-default-charlie";
        var domain = await CreateAsync(Tenant, "Tenant", "History");
        domain = await _service.AddKeyAsync(Tenant, domain.Id, NewKey(domain, "Setting", "String") with
        {
            Value = Json(JsonSerializer.Serialize(first)), DefaultValue = Json(JsonSerializer.Serialize(fallback)),
        }, Ct);
        var key = Key(domain, "Setting");
        var earlier = await HistoryAsync(domain);
        domain = await _service.UpdateKeyAsync(Tenant, domain.Id, key.Id, EditKey(domain, key) with { Value = Json(JsonSerializer.Serialize(second)) }, Ct);
        var all = await HistoryAsync(domain);
        Assert.Equal(3L, all.Total);
        Assert.Equal(earlier.Items.OrderBy(item => item.Revision), all.Items.Where(item => item.Revision <= 2).OrderBy(item => item.Revision));
        var keys = await _service.HistoryAsync(Tenant, domain.Id, key.Id, 1, 100, Ct);
        Assert.Equal(2L, keys.Total);
        Assert.All(keys.Items, item =>
        {
            Assert.Equal("HISTORY.SETTING", item.FullNId);
            Assert.Equal("Key", item.ObjectType);
            Assert.Equal(key.Id, item.ObjectId);
            Assert.Equal(Tenant.UserNId, item.UserNId);
            Assert.Equal(Tenant.TraceId, item.TraceId);
            Assert.NotEqual(default, item.CreatedOn);
        });
        var serialized = JsonSerializer.Serialize(all.Items);
        Assert.DoesNotContain(first, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(second, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(fallback, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_insert_failure_rolls_back_root_child_and_history_in_one_transaction()
    {
        var domain = await CreateAsync(Tenant, "Tenant", "Atomicity");
        domain = await AddKeyAsync(Tenant, domain, "Setting", "String", value: "\"committed\"");
        var key = Key(domain, "Setting");
        var before = await HistoryAsync(domain);
        // This trigger belongs only to this test's unique temporary database.
        await _context.SqlSugar.Ado.ExecuteCommandAsync("""
            CREATE TRIGGER pf03_parameter_history_failure BEFORE INSERT ON reference_data_parameter_history
            BEGIN SELECT RAISE(ABORT, 'pf03-history-write-failure'); END;
            """);
        try
        {
            var error = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.UpdateKeyAsync(Tenant, domain.Id, key.Id,
                EditKey(domain, key) with { Value = Json("\"uncommitted\"") }, Ct));
            Assert.Equal(503, error.Status);
        }
        finally
        {
            await _context.SqlSugar.Ado.ExecuteCommandAsync("DROP TRIGGER pf03_parameter_history_failure");
        }
        using var freshContext = OpenContext();
        var fresh = Service(freshContext);
        AssertUnchanged(domain, await fresh.GetAsync(Tenant, domain.Id, Ct));
        var after = await fresh.HistoryAsync(Tenant, domain.Id, null, 1, 100, Ct);
        Assert.Equal(before.Total, after.Total);
        Assert.Equal(before.Items, after.Items);
    }

    [Theory]
    [InlineData("Single")]
    [InlineData("Multi")]
    public async Task Readonly_key_cannot_be_unlocked_disabled_or_have_its_values_changed(string mode)
    {
        var domain = await CreateAsync(Tenant, "Tenant", "Readonly");
        domain = await _service.AddKeyAsync(Tenant, domain.Id, NewKey(domain, "Setting", "String", mode) with
        {
            IsReadOnly = true,
            Value = mode == "Single" ? Json("\"fixed\"") : null,
            InitialValues = mode == "Multi" ? [new("Fixed", null, Json("\"fixed\""), 0, false, true)] : null,
        }, Ct);
        var key = Key(domain, "Setting");
        await AssertReadonlyAsync(() => _service.UpdateKeyAsync(Platform, domain.Id, key.Id, EditKey(domain, key) with { IsReadOnly = false }, Ct));
        await AssertReadonlyAsync(() => _service.SetKeyStatusAsync(Platform, domain.Id, key.Id, false, ChildVersion(domain), Ct));
        if (mode == "Multi")
        {
            var value = Assert.Single(key.MultiValues);
            await AssertReadonlyAsync(() => _service.AddValueAsync(Platform, domain.Id, key.Id, NewValue(domain, "NewValue", "\"new\""), Ct));
            await AssertReadonlyAsync(() => _service.UpdateValueAsync(Platform, domain.Id, key.Id, value.Id, NewValue(domain, value.NId, "\"changed\""), Ct));
            await AssertReadonlyAsync(() => _service.SetValueEnabledAsync(Platform, domain.Id, key.Id, value.Id, false, ChildVersion(domain), Ct));
        }
        AssertUnchanged(domain, await _service.GetAsync(Tenant, domain.Id, Ct));
        Assert.Equal(2L, (await HistoryAsync(domain)).Total);
    }

    [Fact]
    public async Task Clearing_values_retains_persisted_type_lock_across_fresh_contexts()
    {
        var domain = await CreateAsync(Tenant, "Tenant", "TypeHistory");
        domain = await AddKeyAsync(Tenant, domain, "Setting", "String", value: "\"once-configured\"");
        var key = Key(domain, "Setting");
        domain = await _service.UpdateKeyAsync(Tenant, domain.Id, key.Id, EditKey(domain, key) with { Value = null, DefaultValue = null }, Ct);
        using var freshContext = OpenContext();
        var fresh = Service(freshContext);
        domain = await fresh.GetAsync(Tenant, domain.Id, Ct);
        key = Key(domain, "Setting");
        Assert.True(key.HasHadValue);
        Assert.Null(key.Value);
        Assert.Null(key.DefaultValue);
        var type = await Assert.ThrowsAsync<ReferenceDataException>(() => fresh.UpdateKeyAsync(Tenant, domain.Id, key.Id,
            EditKey(domain, key) with { DataType = "Boolean" }, Ct));
        var mode = await Assert.ThrowsAsync<ReferenceDataException>(() => fresh.UpdateKeyAsync(Tenant, domain.Id, key.Id,
            EditKey(domain, key) with { ValueMode = "Multi" }, Ct));
        Assert.Equal("REF-CONFIG-TYPE-CHANGE-NOT-ALLOWED", type.ErrorCode);
        Assert.Equal("REF-CONFIG-TYPE-CHANGE-NOT-ALLOWED", mode.ErrorCode);
        Assert.Equal(409, type.Status);
        Assert.Equal(409, mode.Status);
        AssertUnchanged(domain, await fresh.GetAsync(Tenant, domain.Id, Ct));
        Assert.Equal(3L, (await fresh.HistoryAsync(Tenant, domain.Id, null, 1, 100, Ct)).Total);
    }

    [Fact]
    public async Task Tenant_boundaries_platform_writes_and_factory_gate_are_enforced_without_side_effects()
    {
        var domain = await CreateAsync(Tenant, "Tenant", "PrivateDomain");
        var other = Tenant with { TenantNId = "TENANT-B", UserNId = "USER-B" };
        Assert.Equal(404, (await Assert.ThrowsAsync<ReferenceDataException>(() => _service.GetAsync(other, domain.Id, Ct))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReferenceDataException>(() => _service.HistoryAsync(other, domain.Id, null, 1, 100, Ct))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReferenceDataException>(() => _service.UpdateAsync(other, domain.Id, EditDomain(domain, "Forbidden"), Ct))).Status);
        Assert.Empty((await _service.SearchAsync(other, new(), Ct)).Items);
        Assert.Equal(403, (await Assert.ThrowsAsync<ReferenceDataException>(() => CreateAsync(Tenant, "Platform", "Denied"))).Status);
        var platform = await CreateAsync(Platform, "Platform", "SharedDomain");
        Assert.Equal(platform.Id, (await _service.GetAsync(Tenant, platform.Id, Ct)).Id);
        Assert.Equal(403, (await Assert.ThrowsAsync<ReferenceDataException>(() => _service.UpdateAsync(Tenant, platform.Id, EditDomain(platform, "Forbidden"), Ct))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ReferenceDataException>(() => _service.AddKeyAsync(Tenant, platform.Id, NewKey(platform, "Forbidden", "String"), Ct))).Status);
        var factoryCreate = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.CreateAsync(Tenant,
            new("Factory", "FACTORY-A", "FactoryDomain", "Factory", null, "Factory probe"), Ct));
        var factoryUpdate = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.UpdateAsync(Tenant, domain.Id,
            EditDomain(domain, domain.Name) with { ScopeType = "Factory", ScopeId = "FACTORY-A" }, Ct));
        var factoryResolve = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.ResolveAsync(Tenant, "PrivateDomain", "Setting", "FACTORY-A", Ct));
        var factoryDomain = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.ResolveDomainAsync(Tenant, "PrivateDomain", "FACTORY-A", Ct));
        Assert.All(new[] { factoryCreate, factoryUpdate, factoryResolve, factoryDomain }, error =>
        {
            Assert.Equal("REF-SCOPE-FACTORY-NOT-READY", error.ErrorCode);
            Assert.Equal(409, error.Status);
        });
        AssertUnchanged(domain, await _service.GetAsync(Tenant, domain.Id, Ct));
        AssertUnchanged(platform, await _service.GetAsync(Platform, platform.Id, Ct));
        Assert.Equal(1L, (await HistoryAsync(domain)).Total);
        Assert.Equal(1L, (await HistoryAsync(platform)).Total);
    }

    [Fact]
    public async Task Platform_enum_uses_only_platform_dictionary_while_tenant_enum_uses_its_override()
    {
        var dictionaries = new DictionaryService(new DictionaryRepository(_context));
        var tenantDictionary = await dictionaries.CreateAsync(Tenant,
            new("Tenant", null, "Choices", "Tenant choices", null, [new("PRIVATE", "Private", null, 0, true)]), Ct);
        await dictionaries.PublishAsync(Tenant, tenantDictionary.Id,
            new(tenantDictionary.OptimisticVersion, tenantDictionary.ConcurrencyVersion, "Publish tenant choices"), Ct);

        var platform = await CreateAsync(Platform, "Platform", "EnumSettings");
        var request = NewKey(platform, "Choice", "Enum") with { DictionaryNId = "Choices", Value = Json("\"PRIVATE\"") };
        var missing = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.AddKeyAsync(Platform, platform.Id, request, Ct));
        Assert.Equal("REF-DICT-NOT-FOUND", missing.ErrorCode);
        Assert.Equal(404, missing.Status);
        AssertUnchanged(platform, await _service.GetAsync(Platform, platform.Id, Ct));
        Assert.Equal(1L, (await HistoryAsync(platform)).Total);

        var platformDictionary = await dictionaries.CreateAsync(Platform,
            new("Platform", null, "Choices", "Platform choices", null, [new("PUBLIC", "Public", null, 0, true)]), Ct);
        await dictionaries.PublishAsync(Platform, platformDictionary.Id,
            new(platformDictionary.OptimisticVersion, platformDictionary.ConcurrencyVersion, "Publish platform choices"), Ct);
        var privateValue = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.AddKeyAsync(Platform, platform.Id, request, Ct));
        Assert.Equal("REF-CONFIG-ENUM-VALUE-INVALID", privateValue.ErrorCode);
        AssertUnchanged(platform, await _service.GetAsync(Platform, platform.Id, Ct));
        Assert.Equal(1L, (await HistoryAsync(platform)).Total);
        await _service.AddKeyAsync(Platform, platform.Id, request with { Value = Json("\"PUBLIC\"") }, Ct);

        var tenant = await CreateAsync(Tenant, "Tenant", "EnumSettings");
        await _service.AddKeyAsync(Tenant, tenant.Id, NewKey(tenant, "Choice", "Enum") with
        {
            DictionaryNId = "Choices", Value = Json("\"PRIVATE\""),
        }, Ct);
        var tenantValue = await ResolveAsync("EnumSettings", "Choice");
        Assert.Equal("Tenant", tenantValue.SourceScope);
        Assert.Equal("PRIVATE", tenantValue.Value!.Value.GetString());
        var otherTenant = await _service.ResolveAsync(Tenant with { TenantNId = "TENANT-B" }, "EnumSettings", "Choice", null, Ct);
        Assert.Equal("Platform", otherTenant.SourceScope);
        Assert.Null(otherTenant.SourceTenantNId);
        Assert.Equal("PUBLIC", otherTenant.Value!.Value.GetString());
    }

    private SqlSugarDbContext OpenContext() => new(Options.Create(new SqlSugarOptions
    {
        DbType = DbType.Sqlite, ConnectionString = $"Data Source={_path};Pooling=False",
    }));
    private static ParameterService Service(SqlSugarDbContext context) => new(new ParameterRepository(context),
        new DictionaryService(new DictionaryRepository(context)), NullLogger<ParameterService>.Instance);
    private Task<ConfigurationDomainDetailDto> CreateAsync(ReferenceDataActor actor, string scope, string nId) =>
        _service.CreateAsync(actor, new(scope, null, nId, nId, null, "Create test domain"), Ct);
    private Task<ConfigurationDomainDetailDto> AddKeyAsync(ReferenceDataActor actor, ConfigurationDomainDetailDto domain, string nId,
        string type, string mode = "Single", string? value = null) =>
        _service.AddKeyAsync(actor, domain.Id, NewKey(domain, nId, type, mode) with { Value = value is null ? null : Json(value) }, Ct);
    private Task<EffectiveConfigurationDto> ResolveAsync(string domain, string key) => _service.ResolveAsync(Tenant, domain, key, null, Ct);
    private Task<(IReadOnlyList<ConfigurationHistoryDto> Items, long Total)> HistoryAsync(ConfigurationDomainDetailDto domain) =>
        _service.HistoryAsync(Tenant, domain.Id, null, 1, 100, Ct);
    private static ConfigurationKeyDto Key(ConfigurationDomainDetailDto domain, string nId) =>
        domain.Keys.Single(key => string.Equals(key.NId, nId, StringComparison.OrdinalIgnoreCase));
    private static ConfigurationKeyRequest NewKey(ConfigurationDomainDetailDto domain, string nId, string type, string mode = "Single") =>
        new(nId, nId, null, type, mode, null, null, false, false, null, null, "Active", 0, "Change test key", domain.OptimisticVersion, domain.ConcurrencyVersion);
    private static ConfigurationKeyRequest EditKey(ConfigurationDomainDetailDto domain, ConfigurationKeyDto key) =>
        new(key.NId, key.Name, key.Description, key.DataType, key.ValueMode, key.Value, key.DefaultValue, key.IsMandatory, key.IsReadOnly,
            key.DictionaryNId, key.ReferenceTarget, key.Status, key.Sort, "Update test key", domain.OptimisticVersion, domain.ConcurrencyVersion);
    private static ConfigurationValueRequest NewValue(ConfigurationDomainDetailDto domain, string nId, string value) =>
        new(nId, null, Json(value), 0, false, true, "Change test value", domain.OptimisticVersion, domain.ConcurrencyVersion);
    private static UpdateConfigurationDomainRequest EditDomain(ConfigurationDomainDetailDto domain, string name) =>
        new(domain.NId, name, domain.Description, "Update test domain", domain.OptimisticVersion, domain.ConcurrencyVersion, domain.ScopeType, null);
    private static ConfigurationChildStateRequest ChildVersion(ConfigurationDomainDetailDto domain) => new("Change test state", domain.OptimisticVersion, domain.ConcurrencyVersion);
    private static ConfigurationDomainStateRequest DomainVersion(ConfigurationDomainDetailDto domain) => new("Change test state", domain.OptimisticVersion, domain.ConcurrencyVersion);
    private static JsonElement Json(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }
    private static void AssertUnchanged(ConfigurationDomainDetailDto before, ConfigurationDomainDetailDto after) =>
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
    private static async Task AssertReadonlyAsync(Func<Task<ConfigurationDomainDetailDto>> action)
    {
        var error = await Assert.ThrowsAsync<ReferenceDataException>(action);
        Assert.Equal("REF-CONFIG-READ-ONLY", error.ErrorCode);
        Assert.Equal(409, error.Status);
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }
}
