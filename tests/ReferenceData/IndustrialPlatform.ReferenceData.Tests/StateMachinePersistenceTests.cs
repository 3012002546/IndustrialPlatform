using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.StateMachine;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.StateMachine;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Infrastructure.StateMachine;
using IndustrialPlatform.ReferenceData.Infrastructure.Persistence;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class StateMachinePersistenceTests : IAsyncLifetime, IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"pf03-state-machine-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext context;
    private readonly StateMachineRepository repository;
    private readonly StateMachineService service;
    private static readonly ReferenceDataActor Tenant = new("TENANT-A", "USER-A", false, "state-machine-test");
    private static readonly ReferenceDataActor Platform = Tenant with { CanManagePlatform = true };
    private static CancellationToken Ct => CancellationToken.None;

    public StateMachinePersistenceTests()
    {
        context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={path};Pooling=False",
        }));
        repository = new StateMachineRepository(context);
        service = new StateMachineService(repository);
    }

    public async Task InitializeAsync() =>
        await context.SqlSugar.Ado.ExecuteCommandAsync("PRAGMA foreign_keys=ON;"
            + StateMachineMigration.Sql(false) + ReferenceDataSharedMigration.Sql(false)
            + ReferenceDataCacheGenerationMigration.Sql(false));

    [Fact]
    public async Task Available_lists_platform_and_tenant_sources_without_mixing_and_fixed_revision_never_falls_back()
    {
        var platform = await CreateAsync(Platform, "Platform", "OrderFlow");
        platform = await service.PublishAsync(Platform, platform.Id, Version(platform), Ct);
        var tenant = await CreateAsync(Tenant, "Tenant", "OrderFlow");
        tenant = await service.PublishAsync(Tenant, tenant.Id, Version(tenant), Ct);

        var available = await service.ListAvailableAsync(Tenant, new(1, 20, "OrderFlow"), Ct);
        Assert.Equal(2, available.Total);
        Assert.Contains(available.Items, item => item.SourceScope == "Platform" && item.SourceTenantNId is null);
        Assert.Contains(available.Items, item => item.SourceScope == "Tenant" && item.SourceTenantNId == "TENANT-A");
        Assert.Equal(platform.Id, (await service.GetAsync(Tenant, platform.Id, Ct)).Id);

        var otherTenant = Tenant with { TenantNId = "TENANT-B" };
        var error = await Assert.ThrowsAsync<ReferenceDataException>(() => service.GetRevisionAsync(
            otherTenant, "OrderFlow", 1, "Tenant", "TENANT-A", Ct));
        Assert.Equal("REF-STATE-MACHINE-NOT-FOUND", error.ErrorCode);
        await Assert.ThrowsAsync<ReferenceDataException>(() =>
            service.GetCurrentAsync(Tenant, "OrderFlow", null, null, Ct));
    }

    [Fact]
    public async Task Publish_replaces_current_atomically_and_old_fixed_snapshot_stays_readable()
    {
        var first = await CreateAsync(Tenant, "Tenant", "Revisioned");
        first = await service.PublishAsync(Tenant, first.Id, Version(first), Ct);
        var clone = await service.CloneAsync(Tenant, first.Id, Version(first), Ct);
        clone = await service.UpdateAsync(Tenant, clone.Id, new(
            "Revisioned v2", null,
            [Node("draft", initial: true), Node("done", terminal: true, outcome: "Success")],
            [Transition("draft", "finish", "Finish", "done")],
            clone.OptimisticVersion, clone.ConcurrencyVersion), Ct);
        clone = await service.PublishAsync(Tenant, clone.Id, Version(clone), Ct);

        var current = await service.GetCurrentAsync(Tenant, "Revisioned", "Tenant", "TENANT-A", Ct);
        var historical = await service.GetRevisionAsync(Tenant, "Revisioned", 1, "Tenant", "TENANT-A", Ct);
        Assert.Equal(2, current.Revision);
        Assert.Equal("Revisioned v2", current.Name);
        Assert.Equal(1, historical.Revision);
        Assert.Equal("Superseded", historical.Status);
        Assert.NotEqual(first.Id, clone.Id);
        Assert.All(clone.Nodes, node => Assert.DoesNotContain(first.Nodes, old => old.Id == node.Id));
        Assert.All(clone.Transitions, transition => Assert.DoesNotContain(first.Transitions, old => old.Id == transition.Id));

        var stale = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            service.CloneAsync(Tenant, first.Id, Version(first), Ct));
        Assert.Equal("REF-CONCURRENCY-CONFLICT", stale.ErrorCode);
    }

    [Fact]
    public async Task Failed_invalid_publication_does_not_supersede_the_existing_current_definition()
    {
        var first = await CreateAsync(Tenant, "Tenant", "AtomicFlow");
        first = await service.PublishAsync(Tenant, first.Id, Version(first), Ct);
        var clone = await service.CloneAsync(Tenant, first.Id, Version(first), Ct);
        clone = await service.UpdateAsync(Tenant, clone.Id, new(
            clone.Name, clone.Description,
            [Node("draft", initial: true), Node("done", initial: true)],
            [Transition("draft", "finish", "Finish", "done")],
            clone.OptimisticVersion, clone.ConcurrencyVersion), Ct);

        var error = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            service.PublishAsync(Tenant, clone.Id, Version(clone), Ct));

        Assert.Equal("REF-STATE-MACHINE-INVALID", error.ErrorCode);
        Assert.Equal(1, (await service.GetCurrentAsync(
            Tenant, "AtomicFlow", "Tenant", "TENANT-A", Ct)).Revision);
        Assert.Equal("Draft", (await service.GetAsync(Tenant, clone.Id, Ct)).Status);
    }

    [Fact]
    public async Task Publication_check_compares_with_the_last_published_revision_after_it_is_disabled()
    {
        var first = await CreateAsync(Tenant, "Tenant", "DisabledHistory");
        first = await service.PublishAsync(Tenant, first.Id, Version(first), Ct);
        var disabled = await service.DisableAsync(Tenant, first.Id,
            new(first.OptimisticVersion, first.ConcurrencyVersion, "Retire revision one"), Ct);
        var clone = await service.CloneAsync(Tenant, disabled.Id, Version(disabled), Ct);

        var check = await service.CheckPublicationAsync(Tenant, clone.Id, Ct);

        Assert.Equal(1, check.PreviousRevision);
        Assert.Empty(check.AddedNodeNIds);
        Assert.Empty(check.RemovedNodeNIds);
        Assert.Empty(check.ChangedNodeNIds);
        Assert.Empty(check.AddedTransitionKeys);
        Assert.Empty(check.RemovedTransitionKeys);
        Assert.Empty(check.ChangedTransitionKeys);
    }

    [Fact]
    public async Task Child_tables_enforce_parent_revision_and_transition_node_foreign_keys()
    {
        var draft = await CreateAsync(Tenant, "Tenant", "ForeignKeys");
        var orphan = await Assert.ThrowsAnyAsync<Exception>(() => context.SqlSugar.Ado.ExecuteCommandAsync($"""
            INSERT INTO reference_data_state_machine_node
                (id,state_machine_definition_id,definition_revision,n_id,name,description,is_initial,is_terminal,outcome,color,sort)
            VALUES ('{Guid.NewGuid()}','{Guid.NewGuid()}',1,'BAD','Bad',NULL,1,0,'None',NULL,0)
            """));
        Assert.Contains("FOREIGN KEY", orphan.ToString(), StringComparison.OrdinalIgnoreCase);

        var crossRevision = await Assert.ThrowsAnyAsync<Exception>(() => context.SqlSugar.Ado.ExecuteCommandAsync($"""
            INSERT INTO reference_data_state_machine_transition
                (id,state_machine_definition_id,definition_revision,from_status_nid,action_nid,action_name,to_status_nid,description)
            VALUES ('{Guid.NewGuid()}','{draft.Id}',2,'DRAFT','BAD','Bad','DONE',NULL)
            """));
        Assert.Contains("FOREIGN KEY", crossRevision.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Utc_round_trips_version_exhaustion_is_safe_and_evaluation_does_not_write()
    {
        var draft = await CreateAsync(Tenant, "Tenant", "ReadOnlyEvaluation");
        draft = await service.PublishAsync(Tenant, draft.Id, Version(draft), Ct);
        var before = await service.GetAsync(Tenant, draft.Id, Ct);
        var evaluation = await service.EvaluateAsync(Tenant, "ReadOnlyEvaluation", new(
            "Tenant", "TENANT-A", 1, "done", "anything"), Ct);
        var after = await service.GetAsync(Tenant, draft.Id, Ct);
        Assert.False(evaluation.AllowedByDefinition);
        Assert.Equal(before.OptimisticVersion, after.OptimisticVersion);
        Assert.Equal(before.ConcurrencyVersion, after.ConcurrencyVersion);
        Assert.Equal(TimeSpan.Zero, after.LastUpdatedOn.Offset);
        Assert.Equal(TimeSpan.Zero, after.PublishedOn!.Value.Offset);

        await context.SqlSugar.Ado.ExecuteCommandAsync(
            $"UPDATE reference_data_state_machine_definition SET optimistic_version={long.MaxValue} WHERE id='{draft.Id}'");
        var exhausted = await service.GetAsync(Tenant, draft.Id, Ct);
        var error = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            service.DisableAsync(Tenant, draft.Id, Version(exhausted), Ct));
        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
        Assert.Equal("Published", (await service.GetAsync(Tenant, draft.Id, Ct)).Status);
    }

    [Fact]
    public async Task Concurrent_publish_with_the_same_double_version_has_one_winner_and_one_conflict()
    {
        var draft = await CreateAsync(Tenant, "Tenant", "PublishRace");
        using var firstContext = CreateContext(path);
        using var secondContext = CreateContext(path);
        await firstContext.SqlSugar.Ado.ExecuteCommandAsync("PRAGMA busy_timeout=10000");
        await secondContext.SqlSugar.Ado.ExecuteCommandAsync("PRAGMA busy_timeout=10000");
        var firstService = new StateMachineService(new StateMachineRepository(firstContext));
        var secondService = new StateMachineService(new StateMachineRepository(secondContext));

        async Task<string> PublishAsync(StateMachineService contender)
        {
            try
            {
                return (await contender.PublishAsync(Tenant, draft.Id, Version(draft), Ct)).Status;
            }
            catch (ReferenceDataException error)
            {
                return error.ErrorCode;
            }
        }

        var results = await Task.WhenAll(PublishAsync(firstService), PublishAsync(secondService));

        Assert.Single(results, result => result == "Published");
        Assert.Single(results, result => result == "REF-CONCURRENCY-CONFLICT");
        Assert.Equal("Published", (await service.GetAsync(Tenant, draft.Id, Ct)).Status);
    }

    [Fact]
    public async Task Published_and_disabled_events_keep_revision_and_advance_optimistic_version()
    {
        var draft = await CreateAsync(Tenant, "Tenant", "StateEvents");
        var published = await service.PublishAsync(Tenant, draft.Id, Version(draft), Ct);
        var disabled = await service.DisableAsync(Tenant, published.Id, Version(published), Ct);

        var rows = await context.SqlSugar.Ado.SqlQueryAsync<EventPayloadProbe>("""
            SELECT payload AS Payload FROM reference_data_outbox_message
            WHERE module_key='state-machine' ORDER BY rowid
            """);
        Assert.Equal(2, rows.Count);
        using var publishedPayload = JsonDocument.Parse(rows[0].Payload);
        using var disabledPayload = JsonDocument.Parse(rows[1].Payload);
        Assert.Equal("Published", publishedPayload.RootElement.GetProperty("changeType").GetString());
        Assert.Equal("Disabled", disabledPayload.RootElement.GetProperty("changeType").GetString());
        Assert.Equal(published.Revision,
            publishedPayload.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(disabled.Revision,
            disabledPayload.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(published.OptimisticVersion,
            publishedPayload.RootElement.GetProperty("optimisticVersion").GetInt64());
        Assert.Equal(disabled.OptimisticVersion,
            disabledPayload.RootElement.GetProperty("optimisticVersion").GetInt64());
        Assert.True(disabled.OptimisticVersion > published.OptimisticVersion);
    }

    [Fact]
    public async Task Business_revision_exhaustion_returns_conflict_before_clone_creation()
    {
        var id = Guid.NewGuid();
        var token = Guid.NewGuid();
        await context.SqlSugar.Ado.ExecuteCommandAsync($"""
            INSERT INTO reference_data_state_machine_definition
                (id,tenant_nid,scope_type,n_id,name,description,revision,status,source_revision,published_on,
                 published_by,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,
                 optimistic_version,concurrency_version)
            VALUES ('{id}','TENANT-A','Tenant','EXHAUSTED','Exhausted',NULL,{int.MaxValue},'Draft',NULL,NULL,
                    NULL,0,0,0,'StateMachine','2026-09-05T00:00:00Z','2026-09-05T00:00:00Z',0,'{token}')
            """);
        var probe = new IndustrialPlatform.ReferenceData.Domain.StateMachine.StateMachineDefinition(
            "Exhausted", "Exhausted", null, ReferenceScopeType.Tenant, "TENANT-A", null);

        var error = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            repository.GetNextRevisionAsync(probe, Ct));

        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
    }

    [Fact]
    public void Migration_008_has_sqlite_and_postgres_composite_child_foreign_keys_and_constraints()
    {
        Assert.Equal("reference-data-2.7-008", StateMachineMigration.Version);
        var postgres = StateMachineMigration.Sql(true);
        Assert.Contains("reference_data.state_machine_definition", postgres, StringComparison.Ordinal);
        Assert.Contains("timestamptz", postgres, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY (state_machine_definition_id,definition_revision)", postgres, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY (state_machine_definition_id,definition_revision,from_status_nid)", postgres, StringComparison.Ordinal);
        Assert.Contains("UNIQUE(state_machine_definition_id,definition_revision,from_status_nid,action_nid)", postgres, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE SCHEMA", postgres, StringComparison.OrdinalIgnoreCase);
        var sqlite = StateMachineMigration.Sql(false);
        Assert.Contains("reference_data_state_machine_transition", sqlite, StringComparison.Ordinal);
        Assert.DoesNotContain("reference_data.state_machine_transition", sqlite, StringComparison.Ordinal);
    }

    private async Task<StateMachineDetailDto> CreateAsync(
        ReferenceDataActor actor, string scope, string nId) =>
        await service.CreateAsync(actor, new(scope, nId, nId, null,
            [Node("draft", initial: true), Node("done", terminal: true, outcome: "Success")],
            [Transition("draft", "finish", "Finish", "done")]), Ct);

    private static StateNodeRequest Node(
        string nId, bool initial = false, bool terminal = false, string outcome = "None") =>
        new(nId, nId, null, initial, terminal, outcome, "#0088FF", 0);

    private static StateTransitionRequest Transition(
        string from, string action, string actionName, string to) =>
        new(from, action, actionName, to, null);

    private static PublishOrDisableRequest Version(StateMachineDetailDto definition) =>
        new(definition.OptimisticVersion, definition.ConcurrencyVersion, "State machine test");

    private static SqlSugarDbContext CreateContext(string databasePath) =>
        new(Options.Create(new SqlSugarOptions
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={databasePath};Pooling=False",
        }));

    private sealed class EventPayloadProbe
    {
        public string Payload { get; set; } = string.Empty;
    }

    public void Dispose()
    {
        context.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(path)) File.Delete(path);
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }
}
