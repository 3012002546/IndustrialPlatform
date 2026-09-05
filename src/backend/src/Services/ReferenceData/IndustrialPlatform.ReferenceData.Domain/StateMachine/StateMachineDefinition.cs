using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.SharedKernel.Entities;
using System.Diagnostics.CodeAnalysis;

namespace IndustrialPlatform.ReferenceData.Domain.StateMachine;

public enum StateOutcome { None, Success, Failure, Skipped }

public sealed class StateMachineDefinition : AggregateRoot
{
    private StateNode[] nodes = [];
    private StateTransition[] transitions = [];

    public string NId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public ReferenceScopeType ScopeType { get; private set; }
    public string? TenantNId { get; private set; }
    public int Revision { get; private set; } = 1;
    public PublicationStatus Status { get; private set; } = PublicationStatus.Draft;
    public int? SourceRevision { get; private set; }
    public DateTimeOffset? PublishedOn { get; private set; }
    public string? PublishedBy { get; private set; }
    public IReadOnlyList<StateNode> Nodes => nodes.ToArray();
    public IReadOnlyList<StateTransition> Transitions => transitions.ToArray();

    public StateMachineDefinition(
        string nId,
        string name,
        string? description,
        ReferenceScopeType scopeType,
        string? tenantNId,
        string? scopeNId)
    {
        ReferenceValidation.Scope(scopeType, tenantNId, scopeNId);
        NId = ReferenceValidation.NId(nId);
        Name = ReferenceValidation.Name(name);
        Description = ReferenceValidation.Description(description);
        ScopeType = scopeType;
        TenantNId = tenantNId;
    }

    public void Update(
        string name,
        string? description,
        IReadOnlyList<StateNode> requestedNodes,
        IReadOnlyList<StateTransition> requestedTransitions)
    {
        EnsureDraft();
        if (requestedNodes is null || requestedNodes.Count is < 1 or > 200)
            Invalid("nodes");
        if (requestedTransitions is null || requestedTransitions.Count > 1000)
            Invalid("transitions");
        if (requestedNodes.Any(node => node is null) || requestedTransitions.Any(transition => transition is null))
            Invalid("nodes");
        if (requestedNodes.Select(node => node.NId).Distinct(StringComparer.Ordinal).Count() != requestedNodes.Count)
            Invalid("nodes");

        var nodeNIds = requestedNodes.Select(node => node.NId).ToHashSet(StringComparer.Ordinal);
        if (requestedTransitions.Any(transition =>
                !nodeNIds.Contains(transition.FromStatusNId) || !nodeNIds.Contains(transition.ToStatusNId)))
            Invalid("transitions");
        if (requestedTransitions.Select(transition => (transition.FromStatusNId, transition.ActionNId))
            .Distinct().Count() != requestedTransitions.Count)
            Invalid("transitions");

        var nextName = ReferenceValidation.Name(name);
        var nextDescription = ReferenceValidation.Description(description);
        var nextNodes = requestedNodes.Select(requested =>
        {
            var current = nodes.SingleOrDefault(node => node.NId == requested.NId);
            return requested.CopyFor(Id, current?.Id);
        }).OrderBy(node => node.Sort).ThenBy(node => node.NId, StringComparer.Ordinal).ToArray();
        var nextTransitions = requestedTransitions.Select(requested =>
        {
            var current = transitions.SingleOrDefault(transition =>
                transition.FromStatusNId == requested.FromStatusNId && transition.ActionNId == requested.ActionNId);
            return requested.CopyFor(Id, current?.Id);
        }).OrderBy(transition => transition.FromStatusNId, StringComparer.Ordinal)
            .ThenBy(transition => transition.ActionNId, StringComparer.Ordinal).ToArray();

        if (Name == nextName && Description == nextDescription
            && SameNodes(nodes, nextNodes) && SameTransitions(transitions, nextTransitions)) return;
        EnsureCapacity();
        Name = nextName;
        Description = nextDescription;
        nodes = nextNodes;
        transitions = nextTransitions;
        Touch();
    }

