using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure;

internal sealed class UnitDimensionRow
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
    [SugarColumn(ColumnName = "is_system_defined")] public bool IsSystemDefined { get; set; }
    [SugarColumn(ColumnName = "conversion_kind")] public string ConversionKind { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "base_unit_nid")] public string BaseUnitNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "is_frozen")] public bool IsFrozen { get; set; }
    [SugarColumn(ColumnName = "is_locked")] public bool IsLocked { get; set; }
    [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "entity_type")] public string EntityType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
    [SugarColumn(ColumnName = "optimistic_version")] public long OptimisticVersion { get; set; }
    [SugarColumn(ColumnName = "concurrency_version")] public Guid ConcurrencyVersion { get; set; }
}

internal sealed class UnitDefinitionRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "unit_dimension_id")] public Guid UnitDimensionId { get; set; }
    [SugarColumn(ColumnName = "n_id")] public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "symbol")] public string Symbol { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "factor_to_base")] public decimal FactorToBase { get; set; }
    [SugarColumn(ColumnName = "offset_to_base")] public decimal OffsetToBase { get; set; }
    [SugarColumn(ColumnName = "decimal_places")] public int DecimalPlaces { get; set; }
    [SugarColumn(ColumnName = "rounding_mode")] public string RoundingMode { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "enabled")] public bool Enabled { get; set; }
    [SugarColumn(ColumnName = "sort")] public int Sort { get; set; }
    [SugarColumn(ColumnName = "is_frozen")] public bool IsFrozen { get; set; }
    [SugarColumn(ColumnName = "is_locked")] public bool IsLocked { get; set; }
    [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "entity_type")] public string EntityType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
    [SugarColumn(ColumnName = "optimistic_version")] public long OptimisticVersion { get; set; }
    [SugarColumn(ColumnName = "concurrency_version")] public Guid ConcurrencyVersion { get; set; }
}
