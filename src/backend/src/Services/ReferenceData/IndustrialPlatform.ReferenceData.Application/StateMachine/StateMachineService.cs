using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.StateMachine;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.StateMachine;

namespace IndustrialPlatform.ReferenceData.Application.StateMachine;

public sealed class StateMachineService
{
    private readonly IStateMachineRepository repository;
    private readonly IReferenceDataCache cache;

    public StateMachineService(IStateMachineRepository repository) :
        this(repository, NullReferenceDataCache.Instance)
    { }

    public StateMachineService(IStateMachineRepository repository, IReferenceDataCache cache)
    {
        this.repository = repository;
        this.cache = cache;
    }

    public async Task<(IReadOnlyList<StateMachineSummaryDto> Items, long Total)> SearchAsync(
        ReferenceDataActor actor, StateMachineQuery query, CancellationToken cancellationToken)
    {
        ValidatePage(query.PageIndex, query.PageSize, query.Keyword);
        if (query.ScopeType is not null) _ = ParseScope(query.ScopeType);
        if (query.Status is not null
            && !Enum.GetNames<PublicationStatus>().Contains(query.Status, StringComparer.Ordinal))
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "status");
        return await repository.SearchAsync(actor.TenantNId, query, cancellationToken);
    }

    public async Task<StateMachineDetailDto> GetAsync(
        ReferenceDataActor actor, Guid id, CancellationToken cancellationToken) =>
        MapDetail(await LoadAsync(actor, id, cancellationToken));

    public async Task<StateMachinePublicationCheckDto> CheckPublicationAsync(
        ReferenceDataActor actor, Guid id, CancellationToken cancellationToken)
    {
        var definition = await LoadAsync(actor, id, cancellationToken);
        var previous = await repository.GetLastPublishedAsync(definition, cancellationToken);
        var errors = new List<StateMachinePublicationIssue>();
        try
        {
            definition.CheckPublication();
        }
        catch (ReferenceDataException error)
        {
            errors.Add(new(error.ErrorCode, error.Field ?? "stateMachine"));
        }

        var previousNodes = previous?.Nodes ?? [];
        var previousTransitions = previous?.Transitions ?? [];
        var previousNodeByNId = previousNodes.ToDictionary(node => node.NId, StringComparer.Ordinal);
        var currentNodeByNId = definition.Nodes.ToDictionary(node => node.NId, StringComparer.Ordinal);
        var previousTransitionByKey = previousTransitions.ToDictionary(TransitionKey, StringComparer.Ordinal);
        var currentTransitionByKey = definition.Transitions.ToDictionary(TransitionKey, StringComparer.Ordinal);
        return new(previous?.Revision,
            currentNodeByNId.Keys.Except(previousNodeByNId.Keys, StringComparer.Ordinal).Order().ToArray(),
            previousNodeByNId.Keys.Except(currentNodeByNId.Keys, StringComparer.Ordinal).Order().ToArray(),
            currentNodeByNId.Keys.Intersect(previousNodeByNId.Keys, StringComparer.Ordinal)
                .Where(key => NodeChanged(previousNodeByNId[key], currentNodeByNId[key])).Order().ToArray(),
            currentTransitionByKey.Keys.Except(previousTransitionByKey.Keys, StringComparer.Ordinal).Order().ToArray(),
            previousTransitionByKey.Keys.Except(currentTransitionByKey.Keys, StringComparer.Ordinal).Order().ToArray(),
            currentTransitionByKey.Keys.Intersect(previousTransitionByKey.Keys, StringComparer.Ordinal)
                .Where(key => TransitionChanged(previousTransitionByKey[key], currentTransitionByKey[key]))
                .Order().ToArray(), errors);
    }

    public async Task<StateMachineDetailDto> CreateAsync(
        ReferenceDataActor actor, CreateStateMachineRequest request, CancellationToken cancellationToken)
    {
        var scope = ParseScope(request.ScopeType);
        if (scope == ReferenceScopeType.Platform && !actor.CanManagePlatform)
            throw new ReferenceDataException("ID_PERMISSION_DENIED", 403);
        var definition = new StateMachineDefinition(request.NId, request.Name, request.Description, scope,
            scope == ReferenceScopeType.Platform ? null : actor.TenantNId, null);
        definition.Update(request.Name, request.Description, ParseNodes(request.Nodes),
            ParseTransitions(request.Transitions));
        await repository.CreateAsync(definition, null, cancellationToken);
        return MapDetail(definition);
    }

    public async Task<StateMachineDetailDto> UpdateAsync(
        ReferenceDataActor actor,
        Guid id,
        UpdateStateMachineRequest request,
        CancellationToken cancellationToken)
    {
        var definition = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        definition.Update(request.Name, request.Description, ParseNodes(request.Nodes),
            ParseTransitions(request.Transitions));
        await repository.SaveAsync(
            [new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion)],
            cancellationToken);
        await InvalidateRuntimeStateAsync(definition, null, CancellationToken.None);
        return MapDetail(definition);
    }

    public async Task<StateMachineDetailDto> CloneAsync(
        ReferenceDataActor actor,
        Guid id,
        PublishOrDisableRequest request,
        CancellationToken cancellationToken)
    {
        ValidateReason(request.ChangeReason, required: false);
        var source = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        var clone = source.Clone(await repository.GetNextRevisionAsync(source, cancellationToken));
        await repository.CreateAsync(clone,
            new(source, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion), cancellationToken);
        return MapDetail(clone);
    }

    public async Task<StateMachineDetailDto> PublishAsync(
        ReferenceDataActor actor,
        Guid id,
        PublishOrDisableRequest request,
        CancellationToken cancellationToken)
    {
        ValidateReason(request.ChangeReason, required: false);
        var definition = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        definition.CheckPublication();
        var previous = await repository.GetPublishedInScopeAsync(definition, cancellationToken);
        var changes = new List<StateMachineChange>();
        if (previous is not null && previous.Id != definition.Id)
        {
            var expectedVersion = previous.OptimisticVersion;
            var expectedToken = previous.ConcurrencyVersion;
            previous.Supersede();
            changes.Add(new(previous, expectedVersion, expectedToken));
        }
        definition.Publish(actor.UserNId);
        changes.Add(new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion));
        await repository.SaveAsync(changes, cancellationToken);
        await InvalidateRuntimeStateAsync(definition, previous?.Revision, CancellationToken.None);
        return MapDetail(definition);
    }

    public async Task<StateMachineDetailDto> DisableAsync(
        ReferenceDataActor actor,
        Guid id,
        PublishOrDisableRequest request,
        CancellationToken cancellationToken)
    {
        ValidateReason(request.ChangeReason, required: true);
        var definition = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        definition.Disable();
        await repository.SaveAsync(
            [new(definition, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion)],
            cancellationToken);
        await InvalidateRuntimeStateAsync(definition, null, CancellationToken.None);
        return MapDetail(definition);
    }

    public async Task<(IReadOnlyList<AvailableStateMachineDto> Items, long Total)> ListAvailableAsync(
        ReferenceDataActor actor,
        AvailableStateMachineQuery query,
        CancellationToken cancellationToken)
    {
        ValidatePage(query.PageIndex, query.PageSize, query.Keyword);
        var key = ReferenceDataCacheKeys.StateMachine("Effective",
            ReferenceDataCacheKeys.TenantKey(actor.TenantNId), "list",
            ReferenceDataCacheKeys.PageKey(query.PageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                query.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture), query.Keyword));
        var cached = await cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.StateMachineCurrent, async () =>
        {
            var page = await repository.ListAvailableAsync(actor.TenantNId, query, cancellationToken);
            return new ReferenceDataCachePage<AvailableStateMachineDto>(page.Items, page.Total,
                page.Items.Count == 0 ? 0 : page.Items.Max(item => item.Revision));
        }, cancellationToken);
        return (cached.Items, cached.Total);
    }

    public async Task<StateMachineDefinitionDto> GetCurrentAsync(
        ReferenceDataActor actor,
        string nId,
        string? sourceScope,
        string? sourceTenantNId,
        CancellationToken cancellationToken)
    {
        var source = ResolveSource(actor, sourceScope, sourceTenantNId);
        var normalizedNId = NormalizeNId(nId);
        var key = ReferenceDataCacheKeys.StateMachine(source,
            ReferenceDataCacheKeys.SourceTenantKey(source, sourceTenantNId), normalizedNId, "current");
        return await cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.StateMachineCurrent, async () =>
        {
            var definition = await repository.GetCurrentAsync(actor.TenantNId, normalizedNId, source,
                cancellationToken) ?? throw NotFound();
            return MapRuntime(definition);
        }, cancellationToken);
    }

    public async Task<StateMachineDefinitionDto> GetRevisionAsync(
        ReferenceDataActor actor,
        string nId,
        int revision,
        string? sourceScope,
        string? sourceTenantNId,
        CancellationToken cancellationToken)
    {
        if (revision < 1) throw NotFound();
        var source = ResolveSource(actor, sourceScope, sourceTenantNId);
        var normalizedNId = NormalizeNId(nId);
        var key = ReferenceDataCacheKeys.StateMachine(source,
            ReferenceDataCacheKeys.SourceTenantKey(source, sourceTenantNId), normalizedNId,
            revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return await cache.GetOrLoadAsync(key, ReferenceDataCacheTtl.StateMachineRevision, async () =>
        {
            var definition = await repository.GetRevisionAsync(actor.TenantNId, normalizedNId, source,
                revision, cancellationToken) ?? throw NotFound();
            return MapRuntime(definition);
        }, cancellationToken);
    }

    public async Task<TransitionEvaluationDto> EvaluateAsync(
        ReferenceDataActor actor,
        string nId,
        TransitionEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Revision < 1) throw NotFound();
        var definition = await GetRevisionAsync(actor, nId, request.Revision, request.SourceScope,
            request.SourceTenantNId, cancellationToken);
        string from;
        string action;
        try
        {
            from = ReferenceValidation.NId(request.FromStatusNId, item: true);
            action = ReferenceValidation.NId(request.ActionNId, item: true);
        }
        catch (ReferenceDataException)
        {
            throw new ReferenceDataException("REF-VALIDATION-FAILED");
        }
        var nodeExists = definition.Nodes.Any(node => node.NId == from);
        var transition = definition.Transitions.SingleOrDefault(item =>
            item.FromStatusNId == from && item.ActionNId == action);
        var allowed = nodeExists && transition is not null;
        return new(definition.NId, definition.Revision, definition.SourceScope,
            definition.SourceTenantNId, from, action, allowed, transition?.ToStatusNId,
            !nodeExists ? "FROM_STATUS_NOT_DEFINED" : transition is null ? "TRANSITION_NOT_DEFINED" : null);
    }

    private async Task InvalidateRuntimeStateAsync(StateMachineDefinition definition, int? previousRevision,
        CancellationToken cancellationToken)
    {
        var source = definition.ScopeType.ToString();
        var tenantKey = ReferenceDataCacheKeys.SourceTenantKey(source, definition.TenantNId);
        await cache.InvalidateAsync(ReferenceDataCacheKeys.StateMachineState(source, tenantKey,
            definition.NId, "current"), cancellationToken);
        await cache.InvalidateAsync(ReferenceDataCacheKeys.StateMachineState(source, tenantKey,
            definition.NId, definition.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            cancellationToken);
        if (previousRevision is not null && previousRevision != definition.Revision)
            await cache.InvalidateAsync(ReferenceDataCacheKeys.StateMachineState(source, tenantKey,
                definition.NId,
                previousRevision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                cancellationToken);
        await cache.InvalidateAsync(ReferenceDataCacheKeys.AvailableStateMachines(), cancellationToken);
    }

    private async Task<StateMachineDefinition> LoadAsync(
        ReferenceDataActor actor, Guid id, CancellationToken cancellationToken) =>
        await repository.GetAsync(actor.TenantNId, id, cancellationToken) ?? throw NotFound();

    private async Task<StateMachineDefinition> LoadWritableAsync(
        ReferenceDataActor actor,
        Guid id,
        long version,
        Guid token,
        CancellationToken cancellationToken)
    {
        var definition = await LoadAsync(actor, id, cancellationToken);
        if (definition.ScopeType == ReferenceScopeType.Platform && !actor.CanManagePlatform)
            throw new ReferenceDataException("ID_PERMISSION_DENIED", 403);
        definition.CheckVersion(version, token);
        return definition;
    }

    private static string TransitionKey(StateTransition transition) =>
        $"{transition.FromStatusNId}:{transition.ActionNId}";

    private static bool NodeChanged(StateNode previous, StateNode current) =>
        previous.Name != current.Name || previous.Description != current.Description
        || previous.IsInitial != current.IsInitial || previous.IsTerminal != current.IsTerminal
        || previous.Outcome != current.Outcome || previous.Color != current.Color
        || previous.Sort != current.Sort;

    private static bool TransitionChanged(StateTransition previous, StateTransition current) =>
        previous.ActionName != current.ActionName || previous.ToStatusNId != current.ToStatusNId
        || previous.Description != current.Description;

    private static StateNode[] ParseNodes(IReadOnlyList<StateNodeRequest> requests)
    {
        if (requests is null || requests.Count is < 1 or > 200)
            throw new ReferenceDataException("REF-STATE-MACHINE-INVALID", 422, "nodes");
        var result = new StateNode[requests.Count];
        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            if (request is null)
                throw new ReferenceDataException("REF-STATE-MACHINE-INVALID", 422, $"nodes[{index}]");
            try
            {
                if (!Enum.TryParse<StateOutcome>(request.Outcome, ignoreCase: false, out var outcome)
                    || !Enum.IsDefined(outcome))
                    throw new ReferenceDataException("REF-STATE-MACHINE-INVALID", 422, "outcome");
                result[index] = new(request.NId, request.Name, request.Description, request.IsInitial,
                    request.IsTerminal, outcome, request.Color, request.Sort);
            }
            catch (ReferenceDataException error)
            {
                throw new ReferenceDataException("REF-STATE-MACHINE-INVALID", 422,
                    $"nodes[{index}].{error.Field ?? "nId"}");
            }
        }
        return result;
    }

    private static StateTransition[] ParseTransitions(IReadOnlyList<StateTransitionRequest> requests)
    {
        if (requests is null || requests.Count > 1000)
            throw new ReferenceDataException("REF-STATE-MACHINE-INVALID", 422, "transitions");
        var result = new StateTransition[requests.Count];
        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            if (request is null)
                throw new ReferenceDataException("REF-STATE-MACHINE-INVALID", 422, $"transitions[{index}]");
            try
            {
                result[index] = new(request.FromStatusNId, request.ActionNId, request.ActionName,
                    request.ToStatusNId, request.Description);
            }
            catch (ReferenceDataException error)
            {
                throw new ReferenceDataException("REF-STATE-MACHINE-INVALID", 422,
                    $"transitions[{index}].{error.Field ?? "fromStatusNId"}");
            }
        }
        return result;
    }

    private static ReferenceScopeType ParseScope(string value)
    {
        if (!Enum.TryParse<ReferenceScopeType>(value, ignoreCase: false, out var scope) || !Enum.IsDefined(scope))
            throw new ReferenceDataException("REF-SCOPE-INVALID");
        if (scope == ReferenceScopeType.Factory)
            throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409);
        return scope;
    }

    private static string ResolveSource(
        ReferenceDataActor actor, string? sourceScope, string? sourceTenantNId)
    {
        if (sourceScope is null) throw new ReferenceDataException("REF-SCOPE-INVALID");
        var scope = ParseScope(sourceScope);
        if (scope == ReferenceScopeType.Platform)
        {
            if (sourceTenantNId is not null) throw new ReferenceDataException("REF-SCOPE-INVALID");
            return nameof(ReferenceScopeType.Platform);
        }
        if (string.IsNullOrWhiteSpace(sourceTenantNId))
            throw new ReferenceDataException("REF-SCOPE-INVALID");
        if (!string.Equals(actor.TenantNId, sourceTenantNId, StringComparison.Ordinal)) throw NotFound();
        return nameof(ReferenceScopeType.Tenant);
    }

    private static string NormalizeNId(string value)
    {
        try { return ReferenceValidation.NId(value); }
        catch (ReferenceDataException) { throw NotFound(); }
    }

    private static void ValidatePage(int pageIndex, int pageSize, string? keyword)
    {
        if (pageIndex < 1 || pageSize is < 1 or > 100 || keyword?.Length > 200)
            throw new ReferenceDataException("REF-VALIDATION-FAILED");
    }

    private static void ValidateReason(string? reason, bool required)
    {
        if ((required && string.IsNullOrWhiteSpace(reason)) || reason?.Length > 1000)
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "changeReason");
    }

    public static StateMachineSummaryDto ToSummary(StateMachineDefinition definition) => new(
        definition.Id, definition.NId, definition.Name, definition.Description,
        definition.ScopeType.ToString(), definition.TenantNId, definition.Revision, definition.Status.ToString(),
        definition.SourceRevision, definition.Nodes.Count, definition.Transitions.Count,
        definition.OptimisticVersion, definition.ConcurrencyVersion, definition.LastUpdatedOn,
        definition.PublishedOn, definition.PublishedBy, definition.IsFrozen, definition.IsLocked);

    private static StateMachineDetailDto MapDetail(StateMachineDefinition definition) => new(
        definition.Id, definition.NId, definition.Name, definition.Description,
        definition.ScopeType.ToString(), definition.TenantNId, definition.Revision, definition.Status.ToString(),
        definition.SourceRevision, definition.PublishedOn, definition.PublishedBy,
        definition.Nodes.Select(MapNode).ToArray(), definition.Transitions.Select(MapTransition).ToArray(),
        definition.OptimisticVersion, definition.ConcurrencyVersion, definition.LastUpdatedOn,
        definition.IsFrozen, definition.IsLocked);

    private static StateMachineDefinitionDto MapRuntime(StateMachineDefinition definition) => new(
        definition.NId, definition.Name, definition.Description, definition.ScopeType.ToString(),
        definition.TenantNId, definition.Revision, definition.Status.ToString(), definition.SourceRevision,
        definition.PublishedOn, definition.Nodes.Select(node => new RuntimeStateNodeDto(
            node.NId, node.Name, node.Description, node.IsInitial, node.IsTerminal, node.Outcome.ToString(),
            node.Color, node.Sort)).ToArray(), definition.Transitions.Select(transition =>
            new RuntimeStateTransitionDto(transition.FromStatusNId, transition.ActionNId, transition.ActionName,
                transition.ToStatusNId, transition.Description)).ToArray());

    private static StateNodeDto MapNode(StateNode node) => new(
        node.Id, node.NId, node.Name, node.Description, node.IsInitial, node.IsTerminal,
        node.Outcome.ToString(), node.Color, node.Sort);

    private static StateTransitionDto MapTransition(StateTransition transition) => new(
        transition.Id, transition.FromStatusNId, transition.ActionNId, transition.ActionName,
        transition.ToStatusNId, transition.Description);

    private static ReferenceDataException NotFound() =>
        new("REF-STATE-MACHINE-NOT-FOUND", 404);
}
