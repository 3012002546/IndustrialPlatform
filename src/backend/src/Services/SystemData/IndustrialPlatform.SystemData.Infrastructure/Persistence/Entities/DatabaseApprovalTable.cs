using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_database_approval 表的持久化模型(9 公共列 + 业务列,snake_case)。
/// 只追加审批记录,由 <see cref="DatabaseApproval"/> 聚合维护。
/// </summary>
[SugarTable("system_data_database_approval")]
public sealed class DatabaseApprovalTable
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

    [SugarColumn(ColumnName = "approval_n_id")]
    public string ApprovalNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "plan_n_id")]
    public string PlanNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "plan_checksum")]
    public string PlanChecksum { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "target_state_fingerprint")]
    public string TargetStateFingerprint { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "approved_by_user_n_id")]
    public string ApprovedByUserNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "reason")]
    public string? Reason { get; set; }

    [SugarColumn(ColumnName = "approved_on")]
    public DateTimeOffset ApprovedOn { get; set; }

    [SugarColumn(ColumnName = "expires_on")]
    public DateTimeOffset ExpiresOn { get; set; }

    [SugarColumn(ColumnName = "status")]
    public ApprovalStatus Status { get; set; }
}
