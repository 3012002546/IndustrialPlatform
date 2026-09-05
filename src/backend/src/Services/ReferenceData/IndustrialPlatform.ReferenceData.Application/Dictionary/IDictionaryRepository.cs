using IndustrialPlatform.ReferenceData.Domain.Dictionary;
using IndustrialPlatform.ReferenceData.Contracts.Dictionary;

namespace IndustrialPlatform.ReferenceData.Application.Dictionary;

public sealed record DictionaryQuery(int PageIndex = 1, int PageSize = 20, string? Keyword = null, string? Status = null, string? ScopeType = null, string? SortField = null, bool Descending = false);
public sealed record DictionaryChange(DictionaryDefinition Definition, long ExpectedOptimisticVersion, Guid ExpectedConcurrencyVersion);
public interface IDictionaryRepository
{
    Task<(IReadOnlyList<DictionarySummaryDto> Items, long Total)> SearchAsync(string tenantNId, DictionaryQuery query, CancellationToken cancellationToken);
    Task<DictionaryDefinition?> GetAsync(string tenantNId, Guid id, CancellationToken cancellationToken);
    Task<DictionaryDefinition?> GetEffectiveAsync(string tenantNId, string nId, CancellationToken cancellationToken);
    Task<DictionaryDefinition?> GetPublishedInScopeAsync(DictionaryDefinition definition, CancellationToken cancellationToken);
    Task<DictionaryDefinition?> GetLastPublishedAsync(DictionaryDefinition definition, CancellationToken cancellationToken);
    Task<int> GetNextRevisionAsync(DictionaryDefinition definition, CancellationToken cancellationToken);
    Task CreateAsync(DictionaryDefinition definition, DictionaryChange? source, CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<DictionaryChange> changes, CancellationToken cancellationToken);
}
