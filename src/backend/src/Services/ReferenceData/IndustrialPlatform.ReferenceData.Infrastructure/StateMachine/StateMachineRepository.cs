using System.Data.Common;
using System.Globalization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Application.StateMachine;
using IndustrialPlatform.ReferenceData.Contracts.StateMachine;
using IndustrialPlatform.ReferenceData.Contracts.Events;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.StateMachine;
using IndustrialPlatform.ReferenceData.Infrastructure.Caching;
using IndustrialPlatform.ReferenceData.Infrastructure.Outbox;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.StateMachine;

public sealed class StateMachineRepository(SqlSugarDbContext context) : IStateMachineRepository
{
    private ISqlSugarClient Db => context.SqlSugar;
    private bool Postgres => Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL;
    private string Definitions => Postgres
        ? "reference_data.state_machine_definition"
        : "reference_data_state_machine_definition";
    private string Nodes => Postgres
        ? "reference_data.state_machine_node"
        : "reference_data_state_machine_node";
    private string Transitions => Postgres
        ? "reference_data.state_machine_transition"
        : "reference_data_state_machine_transition";

    private ISugarQueryable<StateMachineDefinitionRow> Visible(string tenantNId) =>
        Db.Queryable<StateMachineDefinitionRow>().AS(Definitions)
            .Where(row => !row.IsDeleted && (row.TenantNId == tenantNId || row.TenantNId == null));

    public async Task<(IReadOnlyList<StateMachineSummaryDto> Items, long Total)> SearchAsync(
        string tenantNId,
        StateMachineQuery query,
        CancellationToken cancellationToken)
    {
        var filter = ApplyKeyword(Visible(tenantNId), query.Keyword);
        filter = query.Status is null
            ? filter.Where(row => row.Status == "Draft" || row.Status == "Published")
            : filter.Where(row => row.Status == query.Status);
        if (query.ScopeType is not null) filter = filter.Where(row => row.ScopeType == query.ScopeType);
        var direction = query.Descending ? OrderByType.Desc : OrderByType.Asc;
        filter = query.SortField switch
        {
            "nId" => filter.OrderBy(row => row.NId, direction),
            "name" => filter.OrderBy(row => row.Name, direction),
            "revision" => filter.OrderBy(row => row.Revision, direction),
            "status" => filter.OrderBy(row => row.Status, direction),
            "lastUpdatedOn" => filter.OrderBy(row => row.LastUpdatedOn, direction),
            _ => filter.OrderBy(row => row.LastUpdatedOn, OrderByType.Desc),
        };
        return await PageAsync(filter.OrderBy(row => row.Id), query.PageIndex, query.PageSize, cancellationToken);
    }

    public async Task<(IReadOnlyList<AvailableStateMachineDto> Items, long Total)> ListAvailableAsync(
        string tenantNId,
        AvailableStateMachineQuery query,
        CancellationToken cancellationToken)
    {
        var filter = ApplyKeyword(Visible(tenantNId).Where(row => row.Status == "Published"), query.Keyword)
            .OrderBy(row => row.NId).OrderBy(row => row.ScopeType).OrderBy(row => row.TenantNId)
            .OrderBy(row => row.Id);
        var page = await PageAsync(filter, query.PageIndex, query.PageSize, cancellationToken);
        return (page.Items.Select(item => new AvailableStateMachineDto(
            item.NId, item.Name, item.ScopeType, item.TenantNId, item.Revision, item.PublishedOn!.Value,
            item.NodeCount, item.TransitionCount)).ToArray(), page.Total);
    }

