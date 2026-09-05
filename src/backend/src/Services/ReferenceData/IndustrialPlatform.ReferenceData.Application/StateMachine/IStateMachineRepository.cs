using IndustrialPlatform.ReferenceData.Contracts.StateMachine;
using IndustrialPlatform.ReferenceData.Domain.StateMachine;

namespace IndustrialPlatform.ReferenceData.Application.StateMachine;

public sealed record StateMachineQuery(
    int PageIndex = 1,
    int PageSize = 20,
    string? Keyword = null,
    string? Status = null,
    string? ScopeType = null,
    string? SortField = null,
    bool Descending = false);

public sealed record AvailableStateMachineQuery(
    int PageIndex = 1,
    int PageSize = 20,
    string? Keyword = null);

public sealed record StateMachineChange(
    StateMachineDefinition Definition,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

public interface IStateMachineRepository
{
    Task<(IReadOnlyList<StateMachineSummaryDto> Items, long Total)> SearchAsync(
        string tenantNId, StateMachineQuery query, CancellationToken cancellationToken);
    Task<(IReadOnlyList<AvailableStateMachineDto> Items, long Total)> ListAvailableAsync(
        string tenantNId, AvailableStateMachineQuery query, CancellationToken cancellationToken);
    Task<StateMachineDefinition?> GetAsync(
        string tenantNId, Guid id, CancellationToken cancellationToken);
    Task<StateMachineDefinition?> GetCurrentAsync(
        string tenantNId, string nId, string sourceScope, CancellationToken cancellationToken);
    Task<StateMachineDefinition?> GetRevisionAsync(
        string tenantNId, string nId, string sourceScope, int revision, CancellationToken cancellationToken);
    Task<StateMachineDefinition?> GetPublishedInScopeAsync(
        StateMachineDefinition definition, CancellationToken cancellationToken);
    Task<StateMachineDefinition?> GetLastPublishedAsync(
        StateMachineDefinition definition, CancellationToken cancellationToken);
    Task<int> GetNextRevisionAsync(
        StateMachineDefinition definition, CancellationToken cancellationToken);
    Task CreateAsync(
        StateMachineDefinition definition, StateMachineChange? source, CancellationToken cancellationToken);
    Task SaveAsync(
        IReadOnlyList<StateMachineChange> changes, CancellationToken cancellationToken);
}
