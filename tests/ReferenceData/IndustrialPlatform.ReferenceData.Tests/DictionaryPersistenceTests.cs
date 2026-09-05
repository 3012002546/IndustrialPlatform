using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.Dictionary;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Dictionary;
using IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class DictionaryPersistenceTests : IAsyncLifetime, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pf03-dictionary-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext _context;
    private readonly DictionaryService _service;
    private static readonly ReferenceDataActor Tenant = new("TENANT-A", "USER-A", false, "test");
    private static readonly ReferenceDataActor Platform = Tenant with { CanManagePlatform = true };

    public DictionaryPersistenceTests()
    {
        _context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions { DbType = DbType.Sqlite, ConnectionString = $"Data Source={_path};Pooling=False" }));
        _service = new DictionaryService(new DictionaryRepository(_context));
    }

    public async Task InitializeAsync()
    {
        var initializer = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(_context));
        var context = new ServiceInitializationContext("Test", Tenant.TenantNId, "OP", "referencedata", "referencedata",
            new ResolvedDatabaseTarget("Test", DatabaseTopologyMode.PerService, "referencedata", DatabaseProvider.Sqlite, "referencedata_db", _path, false),
            ReferenceDataServiceInitializer.CurrentVersion, ServiceInitializationPolicy.Standard, "test");
        var inspection = await initializer.InspectAsync(context, CancellationToken.None);
        Assert.True((await initializer.ApplyAsync(context, await initializer.PlanAsync(context, inspection, CancellationToken.None), CancellationToken.None)).Ready);
    }

    [Fact]
    public async Task Sqlite_round_trips_dictionary_and_item_timestamps_as_the_same_utc_instants()
    {
        var created = await CreateAsync(Tenant, "Tenant", "TIMESTAMPS", "Timestamps", "READY");
        var draft = await _service.GetAsync(Tenant, created.Id, CancellationToken.None);
        Assert.Equal(created.LastUpdatedOn, draft.LastUpdatedOn);
        Assert.Equal(TimeSpan.Zero, draft.LastUpdatedOn.Offset);
        Assert.Null(draft.PublishedOn);

        var published = await _service.PublishAsync(Tenant, created.Id, Version(created), CancellationToken.None);
        Assert.NotNull(published.PublishedOn);
        var reloaded = await _service.GetAsync(Tenant, published.Id, CancellationToken.None);
        Assert.Equal(published.LastUpdatedOn, reloaded.LastUpdatedOn);
        Assert.Equal(published.PublishedOn, reloaded.PublishedOn);
        Assert.Equal(TimeSpan.Zero, reloaded.LastUpdatedOn.Offset);
        Assert.Equal(TimeSpan.Zero, reloaded.PublishedOn!.Value.Offset);
        var summaries = await _service.SearchAsync(Tenant, new(Keyword: "TIMESTAMPS"), CancellationToken.None);
        Assert.Equal(published.LastUpdatedOn, Assert.Single(summaries.Items).LastUpdatedOn);
        var effective = await _service.GetEffectiveAsync(Tenant, "TIMESTAMPS", null, CancellationToken.None);
        Assert.Equal(published.PublishedOn!.Value, effective.PublishedOn);

        // Keep the original entities so item timestamps are compared with their values before persistence.
        var repository = new DictionaryRepository(_context);
        var definition = new DictionaryDefinition("ITEMTIMES", "Item timestamps", null, ReferenceScopeType.Tenant, Tenant.TenantNId, null);
        var item = new DictionaryItem("READY", "Ready", null, 0, true);
        definition.Update(definition.Name, null, [item]);
        await repository.CreateAsync(definition, null, CancellationToken.None);
        var stored = await repository.GetAsync(Tenant.TenantNId, definition.Id, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(definition.CreatedOn, stored.CreatedOn);
        Assert.Equal(definition.LastUpdatedOn, stored.LastUpdatedOn);
        Assert.Equal(TimeSpan.Zero, stored.CreatedOn.Offset);
        Assert.Equal(TimeSpan.Zero, stored.LastUpdatedOn.Offset);
        var storedItem = Assert.Single(stored.Items);
        Assert.Equal(item.Id, storedItem.Id);
        Assert.Equal(item.CreatedOn, storedItem.CreatedOn);
        Assert.Equal(item.LastUpdatedOn, storedItem.LastUpdatedOn);
        Assert.Equal(TimeSpan.Zero, storedItem.CreatedOn.Offset);
        Assert.Equal(TimeSpan.Zero, storedItem.LastUpdatedOn.Offset);
    }

    [Fact]
    public async Task Tenant_dictionary_replaces_whole_platform_dictionary_and_disable_restores_fallback()
    {
        var platform = await CreateAsync(Platform, "Platform", "STATUS", "Platform", "A", "B");
        platform = await _service.PublishAsync(Platform, platform.Id, Version(platform), CancellationToken.None);
        var tenant = await CreateAsync(Tenant, "Tenant", "STATUS", "Tenant", "C");
        tenant = await _service.PublishAsync(Tenant, tenant.Id, Version(tenant), CancellationToken.None);
        var effective = await _service.GetEffectiveAsync(Tenant, "status", null, CancellationToken.None);
        Assert.Equal("Tenant", effective.SourceScope);
        Assert.Equal("C", Assert.Single(effective.Items).NId);
        await _service.DisableAsync(Tenant, tenant.Id, Version(tenant) with { ChangeReason = "Retire tenant override" }, CancellationToken.None);
        effective = await _service.GetEffectiveAsync(Tenant, "STATUS", null, CancellationToken.None);
        Assert.Equal("Platform", effective.SourceScope);
        Assert.Collection(effective.Items, item => Assert.Equal("A", item.NId), item => Assert.Equal("B", item.NId));
        Assert.Null(effective.SourceTenantNId);
    }

    [Fact]
    public async Task Publish_clone_atomically_supersedes_previous_revision_and_retains_historical_content()
    {
        var first = await CreateAsync(Tenant, "Tenant", "COLOR", "Color", "RED");
        first = await _service.PublishAsync(Tenant, first.Id, Version(first), CancellationToken.None);
        var clone = await _service.CloneAsync(Tenant, first.Id, Version(first), CancellationToken.None);
        Assert.Equal(2, clone.Revision);
        clone = await _service.UpdateAsync(Tenant, clone.Id, new("Colours", null,
            [new("RED", "Red", null, 0, false), new("BLUE", "Blue", null, 1, true)], clone.OptimisticVersion, clone.ConcurrencyVersion), CancellationToken.None);
        clone = await _service.PublishAsync(Tenant, clone.Id, Version(clone), CancellationToken.None);
        var historical = await _service.GetAsync(Tenant, first.Id, CancellationToken.None);
        Assert.Equal("Superseded", historical.Status);
        Assert.True(Assert.Single(historical.Items).Enabled);
        Assert.Equal("BLUE", Assert.Single((await _service.GetEffectiveAsync(Tenant, "COLOR", null, CancellationToken.None)).Items).NId);
        var summaries = await _service.SearchAsync(Tenant, new(Keyword: "colour"), CancellationToken.None);
        Assert.Equal(clone.Id, Assert.Single(summaries.Items).Id);
    }

    [Fact]
    public async Task Only_one_draft_per_scope_is_allowed()
    {
        var published = await CreateAsync(Tenant, "Tenant", "ONE_DRAFT", "One draft", "READY");
        published = await _service.PublishAsync(Tenant, published.Id, Version(published), CancellationToken.None);
        var draft = await _service.CloneAsync(Tenant, published.Id, Version(published), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            _service.CloneAsync(Tenant, published.Id, Version(published), CancellationToken.None));

        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
        Assert.Equal("Draft", (await _service.GetAsync(Tenant, draft.Id, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Publishing_a_legacy_lower_draft_cannot_roll_back_the_current_revision()
    {
        // Simulate duplicate drafts left by the pre-010 schema. The service guard must remain defensive.
        await _context.SqlSugar.Ado.ExecuteCommandAsync("DROP INDEX dictionary_draft_uq");
        var first = await CreateAsync(Tenant, "Tenant", "NO_ROLLBACK", "No rollback", "READY");
        first = await _service.PublishAsync(Tenant, first.Id, Version(first), CancellationToken.None);
        var second = await _service.CloneAsync(Tenant, first.Id, Version(first), CancellationToken.None);
        var third = await _service.CloneAsync(Tenant, first.Id, Version(first), CancellationToken.None);
        third = await _service.PublishAsync(Tenant, third.Id, Version(third), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            _service.PublishAsync(Tenant, second.Id, Version(second), CancellationToken.None));

        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
        Assert.Equal("revision", error.Field);
        Assert.Equal(third.Revision,
            (await _service.GetEffectiveAsync(Tenant, "NO_ROLLBACK", null, CancellationToken.None)).Revision);
    }

    [Fact]
    public async Task Clone_transaction_rejects_a_source_changed_after_it_was_loaded()
    {
        var published = await CreateAsync(Tenant, "Tenant", "CLONE_CAS", "Clone CAS", "READY");
        published = await _service.PublishAsync(Tenant, published.Id, Version(published), CancellationToken.None);
        var repository = new DictionaryRepository(_context);
        var loaded = await repository.GetAsync(Tenant.TenantNId, published.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        var expectedVersion = loaded.OptimisticVersion;
        var expectedToken = loaded.ConcurrencyVersion;
        var clone = loaded.Clone(await repository.GetNextRevisionAsync(loaded, CancellationToken.None));
        await _service.DisableAsync(Tenant, published.Id,
            Version(published) with { ChangeReason = "Changed after clone read" }, CancellationToken.None);

        var error = await Assert.ThrowsAsync<ReferenceDataException>(() => repository.CreateAsync(clone,
            new(loaded, expectedVersion, expectedToken), CancellationToken.None));

        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
    }

    [Fact]
    public async Task Stale_update_duplicate_identity_and_cross_tenant_access_are_rejected()
    {
        var draft = await CreateAsync(Tenant, "Tenant", "STATUS", "Status", "A");
        await _service.UpdateAsync(Tenant, draft.Id, new("New", null, draft.Items, draft.OptimisticVersion, draft.ConcurrencyVersion), CancellationToken.None);
        var stale = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.UpdateAsync(Tenant, draft.Id,
            new("Stale", null, draft.Items, draft.OptimisticVersion, draft.ConcurrencyVersion), CancellationToken.None));
        Assert.Equal("REF-CONCURRENCY-CONFLICT", stale.ErrorCode);
        var duplicate = await Assert.ThrowsAsync<ReferenceDataException>(() => CreateAsync(Tenant, "Tenant", "status", "Duplicate", "B"));
        Assert.Equal("REF-DICT-DUPLICATE-NID", duplicate.ErrorCode);
        var hidden = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.GetAsync(Tenant with { TenantNId = "TENANT-B" }, draft.Id, CancellationToken.None));
        Assert.Equal(404, hidden.Status);
        Assert.Equal("New", (await _service.GetAsync(Tenant, draft.Id, CancellationToken.None)).Name);
    }

    [Fact]
    public async Task Consumed_item_cannot_be_removed_when_publishing_a_new_revision()
    {
        var first = await CreateAsync(Tenant, "Tenant", "STATUS", "Status", "OLD");
        first = await _service.PublishAsync(Tenant, first.Id, Version(first), CancellationToken.None);
        var clone = await _service.CloneAsync(Tenant, first.Id, Version(first), CancellationToken.None);
        clone = await _service.UpdateAsync(Tenant, clone.Id, new("Status", null, [new("NEW", "New", null, 0, true)], clone.OptimisticVersion, clone.ConcurrencyVersion), CancellationToken.None);
        var error = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.PublishAsync(Tenant, clone.Id, Version(clone), CancellationToken.None));
        Assert.Equal("items", error.Field);
        Assert.Equal("Published", (await _service.GetAsync(Tenant, first.Id, CancellationToken.None)).Status);
        Assert.Equal("Draft", (await _service.GetAsync(Tenant, clone.Id, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Disabling_published_history_does_not_allow_removing_consumed_items()
    {
        var first = await CreateAsync(Tenant, "Tenant", "STATUS", "Status", "OLD");
        first = await _service.PublishAsync(Tenant, first.Id, Version(first), CancellationToken.None);
        first = await _service.DisableAsync(Tenant, first.Id, Version(first) with { ChangeReason = "Replace later" }, CancellationToken.None);
        var clone = await _service.CloneAsync(Tenant, first.Id, Version(first), CancellationToken.None);
        clone = await _service.UpdateAsync(Tenant, clone.Id, new("Status", null, [new("NEW", "New", null, 0, true)], clone.OptimisticVersion, clone.ConcurrencyVersion), CancellationToken.None);
        var check = await _service.CheckPublicationAsync(Tenant, clone.Id, CancellationToken.None);
        Assert.Equal(1, check.PreviousRevision);
        Assert.Equal("REF-DICT-HISTORICAL-ITEM-REMOVED", Assert.Single(check.Errors).Code);
        await Assert.ThrowsAsync<ReferenceDataException>(() => _service.PublishAsync(Tenant, clone.Id, Version(clone), CancellationToken.None));
        Assert.Equal("Disabled", (await _service.GetAsync(Tenant, first.Id, CancellationToken.None)).Status);
    }

    private Task<DictionaryDetailDto> CreateAsync(ReferenceDataActor actor, string scope, string nId, string name, params string[] items) =>
        _service.CreateAsync(actor, new(scope, null, nId, name, null, items.Select((item, index) => new DictionaryItemDto(item, item, null, index, true)).ToArray()), CancellationToken.None);

    private static PublishOrDisableRequest Version(DictionaryDetailDto dto) => new(dto.OptimisticVersion, dto.ConcurrencyVersion, null);

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