    public async Task<StateMachineDefinition?> GetAsync(
        string tenantNId, Guid id, CancellationToken cancellationToken)
    {
        var rows = await Visible(tenantNId).Where(row => row.Id == id).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<StateMachineDefinition?> GetCurrentAsync(
        string tenantNId,
        string nId,
        string sourceScope,
        CancellationToken cancellationToken)
    {
        var filter = Db.Queryable<StateMachineDefinitionRow>().AS(Definitions)
            .Where(row => !row.IsDeleted && row.NId == nId && row.Status == "Published");
        if (sourceScope == nameof(ReferenceScopeType.Platform))
            filter = filter.Where(row => row.ScopeType == "Platform" && row.TenantNId == null);
        else if (sourceScope == nameof(ReferenceScopeType.Tenant))
            filter = filter.Where(row => row.ScopeType == "Tenant" && row.TenantNId == tenantNId);
        else
            return null;
        var rows = await filter.Take(1).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<StateMachineDefinition?> GetRevisionAsync(
        string tenantNId,
        string nId,
        string sourceScope,
        int revision,
        CancellationToken cancellationToken)
    {
        var filter = Db.Queryable<StateMachineDefinitionRow>().AS(Definitions)
            .Where(row => !row.IsDeleted && row.NId == nId && row.Revision == revision
                && row.PublishedOn != null
                && (row.Status == "Published" || row.Status == "Superseded" || row.Status == "Disabled"));
        if (sourceScope == nameof(ReferenceScopeType.Platform))
            filter = filter.Where(row => row.ScopeType == "Platform" && row.TenantNId == null);
        else if (sourceScope == nameof(ReferenceScopeType.Tenant))
            filter = filter.Where(row => row.ScopeType == "Tenant" && row.TenantNId == tenantNId);
        else
            return null;
        var rows = await filter.Take(1).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<StateMachineDefinition?> GetPublishedInScopeAsync(
        StateMachineDefinition definition, CancellationToken cancellationToken)
    {
        var rows = await Db.Queryable<StateMachineDefinitionRow>().AS(Definitions)
            .Where(row => !row.IsDeleted && row.TenantNId == definition.TenantNId
                && row.NId == definition.NId && row.Status == "Published")
            .Take(1).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<StateMachineDefinition?> GetLastPublishedAsync(
        StateMachineDefinition definition, CancellationToken cancellationToken)
    {
        var rows = await Db.Queryable<StateMachineDefinitionRow>().AS(Definitions)
            .Where(row => !row.IsDeleted && row.Id != definition.Id
                && row.TenantNId == definition.TenantNId && row.NId == definition.NId
                && row.PublishedOn != null)
            .OrderBy(row => row.Revision, OrderByType.Desc)
            .Take(1).ToListAsync(cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    public async Task<int> GetNextRevisionAsync(
        StateMachineDefinition definition, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var revision = await Db.Queryable<StateMachineDefinitionRow>().AS(Definitions)
            .Where(row => !row.IsDeleted && row.TenantNId == definition.TenantNId && row.NId == definition.NId)
            .MaxAsync(row => row.Revision);
        if (revision == int.MaxValue)
            throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
        return revision + 1;
    }

    public async Task CreateAsync(
        StateMachineDefinition definition,
        StateMachineChange? source,
        CancellationToken cancellationToken)
    {
        await Db.Ado.BeginTranAsync();
        try
        {
            if (source is not null)
            {
                var count = await Db.Updateable(RootRow(source.Definition)).AS(Definitions)
                    .Where(row => row.Id == source.Definition.Id && !row.IsDeleted
                        && row.OptimisticVersion == source.ExpectedOptimisticVersion
                        && row.ConcurrencyVersion == source.ExpectedConcurrencyVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (count != 1) throw Conflict();
            }
            await Db.Insertable(RootRow(definition)).AS(Definitions).ExecuteCommandAsync(cancellationToken);
            await InsertChildrenAsync(definition, cancellationToken);
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception exception)
        {
            await Db.Ado.RollbackTranAsync();
            ThrowKnownDatabaseError(exception, concurrencyOnUnique: source is not null);
            throw;
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<StateMachineChange> changes, CancellationToken cancellationToken)
    {
        await Db.Ado.BeginTranAsync();
        try
        {
            foreach (var change in changes)
            {
                var definition = change.Definition;
                var count = await Db.Updateable(RootRow(definition)).AS(Definitions)
                    .Where(row => row.Id == definition.Id && row.TenantNId == definition.TenantNId
                        && !row.IsDeleted && row.OptimisticVersion == change.ExpectedOptimisticVersion
                        && row.ConcurrencyVersion == change.ExpectedConcurrencyVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (count != 1) throw Conflict();
                if (definition.Status == PublicationStatus.Draft)
                {
                    await Db.Deleteable<StateMachineTransitionRow>().AS(Transitions)
                        .Where(row => row.StateMachineDefinitionId == definition.Id)
                        .ExecuteCommandAsync(cancellationToken);
                    await Db.Deleteable<StateMachineNodeRow>().AS(Nodes)
                        .Where(row => row.StateMachineDefinitionId == definition.Id)
                        .ExecuteCommandAsync(cancellationToken);
                    await InsertChildrenAsync(definition, cancellationToken);
                }
                if (definition.Status is PublicationStatus.Published or PublicationStatus.Disabled)
                    await ReferenceDataOutboxWriter.InsertAsync(Db, "state-machine",
                        new ReferenceStateMachineChangedV1(definition.TenantNId,
                            definition.ScopeType.ToString(), null, definition.Id, definition.NId,
                            definition.Revision, definition.Status.ToString(), definition.OptimisticVersion),
                        cancellationToken);
            }
            await ReferenceDataCacheGenerationStore.AdvanceAsync(Db,
                changes.Where(change => change.Definition.Status != PublicationStatus.Draft)
                    .SelectMany(change => CachePatterns(change.Definition)), cancellationToken);
            await Db.Ado.CommitTranAsync();
        }
        catch (Exception exception)
        {
            await Db.Ado.RollbackTranAsync();
            ThrowKnownDatabaseError(exception, concurrencyOnUnique: true);
            throw;
        }
    }

    private async Task<(IReadOnlyList<StateMachineSummaryDto> Items, long Total)> PageAsync(
        ISugarQueryable<StateMachineDefinitionRow> filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var total = await filter.CountAsync(cancellationToken);
        var rows = await filter.ToPageListAsync(pageIndex, pageSize, cancellationToken);
        var loaded = await LoadAsync(rows, cancellationToken);
        return (loaded.Select(StateMachineService.ToSummary).ToArray(), total);
    }

    private static ISugarQueryable<StateMachineDefinitionRow> ApplyKeyword(
        ISugarQueryable<StateMachineDefinitionRow> filter, string? keyword)
    {
        if (keyword is not { Length: > 0 }) return filter;
        keyword = keyword.ToUpperInvariant();
        return filter.Where(row => row.NId.Contains(keyword)
            || SqlFunc.Contains(SqlFunc.ToUpper(row.Name), keyword));
    }

    private async Task<IReadOnlyList<StateMachineDefinition>> LoadAsync(
        List<StateMachineDefinitionRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(row => row.Id).ToArray();
        var nodeRows = await Db.Queryable<StateMachineNodeRow>().AS(Nodes)
            .Where(row => ids.Contains(row.StateMachineDefinitionId))
            .OrderBy(row => row.Sort).OrderBy(row => row.NId).ToListAsync(cancellationToken);
        var transitionRows = await Db.Queryable<StateMachineTransitionRow>().AS(Transitions)
            .Where(row => ids.Contains(row.StateMachineDefinitionId))
            .OrderBy(row => row.FromStatusNId).OrderBy(row => row.ActionNId).ToListAsync(cancellationToken);
        return rows.Select(row => StateMachineDefinition.Restore(new(
                row.Id, row.NId, row.Name, row.Description, Enum.Parse<ReferenceScopeType>(row.ScopeType),
                row.TenantNId, row.Revision, Enum.Parse<PublicationStatus>(row.Status), row.SourceRevision,
                row.PublishedOn is null ? null : Timestamp(row.PublishedOn.Value), row.PublishedBy,
                row.IsFrozen, row.IsLocked, row.IsDeleted, Timestamp(row.CreatedOn), Timestamp(row.LastUpdatedOn),
                row.OptimisticVersion, row.ConcurrencyVersion),
            nodeRows.Where(node => node.StateMachineDefinitionId == row.Id && node.DefinitionRevision == row.Revision)
                .Select(node => StateNode.Restore(node.Id, node.StateMachineDefinitionId, node.NId, node.Name,
                    node.Description, node.IsInitial, node.IsTerminal, Enum.Parse<StateOutcome>(node.Outcome),
                    node.Color, node.Sort)).ToArray(),
            transitionRows.Where(transition => transition.StateMachineDefinitionId == row.Id
                    && transition.DefinitionRevision == row.Revision)
                .Select(transition => StateTransition.Restore(transition.Id, transition.StateMachineDefinitionId,
                    transition.FromStatusNId, transition.ActionNId, transition.ActionName,
                    transition.ToStatusNId, transition.Description)).ToArray())).ToArray();
    }

    private async Task InsertChildrenAsync(
        StateMachineDefinition definition, CancellationToken cancellationToken)
    {
        if (definition.Nodes.Count > 0)
            await Db.Insertable(definition.Nodes.Select(node => NodeRow(node, definition.Revision)).ToArray())
                .AS(Nodes).ExecuteCommandAsync(cancellationToken);
        if (definition.Transitions.Count > 0)
            await Db.Insertable(definition.Transitions
                .Select(transition => TransitionRow(transition, definition.Revision)).ToArray())
                .AS(Transitions).ExecuteCommandAsync(cancellationToken);
    }

    private DateTimeOffset Timestamp(DateTimeOffset value) =>
        Postgres ? value.ToUniversalTime() : new(value.DateTime, TimeSpan.Zero);

    private static StateMachineDefinitionRow RootRow(StateMachineDefinition definition) => new()
    {
        Id = definition.Id,
        TenantNId = definition.TenantNId,
        ScopeType = definition.ScopeType.ToString(),
        NId = definition.NId,
        Name = definition.Name,
        Description = definition.Description,
        Revision = definition.Revision,
        Status = definition.Status.ToString(),
        SourceRevision = definition.SourceRevision,
        PublishedOn = definition.PublishedOn,
        PublishedBy = definition.PublishedBy,
        IsFrozen = definition.IsFrozen,
        IsLocked = definition.IsLocked,
        IsDeleted = definition.IsDeleted,
        EntityType = definition.EntityType,
        CreatedOn = definition.CreatedOn,
        LastUpdatedOn = definition.LastUpdatedOn,
        OptimisticVersion = definition.OptimisticVersion,
        ConcurrencyVersion = definition.ConcurrencyVersion,
    };

    private static StateMachineNodeRow NodeRow(StateNode node, int revision) => new()
    {
        Id = node.Id,
        StateMachineDefinitionId = node.StateMachineDefinitionId,
        DefinitionRevision = revision,
        NId = node.NId,
        Name = node.Name,
        Description = node.Description,
        IsInitial = node.IsInitial,
        IsTerminal = node.IsTerminal,
        Outcome = node.Outcome.ToString(),
        Color = node.Color,
        Sort = node.Sort,
    };

    private static StateMachineTransitionRow TransitionRow(StateTransition transition, int revision) => new()
    {
        Id = transition.Id,
        StateMachineDefinitionId = transition.StateMachineDefinitionId,
        DefinitionRevision = revision,
        FromStatusNId = transition.FromStatusNId,
        ActionNId = transition.ActionNId,
        ActionName = transition.ActionName,
        ToStatusNId = transition.ToStatusNId,
        Description = transition.Description,
    };

    private static IEnumerable<ReferenceDataCachePattern> CachePatterns(StateMachineDefinition definition)
    {
        var source = definition.ScopeType.ToString();
        var tenantKey = ReferenceDataCacheKeys.SourceTenantKey(source, definition.TenantNId);
        yield return ReferenceDataCacheKeys.StateMachineState(source, tenantKey, definition.NId, "current");
        yield return ReferenceDataCacheKeys.StateMachineState(source, tenantKey, definition.NId,
            definition.Revision.ToString(CultureInfo.InvariantCulture));
        yield return ReferenceDataCacheKeys.AvailableStateMachines();
    }

    private static ReferenceDataException Conflict() =>
        new("REF-CONCURRENCY-CONFLICT", 409);

    private static void ThrowKnownDatabaseError(Exception exception, bool concurrencyOnUnique)
    {
        if (exception is ReferenceDataException) return;
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }
                || current is Npgsql.PostgresException { SqlState: "23505" })
                throw concurrencyOnUnique
                    ? Conflict()
                    : new ReferenceDataException("REF-STATE-MACHINE-INVALID", 422);
            if (current is DbException or SqlSugarException)
                throw new ReferenceDataException("503", 503);
        }
    }
}