    public void CheckVersion(long expectedOptimisticVersion, Guid expectedConcurrencyVersion)
    {
        if (OptimisticVersion != expectedOptimisticVersion || ConcurrencyVersion != expectedConcurrencyVersion)
            throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    public void CheckPublication()
    {
        EnsureDraft();
        if (nodes.Count(node => node.IsInitial) != 1) Invalid("nodes");
        if (transitions.Any(transition => nodes.Single(node => node.NId == transition.FromStatusNId).IsTerminal))
            Invalid("transitions");
        if (transitions.GroupBy(transition => transition.ActionNId, StringComparer.Ordinal)
            .Any(group => group.Select(transition => transition.ActionName).Distinct(StringComparer.Ordinal).Count() != 1))
            Invalid("transitions");

        var reached = new HashSet<string>(StringComparer.Ordinal) { nodes.Single(node => node.IsInitial).NId };
        var queue = new Queue<string>(reached);
        while (queue.TryDequeue(out var current))
        {
            foreach (var target in transitions.Where(transition => transition.FromStatusNId == current)
                .Select(transition => transition.ToStatusNId))
                if (reached.Add(target)) queue.Enqueue(target);
        }
        if (reached.Count != nodes.Length) Invalid("nodes");
    }

    public void Publish(string userNId)
    {
        CheckPublication();
        if (string.IsNullOrWhiteSpace(userNId))
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "userNId");
        EnsureCapacity();
        Status = PublicationStatus.Published;
        PublishedOn = DateTimeOffset.UtcNow;
        PublishedBy = userNId;
        Touch();
    }

    public void Supersede()
    {
        EnsureMutable();
        EnsureCapacity();
        if (Status != PublicationStatus.Published)
            throw new ReferenceDataException("REF-INVALID-STATE", 409);
        Status = PublicationStatus.Superseded;
        Touch();
    }

    public void Disable()
    {
        EnsureMutable();
        EnsureCapacity();
        if (Status is not (PublicationStatus.Draft or PublicationStatus.Published))
            throw new ReferenceDataException("REF-INVALID-STATE", 409);
        Status = PublicationStatus.Disabled;
        Touch();
    }

    public StateMachineDefinition Clone(int revision)
    {
        EnsureMutable();
        if (PublishedOn is null || revision <= Revision)
            throw new ReferenceDataException("REF-INVALID-STATE", 409);
        var clone = new StateMachineDefinition(NId, Name, Description, ScopeType, TenantNId, null)
        {
            Revision = revision,
            SourceRevision = Revision,
        };
        clone.nodes = nodes.Select(node => node.CopyFor(clone.Id)).ToArray();
        clone.transitions = transitions.Select(transition => transition.CopyFor(clone.Id)).ToArray();
        return clone;
    }

    public TransitionEvaluation Evaluate(string fromStatusNId, string actionNId)
    {
        var from = StateMachineValidation.NId(fromStatusNId, "fromStatusNId");
        var action = StateMachineValidation.NId(actionNId, "actionNId");
        if (nodes.All(node => node.NId != from))
            return new(false, null, "FROM_STATUS_NOT_DEFINED");
        var transition = transitions.SingleOrDefault(item =>
            item.FromStatusNId == from && item.ActionNId == action);
        return transition is null
            ? new(false, null, "TRANSITION_NOT_DEFINED")
            : new(true, transition.ToStatusNId, null);
    }

    public new void Freeze() { if (!IsFrozen) EnsureCapacity(); base.Freeze(); }
    public new void Unfreeze() { if (IsFrozen) EnsureCapacity(); base.Unfreeze(); }
    public new void Lock() { if (!IsLocked) EnsureCapacity(); base.Lock(); }
    public new void Unlock() { if (IsLocked) EnsureCapacity(); base.Unlock(); }
    public new void MarkDeleted() { if (!IsDeleted) EnsureCapacity(); base.MarkDeleted(); }
    public new void Restore() { if (IsDeleted) EnsureCapacity(); base.Restore(); }

