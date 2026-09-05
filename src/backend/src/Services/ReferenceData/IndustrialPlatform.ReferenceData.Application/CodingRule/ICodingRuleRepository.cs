using IndustrialPlatform.ReferenceData.Contracts.CodingRule;
using IndustrialPlatform.ReferenceData.Domain.CodingRule;

namespace IndustrialPlatform.ReferenceData.Application.CodingRule;

public sealed record CodingRuleQuery(
    int PageIndex = 1,
    int PageSize = 20,
    string? Keyword = null,
    string? Status = null,
    string? ScopeType = null,
    string? SortField = null,
    bool Descending = false);

public sealed record CodingRuleChange(
    CodingRuleDefinition Rule,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

public sealed record CodingRuleGeneration(
    CodingRuleDefinition Rule,
    string TenantNId,
    string? FactoryId,
    string IdempotencyKeyHash,
    string RequestHash,
    string ContextHash,
    DateTimeOffset GeneratedOn,
    DateTimeOffset ExpiresOn);

public sealed record CodingRuleGeneratedValue(
    string Code,
    long Sequence,
    string PeriodKey,
    DateTimeOffset GeneratedOn);

public interface ICodingRuleRepository
{
    Task<(IReadOnlyList<CodingRuleSummaryDto> Items, long Total)> SearchAsync(
        string tenantNId, CodingRuleQuery query, CancellationToken cancellationToken);
    Task<CodingRuleDefinition?> GetAsync(string tenantNId, Guid id, CancellationToken cancellationToken);
    Task<CodingRuleDefinition?> GetRevisionAsync(
        string tenantNId, string nId, string sourceScope, int revision, CancellationToken cancellationToken);
    Task<CodingRuleDefinition?> GetPublishedInScopeAsync(
        CodingRuleDefinition rule, CancellationToken cancellationToken);
    Task<int> GetNextRevisionAsync(CodingRuleDefinition rule, CancellationToken cancellationToken);
    Task CreateAsync(
        CodingRuleDefinition rule, CodingRuleChange? source, CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<CodingRuleChange> changes, CancellationToken cancellationToken);
    Task<CodingRuleGeneratedValue> GenerateAsync(
        CodingRuleGeneration generation, CancellationToken cancellationToken);
}
