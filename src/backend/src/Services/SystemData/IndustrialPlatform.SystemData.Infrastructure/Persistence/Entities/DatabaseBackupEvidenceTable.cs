using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_database_backup_evidence 表的持久化模型(9 公共列 + 业务列,snake_case)。
/// 只存备份引用与时间,绝不保存备份访问 Secret/凭据。
/// </summary>
[SugarTable("system_data_database_backup_evidence")]
public sealed class DatabaseBackupEvidenceTable
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

    [SugarColumn(ColumnName = "evidence_n_id")]
    public string EvidenceNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "plan_n_id")]
    public string PlanNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "plan_checksum")]
    public string PlanChecksum { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "target_state_fingerprint")]
    public string TargetStateFingerprint { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "backup_provider")]
    public string BackupProvider { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "backup_reference")]
    public string BackupReference { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "captured_on")]
    public DateTimeOffset CapturedOn { get; set; }

    [SugarColumn(ColumnName = "verified_on")]
    public DateTimeOffset? VerifiedOn { get; set; }

    [SugarColumn(ColumnName = "retention_until")]
    public DateTimeOffset RetentionUntil { get; set; }

    [SugarColumn(ColumnName = "verified_by_user_n_id")]
    public string? VerifiedByUserNId { get; set; }

    [SugarColumn(ColumnName = "status")]
    public BackupEvidenceStatus Status { get; set; }
}
