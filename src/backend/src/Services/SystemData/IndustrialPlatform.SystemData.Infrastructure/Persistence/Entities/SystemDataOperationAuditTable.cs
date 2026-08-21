using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

[SugarTable("system_data_operation_audit")]
public sealed class SystemDataOperationAuditTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "is_frozen")] public bool IsFrozen { get; set; }
    [SugarColumn(ColumnName = "is_locked")] public bool IsLocked { get; set; }
    [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "entity_type")] public string EntityType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
    [SugarColumn(ColumnName = "optimistic_version")] public long OptimisticVersion { get; set; }
    [SugarColumn(ColumnName = "concurrency_version")] public Guid ConcurrencyVersion { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "actor_user_n_id")] public string ActorUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "action")] public string Action { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "object_type")] public string ObjectType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "object_n_id")] public string ObjectNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "reason")] public string? Reason { get; set; }
    [SugarColumn(ColumnName = "before_summary")] public string? BeforeSummary { get; set; }
    [SugarColumn(ColumnName = "after_summary")] public string? AfterSummary { get; set; }
    [SugarColumn(ColumnName = "trace_id")] public string TraceId { get; set; } = string.Empty;
}
