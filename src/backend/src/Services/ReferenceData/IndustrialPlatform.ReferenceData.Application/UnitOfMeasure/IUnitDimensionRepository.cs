using IndustrialPlatform.ReferenceData.Contracts.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure;

namespace IndustrialPlatform.ReferenceData.Application.UnitOfMeasure;

public sealed record UnitDimensionQuery(
    int PageIndex = 1,
    int PageSize = 20,
    string? Keyword = null,
    string? Status = null,
    string? ScopeType = null,
    string? SortField = null,
    bool Descending = false);

public sealed record AvailableUnitDimensionQuery(int PageIndex = 1, int PageSize = 20, string? Keyword = null);

public sealed record UnitDimensionChange(
    UnitDimension Dimension,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

public interface IUnitDimensionRepository
{
    Task<(IReadOnlyList<UnitDimensionSummaryDto> Items, long Total)> SearchAsync(
        string tenantNId, UnitDimensionQuery query, CancellationToken cancellationToken);
    Task<(IReadOnlyList<AvailableUnitDimensionDto> Items, long Total)> ListAvailableAsync(
        string tenantNId, AvailableUnitDimensionQuery query, CancellationToken cancellationToken);
    Task<UnitDimension?> GetAsync(string tenantNId, Guid id, CancellationToken cancellationToken);
    Task<UnitDimension?> GetCurrentAsync(
        string tenantNId, string nId, string sourceScope, CancellationToken cancellationToken);
    Task<UnitDimension?> GetRevisionAsync(
        string tenantNId, string nId, string sourceScope, int revision, CancellationToken cancellationToken);
    Task<UnitDimension?> GetPublishedInScopeAsync(UnitDimension dimension, CancellationToken cancellationToken);
    Task<int> GetNextRevisionAsync(UnitDimension dimension, CancellationToken cancellationToken);
    Task CreateAsync(
        UnitDimension dimension, UnitDimensionChange? source, CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<UnitDimensionChange> changes, CancellationToken cancellationToken);
}
