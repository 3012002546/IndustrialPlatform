using IndustrialPlatform.ReferenceData.Contracts.Metadata;
using IndustrialPlatform.ReferenceData.Domain.Metadata;

namespace IndustrialPlatform.ReferenceData.Application.Metadata;

public sealed record MetadataSchemaQuery(
    int PageIndex = 1,
    int PageSize = 20,
    string? Keyword = null,
    string? Status = null,
    string? ScopeType = null,
    string? SortField = null,
    bool Descending = false);

public sealed record MetadataSchemaChange(
    EntitySchema Schema,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion,
    bool SaveAttributes = false);

public interface IMetadataSchemaRepository
{
    Task<(IReadOnlyList<MetadataSchemaSummaryDto> Items, long Total)> SearchAsync(
        string tenantNId, MetadataSchemaQuery query, CancellationToken cancellationToken);
    Task<EntitySchema?> GetAsync(string tenantNId, Guid id, CancellationToken cancellationToken);
    Task<EntitySchema?> GetEffectiveAsync(string tenantNId, string nId, CancellationToken cancellationToken);
    Task<EntitySchema?> GetRevisionAsync(string tenantNId, string nId, string sourceScope, int revision,
        CancellationToken cancellationToken);
    Task<EntitySchema?> GetPublishedInScopeAsync(EntitySchema schema, CancellationToken cancellationToken);
    Task<EntitySchema?> GetLastPublishedAsync(EntitySchema schema, CancellationToken cancellationToken);
    Task<int> GetNextRevisionAsync(EntitySchema schema, CancellationToken cancellationToken);
    Task CreateAsync(EntitySchema schema, MetadataSchemaChange? source, CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<MetadataSchemaChange> changes, CancellationToken cancellationToken);
}
