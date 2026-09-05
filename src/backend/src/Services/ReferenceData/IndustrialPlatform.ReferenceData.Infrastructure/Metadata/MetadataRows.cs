using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Metadata;

internal sealed class MetadataSchemaRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_nid")] public string? TenantNId { get; set; }
    [SugarColumn(ColumnName = "scope_type")] public string ScopeType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "n_id")] public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "description")] public string? Description { get; set; }
    [SugarColumn(ColumnName = "revision")] public int Revision { get; set; }
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "source_revision")] public int? SourceRevision { get; set; }
    [SugarColumn(ColumnName = "published_on")] public DateTimeOffset? PublishedOn { get; set; }
    [SugarColumn(ColumnName = "published_by")] public string? PublishedBy { get; set; }
    [SugarColumn(ColumnName = "is_frozen")] public bool IsFrozen { get; set; }
    [SugarColumn(ColumnName = "is_locked")] public bool IsLocked { get; set; }
    [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "entity_type")] public string EntityType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
    [SugarColumn(ColumnName = "optimistic_version")] public long OptimisticVersion { get; set; }
    [SugarColumn(ColumnName = "concurrency_version")] public Guid ConcurrencyVersion { get; set; }
}

internal sealed class MetadataAttributeRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "entity_schema_id")] public Guid EntitySchemaId { get; set; }
    [SugarColumn(ColumnName = "n_id")] public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "data_type")] public string DataType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "required")] public bool Required { get; set; }
    [SugarColumn(ColumnName = "is_array")] public bool IsArray { get; set; }
    [SugarColumn(ColumnName = "enabled")] public bool Enabled { get; set; }
    [SugarColumn(ColumnName = "sort")] public int Sort { get; set; }
    [SugarColumn(ColumnName = "default_value")] public string? DefaultValue { get; set; }
    [SugarColumn(ColumnName = "min_length")] public int? MinLength { get; set; }
    [SugarColumn(ColumnName = "max_length")] public int? MaxLength { get; set; }
    [SugarColumn(ColumnName = "min_value")] public decimal? MinValue { get; set; }
    [SugarColumn(ColumnName = "max_value")] public decimal? MaxValue { get; set; }
    [SugarColumn(ColumnName = "pattern")] public string? Pattern { get; set; }
    [SugarColumn(ColumnName = "dictionary_nid")] public string? DictionaryNId { get; set; }
    [SugarColumn(ColumnName = "reference_target")] public string? ReferenceTarget { get; set; }
    [SugarColumn(ColumnName = "precision_value")] public int? Precision { get; set; }
    [SugarColumn(ColumnName = "scale_value")] public int? Scale { get; set; }
    [SugarColumn(ColumnName = "unit_dimension_nid")] public string? UnitDimensionNId { get; set; }
    [SugarColumn(ColumnName = "default_unit_nid")] public string? DefaultUnitNId { get; set; }
    [SugarColumn(ColumnName = "unit_revision")] public int? UnitRevision { get; set; }
    [SugarColumn(ColumnName = "unit_source_scope")] public string? UnitSourceScope { get; set; }
    [SugarColumn(ColumnName = "unit_source_tenant_nid")] public string? UnitSourceTenantNId { get; set; }
    [SugarColumn(ColumnName = "description")] public string? Description { get; set; }
    [SugarColumn(ColumnName = "was_published")] public bool WasPublished { get; set; }
    [SugarColumn(ColumnName = "is_frozen")] public bool IsFrozen { get; set; }
    [SugarColumn(ColumnName = "is_locked")] public bool IsLocked { get; set; }
    [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "entity_type")] public string EntityType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
    [SugarColumn(ColumnName = "optimistic_version")] public long OptimisticVersion { get; set; }
    [SugarColumn(ColumnName = "concurrency_version")] public Guid ConcurrencyVersion { get; set; }
}