    public static StateMachineDefinition Restore(
        StateMachineDefinitionState state,
        IReadOnlyList<StateNode> nodes,
        IReadOnlyList<StateTransition> transitions)
    {
        var definition = new StateMachineDefinition(
            state.NId, state.Name, state.Description, state.ScopeType, state.TenantNId, null)
        {
            Id = state.Id,
            Revision = state.Revision,
            Status = state.Status,
            SourceRevision = state.SourceRevision,
            PublishedOn = state.PublishedOn,
            PublishedBy = state.PublishedBy,
            IsFrozen = state.IsFrozen,
            IsLocked = state.IsLocked,
            IsDeleted = state.IsDeleted,
            CreatedOn = state.CreatedOn,
            LastUpdatedOn = state.LastUpdatedOn,
            OptimisticVersion = state.OptimisticVersion,
            ConcurrencyVersion = state.ConcurrencyVersion,
        };
        definition.nodes = nodes.Select(node => node.CopyFor(state.Id, node.Id)).ToArray();
        definition.transitions = transitions.Select(transition => transition.CopyFor(state.Id, transition.Id)).ToArray();
        return definition;
    }

    private void EnsureDraft()
    {
        EnsureMutable();
        if (Status != PublicationStatus.Draft)
            throw new ReferenceDataException("REF-INVALID-STATE", 409);
    }

    private void EnsureMutable()
    {
        if (IsDeleted || IsFrozen || IsLocked)
            throw new ReferenceDataException("REF-INVALID-STATE", 409);
    }

    private void EnsureCapacity()
    {
        if (OptimisticVersion == long.MaxValue)
            throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    private static bool SameNodes(StateNode[] left, StateNode[] right) =>
        left.Length == right.Length && left.Zip(right).All(pair => pair.First.SameDefinition(pair.Second));

    private static bool SameTransitions(
        StateTransition[] left,
        StateTransition[] right) =>
        left.Length == right.Length && left.Zip(right).All(pair => pair.First.SameDefinition(pair.Second));

    [DoesNotReturn]
    private static void Invalid(string field) =>
        throw new ReferenceDataException("REF-STATE-MACHINE-INVALID", 422, field);
}

public sealed record StateMachineDefinitionState(
    Guid Id,
    string NId,
    string Name,
    string? Description,
    ReferenceScopeType ScopeType,
    string? TenantNId,
    int Revision,
    PublicationStatus Status,
    int? SourceRevision,
    DateTimeOffset? PublishedOn,
    string? PublishedBy,
    bool IsFrozen,
    bool IsLocked,
    bool IsDeleted,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    long OptimisticVersion,
    Guid ConcurrencyVersion);

public sealed class StateNode
{
    public Guid Id { get; }
    public Guid StateMachineDefinitionId { get; }
    public string NId { get; }
    public string Name { get; }
    public string? Description { get; }
    public bool IsInitial { get; }
    public bool IsTerminal { get; }
    public StateOutcome Outcome { get; }
    public string? Color { get; }
    public int Sort { get; }

    public StateNode(
        string nId,
        string name,
        string? description,
        bool isInitial,
        bool isTerminal,
        StateOutcome outcome,
        string? color,
        int sort)
        : this(Guid.NewGuid(), Guid.Empty, nId, name, description, isInitial, isTerminal, outcome, color, sort) { }

    private StateNode(
        Guid id,
        Guid definitionId,
        string nId,
        string name,
        string? description,
        bool isInitial,
        bool isTerminal,
        StateOutcome outcome,
        string? color,
        int sort)
    {
        if (!Enum.IsDefined(outcome)) StateMachineValidation.Invalid("outcome");
        if (sort < 0) StateMachineValidation.Invalid("sort");
        Id = id;
        StateMachineDefinitionId = definitionId;
        NId = StateMachineValidation.NId(nId, "nId");
        Name = StateMachineValidation.Name(name, "name");
        Description = StateMachineValidation.Description(description, "description");
        IsInitial = isInitial;
        IsTerminal = isTerminal;
        Outcome = outcome;
        Color = StateMachineValidation.Color(color);
        Sort = sort;
    }

