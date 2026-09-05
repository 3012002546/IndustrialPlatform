namespace IndustrialPlatform.ReferenceData.Contracts.StateMachine;

public sealed record StateNodeRequest(
    string NId,
    string Name,
    string? Description,
    bool IsInitial,
    bool IsTerminal,
    string Outcome,
    string? Color,
    int Sort);

public sealed record StateTransitionRequest(
    string FromStatusNId,
    string ActionNId,
    string ActionName,
    string ToStatusNId,
    string? Description);

public sealed record StateNodeDto(
    Guid Id,
    string NId,
    string Name,
    string? Description,
    bool IsInitial,
    bool IsTerminal,
    string Outcome,
    string? Color,
    int Sort);

public sealed record StateTransitionDto(
    Guid Id,
    string FromStatusNId,
    string ActionNId,
    string ActionName,
    string ToStatusNId,
    string? Description);

public sealed record CreateStateMachineRequest(
    string ScopeType,
    string NId,
    string Name,
    string? Description,
    IReadOnlyList<StateNodeRequest> Nodes,
    IReadOnlyList<StateTransitionRequest> Transitions);

public sealed record UpdateStateMachineRequest(
    string Name,
    string? Description,
    IReadOnlyList<StateNodeRequest> Nodes,
    IReadOnlyList<StateTransitionRequest> Transitions,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

public sealed record StateMachineSummaryDto(
    Guid Id,
    string NId,
    string Name,
    string? Description,
    string ScopeType,
    string? TenantNId,
    int Revision,
    string Status,
    int? SourceRevision,
    int NodeCount,
    int TransitionCount,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    DateTimeOffset LastUpdatedOn,
    DateTimeOffset? PublishedOn,
    string? PublishedBy,
    bool IsFrozen,
    bool IsLocked);

public sealed record StateMachineDetailDto(
    Guid Id,
    string NId,
    string Name,
    string? Description,
    string ScopeType,
    string? TenantNId,
    int Revision,
    string Status,
    int? SourceRevision,
    DateTimeOffset? PublishedOn,
    string? PublishedBy,
    IReadOnlyList<StateNodeDto> Nodes,
    IReadOnlyList<StateTransitionDto> Transitions,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    DateTimeOffset LastUpdatedOn,
    bool IsFrozen,
    bool IsLocked);

public sealed record StateMachinePublicationIssue(string Code, string Field);

public sealed record StateMachinePublicationCheckDto(
    int? PreviousRevision,
    IReadOnlyList<string> AddedNodeNIds,
    IReadOnlyList<string> RemovedNodeNIds,
    IReadOnlyList<string> ChangedNodeNIds,
    IReadOnlyList<string> AddedTransitionKeys,
    IReadOnlyList<string> RemovedTransitionKeys,
    IReadOnlyList<string> ChangedTransitionKeys,
    IReadOnlyList<StateMachinePublicationIssue> Errors);

public sealed record AvailableStateMachineDto(
    string NId,
    string Name,
    string SourceScope,
    string? SourceTenantNId,
    int Revision,
    DateTimeOffset PublishedOn,
    int NodeCount,
    int TransitionCount);

public sealed record RuntimeStateNodeDto(
    string NId,
    string Name,
    string? Description,
    bool IsInitial,
    bool IsTerminal,
    string Outcome,
    string? Color,
    int Sort);

public sealed record RuntimeStateTransitionDto(
    string FromStatusNId,
    string ActionNId,
    string ActionName,
    string ToStatusNId,
    string? Description);

public sealed record StateMachineDefinitionDto(
    string NId,
    string Name,
    string? Description,
    string SourceScope,
    string? SourceTenantNId,
    int Revision,
    string Status,
    int? SourceRevision,
    DateTimeOffset? PublishedOn,
    IReadOnlyList<RuntimeStateNodeDto> Nodes,
    IReadOnlyList<RuntimeStateTransitionDto> Transitions);

public sealed record TransitionEvaluationRequest(
    string SourceScope,
    string? SourceTenantNId,
    int Revision,
    string FromStatusNId,
    string ActionNId);

public sealed record TransitionEvaluationDto(
    string StateMachineNId,
    int StateMachineRevision,
    string SourceScope,
    string? SourceTenantNId,
    string FromStatusNId,
    string ActionNId,
    bool AllowedByDefinition,
    string? ToStatusNId,
    string? ReasonCode);
