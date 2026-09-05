using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.DynamicProperty;

internal abstract class DynamicEntityRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public Guid Id { get; set; }
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

internal sealed class DynamicDefinitionRow : DynamicEntityRow
{
    [SugarColumn(ColumnName = "n_id")]
    public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "description")]
    public string? Description { get; set; }
    [SugarColumn(ColumnName = "scope_type")]
    public string ScopeType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "tenant_nid")]
    public string? TenantNId { get; set; }
    [SugarColumn(ColumnName = "revision")]
    public int Revision { get; set; }
    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "published_on")]
    public DateTimeOffset? PublishedOn { get; set; }
    [SugarColumn(ColumnName = "published_by")]
    public string? PublishedBy { get; set; }
}

internal sealed class DynamicFieldRow : DynamicEntityRow
{
    [SugarColumn(ColumnName = "definition_id")]
    public Guid DefinitionId { get; set; }
    [SugarColumn(ColumnName = "n_id")]
    public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "data_type")]
    public string DataType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "required")]
    public bool Required { get; set; }
    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; }
    [SugarColumn(ColumnName = "sort")]
    public int Sort { get; set; }
    [SugarColumn(ColumnName = "default_value_json")]
    public string? DefaultValueJson { get; set; }
    [SugarColumn(ColumnName = "min_length")]
    public int? MinLength { get; set; }
    [SugarColumn(ColumnName = "max_length")]
    public int? MaxLength { get; set; }
    [SugarColumn(ColumnName = "min_value")]
    public decimal? MinValue { get; set; }
    [SugarColumn(ColumnName = "max_value")]
    public decimal? MaxValue { get; set; }
    [SugarColumn(ColumnName = "scale")]
    public int? Scale { get; set; }
    [SugarColumn(ColumnName = "pattern")]
    public string? Pattern { get; set; }
    [SugarColumn(ColumnName = "dictionary_nid")]
    public string? DictionaryNId { get; set; }
    [SugarColumn(ColumnName = "reference_target")]
    public string? ReferenceTarget { get; set; }
    [SugarColumn(ColumnName = "description")]
    public string? Description { get; set; }
    [SugarColumn(ColumnName = "has_had_value")]
    public bool HasHadValue { get; set; }
    [SugarColumn(ColumnName = "was_published")]
    public bool WasPublished { get; set; }
}

internal sealed class DynamicRecordRow : DynamicEntityRow
{
    [SugarColumn(ColumnName = "definition_id")]
    public Guid DefinitionId { get; set; }
    [SugarColumn(ColumnName = "n_id")]
    public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")]
    public string? Name { get; set; }
    [SugarColumn(ColumnName = "category")]
    public string? Category { get; set; }
    [SugarColumn(ColumnName = "sort")]
    public int Sort { get; set; }
    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; }
}

internal sealed class DynamicValueRow : DynamicEntityRow
{
    [SugarColumn(ColumnName = "definition_id")]
    public Guid DefinitionId { get; set; }
    [SugarColumn(ColumnName = "record_id")]
    public Guid RecordId { get; set; }
    [SugarColumn(ColumnName = "field_id")]
    public Guid FieldId { get; set; }
    [SugarColumn(ColumnName = "value_type")]
    public string ValueType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "string_value")]
    public string? StringValue { get; set; }
    [SugarColumn(ColumnName = "integer_value")]
    public long? IntegerValue { get; set; }
    [SugarColumn(ColumnName = "decimal_value")]
    public decimal? DecimalValue { get; set; }
    [SugarColumn(ColumnName = "boolean_value")]
    public bool? BooleanValue { get; set; }
    [SugarColumn(ColumnName = "date_value")]
    public DateOnly? DateValue { get; set; }
    [SugarColumn(ColumnName = "date_time_value")]
    public DateTimeOffset? DateTimeValue { get; set; }
    [SugarColumn(ColumnName = "json_value")]
    public string? JsonValue { get; set; }
    [SugarColumn(ColumnName = "reference_value")]
    public string? ReferenceValue { get; set; }
}