    internal StateNode CopyFor(Guid definitionId, Guid? id = null) => new(
        id ?? Guid.NewGuid(), definitionId, NId, Name, Description, IsInitial, IsTerminal, Outcome, Color, Sort);

    internal bool SameDefinition(StateNode other) =>
        Id == other.Id && StateMachineDefinitionId == other.StateMachineDefinitionId && NId == other.NId
        && Name == other.Name && Description == other.Description && IsInitial == other.IsInitial
        && IsTerminal == other.IsTerminal && Outcome == other.Outcome && Color == other.Color && Sort == other.Sort;

    public static StateNode Restore(
        Guid id,
        Guid definitionId,
        string nId,
        string name,
        string? description,
        bool isInitial,
        bool isTerminal,
        StateOutcome outcome,
        string? color,
        int sort) =>
        new(id, definitionId, nId, name, description, isInitial, isTerminal, outcome, color, sort);
}

public sealed class StateTransition
{
    public Guid Id { get; }
    public Guid StateMachineDefinitionId { get; }
    public string FromStatusNId { get; }
    public string ActionNId { get; }
    public string ActionName { get; }
    public string ToStatusNId { get; }
    public string? Description { get; }

    public StateTransition(
        string fromStatusNId,
        string actionNId,
        string actionName,
        string toStatusNId,
        string? description)
        : this(Guid.NewGuid(), Guid.Empty, fromStatusNId, actionNId, actionName, toStatusNId, description) { }

    private StateTransition(
        Guid id,
        Guid definitionId,
        string fromStatusNId,
        string actionNId,
        string actionName,
        string toStatusNId,
        string? description)
    {
        Id = id;
        StateMachineDefinitionId = definitionId;
        FromStatusNId = StateMachineValidation.NId(fromStatusNId, "fromStatusNId");
        ActionNId = StateMachineValidation.NId(actionNId, "actionNId");
        ActionName = StateMachineValidation.Name(actionName, "actionName");
        ToStatusNId = StateMachineValidation.NId(toStatusNId, "toStatusNId");
        Description = StateMachineValidation.Description(description, "description");
    }

    internal StateTransition CopyFor(Guid definitionId, Guid? id = null) => new(
        id ?? Guid.NewGuid(), definitionId, FromStatusNId, ActionNId, ActionName, ToStatusNId, Description);

    internal bool SameDefinition(StateTransition other) =>
        Id == other.Id && StateMachineDefinitionId == other.StateMachineDefinitionId
        && FromStatusNId == other.FromStatusNId && ActionNId == other.ActionNId
        && ActionName == other.ActionName && ToStatusNId == other.ToStatusNId
        && Description == other.Description;

    public static StateTransition Restore(
        Guid id,
        Guid definitionId,
        string fromStatusNId,
        string actionNId,
        string actionName,
        string toStatusNId,
        string? description) =>
        new(id, definitionId, fromStatusNId, actionNId, actionName, toStatusNId, description);
}

public sealed record TransitionEvaluation(bool AllowedByDefinition, string? ToStatusNId, string? ReasonCode);

internal static class StateMachineValidation
{
    public static string NId(string value, string field)
    {
        try { return ReferenceValidation.NId(value, item: true); }
        catch (ReferenceDataException) { Invalid(field); return string.Empty; }
    }

    public static string Name(string value, string field)
    {
        try { return ReferenceValidation.Name(value); }
        catch (ReferenceDataException) { Invalid(field); return string.Empty; }
    }

    public static string? Description(string? value, string field)
    {
        try { return ReferenceValidation.Description(value); }
        catch (ReferenceDataException) { Invalid(field); return null; }
    }

    public static string? Color(string? value)
    {
        if (value is null) return null;
        if (value.Length != 7 || value[0] != '#' || !value.Skip(1).All(Uri.IsHexDigit)) Invalid("color");
        return value.ToUpperInvariant();
    }

    [DoesNotReturn]
    public static void Invalid(string field) =>
        throw new ReferenceDataException("REF-STATE-MACHINE-INVALID", 422, field);
}
