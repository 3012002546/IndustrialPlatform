using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.CodingRule;

internal sealed class CodingRuleDefinitionRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_nid")] public string? TenantNId { get; set; }
    [SugarColumn(ColumnName = "scope_type")] public string ScopeType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "n_id")] public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "target_entity_nid")] public string TargetEntityNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "template")] public string Template { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "reset_policy")] public string ResetPolicy { get; set; } = string.Empty;
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

internal sealed class CodingRuleSequenceRow
{
    [SugarColumn(ColumnName = "coding_rule_id")] public Guid CodingRuleId { get; set; }
    [SugarColumn(ColumnName = "rule_revision")] public int RuleRevision { get; set; }
    [SugarColumn(ColumnName = "period_key")] public string PeriodKey { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "context_hash")] public string ContextHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "last_value")] public long LastValue { get; set; }
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
}

internal sealed class CodingRuleIdempotencyRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_nid")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "rule_nid")] public string RuleNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "idempotency_key_hash")] public string IdempotencyKeyHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "request_hash")] public string RequestHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "coding_rule_id")] public Guid CodingRuleId { get; set; }
    [SugarColumn(ColumnName = "rule_revision")] public int RuleRevision { get; set; }
    [SugarColumn(ColumnName = "source_scope")] public string SourceScope { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "source_tenant_nid")] public string? SourceTenantNId { get; set; }
    [SugarColumn(ColumnName = "code")] public string Code { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "sequence_value")] public long Sequence { get; set; }
    [SugarColumn(ColumnName = "period_key")] public string PeriodKey { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "generated_on")] public DateTimeOffset GeneratedOn { get; set; }
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset ExpiresOn { get; set; }
}
