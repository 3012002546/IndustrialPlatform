using IndustrialPlatform.ReferenceData.Contracts.DynamicProperty;
using IndustrialPlatform.ReferenceData.Domain.DynamicProperty;

namespace IndustrialPlatform.ReferenceData.Application.DynamicProperty;

public sealed record DynamicConfigurationQuery(int PageIndex = 1, int PageSize = 20, string? Keyword = null,
    string? Status = null, string? ScopeType = null, string? SortField = null, bool Descending = false);
public sealed record DynamicRecordQuery(int PageIndex = 1, int PageSize = 20, string? Keyword = null,
    string? NId = null, string? Category = null);
public sealed record DynamicConfigurationChange(DynamicConfigDefinition Definition, long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion, bool SaveFields = false, Guid? RecordId = null);
public interface IDynamicConfigurationRepository
{
    Task<(IReadOnlyList<DynamicConfigurationSummaryDto> Items, long Total)> SearchAsync(string tenantNId, DynamicConfigurationQuery query, CancellationToken cancellationToken);
    Task<DynamicConfigDefinition?> GetAsync(string tenantNId, Guid id, bool includeRecords, CancellationToken cancellationToken);
    Task<(int Records, long Values)> CountAsync(Guid definitionId, CancellationToken cancellationToken);
    Task<DynamicConfigDefinition?> GetEffectiveAsync(string tenantNId, string nId, CancellationToken cancellationToken);
    Task<DynamicConfigDefinition?> GetSnapshotAsync(string tenantNId, string nId, string sourceScope, int revision, CancellationToken cancellationToken);
    Task<DynamicConfigDefinition?> GetPublishedInScopeAsync(DynamicConfigDefinition definition, CancellationToken cancellationToken);
    Task<DynamicConfigDefinition?> GetLastPublishedAsync(DynamicConfigDefinition definition, CancellationToken cancellationToken);
    Task<int> GetNextRevisionAsync(DynamicConfigDefinition definition, CancellationToken cancellationToken);
    Task<(IReadOnlyList<DynamicConfigRecord> Items, long Total)> SearchRecordsAsync(Guid definitionId, DynamicRecordQuery query, bool enabledOnly, CancellationToken cancellationToken);
    Task CreateAsync(DynamicConfigDefinition definition, DynamicConfigurationChange? source, CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<DynamicConfigurationChange> changes, CancellationToken cancellationToken);
}
