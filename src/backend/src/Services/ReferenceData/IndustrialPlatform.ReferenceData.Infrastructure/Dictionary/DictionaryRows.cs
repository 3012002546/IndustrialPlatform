using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;

internal sealed class DictionaryRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public Guid Id { get; set; }
    [SugarColumn(ColumnName = "n_id")]
    public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "description")]
    public string? Description { get; set; }
    [SugarColumn(ColumnName = "tenant_nid")]
    public string? TenantNId { get; set; }
    [SugarColumn(ColumnName = "scope_type")]
    public string ScopeType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "revision")]
    public int Revision { get; set; }
    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "published_on")]
    public DateTimeOffset? PublishedOn { get; set; }
    [SugarColumn(ColumnName = "published_by")]
    public string? PublishedBy { get; set; }
    [SugarColumn(ColumnName = "is_frozen")]
    public bool IsFrozen { get; set; }
    [SugarColumn(ColumnName = "is_locked")]
    public bool IsLocked { get; set; }
    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "entity_type")]
    public string EntityType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")]
    public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")]
    public DateTimeOffset LastUpdatedOn { get; set; }
    [SugarColumn(ColumnName = "optimistic_version")]
    public long OptimisticVersion { get; set; }
    [SugarColumn(ColumnName = "concurrency_version")]
    public Guid ConcurrencyVersion { get; set; }
}
internal sealed class DictionaryItemRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public Guid Id { get; set; }
    [SugarColumn(ColumnName = "dictionary_definition_id")]
    public Guid DictionaryDefinitionId { get; set; }
    [SugarColumn(ColumnName = "n_id")]
    public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "description")]
    public string? Description { get; set; }
    [SugarColumn(ColumnName = "sort")]
    public int Sort { get; set; }
    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; }
    [SugarColumn(ColumnName = "is_frozen")]
    public bool IsFrozen { get; set; }
    [SugarColumn(ColumnName = "is_locked")]
    public bool IsLocked { get; set; }
    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "entity_type")]
    public string EntityType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")]
    public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")]
    public DateTimeOffset LastUpdatedOn { get; set; }
    [SugarColumn(ColumnName = "optimistic_version")]
    public long OptimisticVersion { get; set; }
    [SugarColumn(ColumnName = "concurrency_version")]
    public Guid ConcurrencyVersion { get; set; }
}
