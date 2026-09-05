using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Parameter;

internal abstract class ParameterEntityRow
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
internal sealed class ParameterDomainRow : ParameterEntityRow
{
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
    public long Revision { get; set; }
    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = string.Empty;
}

internal sealed class ParameterKeyRow : ParameterEntityRow
{
    [SugarColumn(ColumnName = "configuration_app_domain_id")]
    public Guid ConfigurationAppDomainId { get; set; }
    [SugarColumn(ColumnName = "n_id")]
    public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "description")]
    public string? Description { get; set; }
    [SugarColumn(ColumnName = "data_type")]
    public string DataType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "value_mode")]
    public string ValueMode { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "value_json")]
    public string? ValueJson { get; set; }
    [SugarColumn(ColumnName = "default_value_json")]
    public string? DefaultValueJson { get; set; }
    [SugarColumn(ColumnName = "is_mandatory")]
    public bool IsMandatory { get; set; }
    [SugarColumn(ColumnName = "is_read_only")]
    public bool IsReadOnly { get; set; }
    [SugarColumn(ColumnName = "has_had_value")]
    public bool HasHadValue { get; set; }
    [SugarColumn(ColumnName = "dictionary_nid")]
    public string? DictionaryNId { get; set; }
    [SugarColumn(ColumnName = "reference_target")]
    public string? ReferenceTarget { get; set; }
    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "sort")]
    public int Sort { get; set; }
}

internal sealed class ParameterValueRow : ParameterEntityRow
{
    [SugarColumn(ColumnName = "configuration_key_id")]
    public Guid ConfigurationKeyId { get; set; }
    [SugarColumn(ColumnName = "n_id")]
    public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")]
    public string? Name { get; set; }
    [SugarColumn(ColumnName = "value_json")]
    public string ValueJson { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "canonical_value_hash")]
    public string CanonicalValueHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "sort")]
    public int Sort { get; set; }
    [SugarColumn(ColumnName = "is_default")]
    public bool IsDefault { get; set; }
    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; }
}

internal sealed class ParameterHistoryRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public Guid Id { get; set; }
    [SugarColumn(ColumnName = "app_domain_id")]
    public Guid AppDomainId { get; set; }
    [SugarColumn(ColumnName = "key_id")]
    public Guid? KeyId { get; set; }
    [SugarColumn(ColumnName = "object_type")]
    public string ObjectType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "object_id")]
    public Guid ObjectId { get; set; }
    [SugarColumn(ColumnName = "full_nid")]
    public string FullNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "change_type")]
    public string ChangeType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "change_reason")]
    public string ChangeReason { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "before_summary")]
    public string BeforeSummary { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "after_summary")]
    public string AfterSummary { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "revision")]
    public long Revision { get; set; }
    [SugarColumn(ColumnName = "user_nid")]
    public string UserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "trace_id")]
    public string TraceId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")]
    public DateTimeOffset CreatedOn { get; set; }
}
