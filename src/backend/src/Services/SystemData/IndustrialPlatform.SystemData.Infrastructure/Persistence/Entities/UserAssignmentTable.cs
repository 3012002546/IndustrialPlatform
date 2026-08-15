using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_user_assignment 表的持久化模型(9 公共列 + 业务列,snake_case)。
/// 时间化多任职:左闭右开区间 [effective_from, effective_to),effective_to 可空表示开放;
/// position_id/position_is_deleted 为岗位复合外键快照;organization_n_id 与岗位归属一致(不建外键)。
/// 用户级并发由应用层按 (tenant_n_id, user_n_id) advisory lock 串行化。
/// </summary>
[SugarTable("system_data_user_assignment")]
public sealed class UserAssignmentTable
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

    [SugarColumn(ColumnName = "n_id")]
    public string NId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "normalized_n_id")]
    public string NormalizedNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "user_n_id")]
    public string UserNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "user_display_name_snapshot")]
    public string UserDisplayNameSnapshot { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "organization_n_id")]
    public string OrganizationNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "position_n_id")]
    public string PositionNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "position_id")]
    public Guid PositionId { get; set; }

    [SugarColumn(ColumnName = "position_is_deleted")]
    public bool PositionIsDeleted { get; set; }

    [SugarColumn(ColumnName = "is_primary")]
    public bool IsPrimary { get; set; }

    [SugarColumn(ColumnName = "effective_from")]
    public DateTimeOffset EffectiveFrom { get; set; }

    [SugarColumn(ColumnName = "effective_to")]
    public DateTimeOffset? EffectiveTo { get; set; }

    [SugarColumn(ColumnName = "state")]
    public int State { get; set; }

    [SugarColumn(ColumnName = "cancelled_on")]
    public DateTimeOffset? CancelledOn { get; set; }

    [SugarColumn(ColumnName = "cancel_reason")]
    public string? CancelReason { get; set; }
}
