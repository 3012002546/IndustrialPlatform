using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// identity_operation_audit 表的持久化模型(§6 公共列 + §19.2 业务列,snake_case)。
/// 只追加、不可变,不提供修改与删除业务 API。密码、Token、内部哈希与数据库主键
/// 绝不进入 before/after 摘要。
/// </summary>
[SugarTable("identity_operation_audit")]
public sealed class OperationAuditTable
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

    [SugarColumn(ColumnName = "tenant_n_id")]
    public string TenantNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "actor_user_n_id")]
    public string ActorUserNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "action")]
    public string Action { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "object_type")]
    public string ObjectType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "object_n_id")]
    public string ObjectNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "before_summary")]
    public string? BeforeSummary { get; set; }

    [SugarColumn(ColumnName = "after_summary")]
    public string? AfterSummary { get; set; }

    [SugarColumn(ColumnName = "trace_id")]
    public string TraceId { get; set; } = string.Empty;
}
