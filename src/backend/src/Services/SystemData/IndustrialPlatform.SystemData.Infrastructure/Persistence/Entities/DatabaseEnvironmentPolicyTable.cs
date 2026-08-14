using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_database_environment_policy 表的持久化模型(9 公共列 + 业务列,snake_case)。
/// 仅承载表结构映射,不变量由 <see cref="DatabaseEnvironmentPolicy"/> 聚合维护。
/// </summary>
[SugarTable("system_data_database_environment_policy")]
public sealed class DatabaseEnvironmentPolicyTable
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

    [SugarColumn(ColumnName = "environment_n_id")]
    public string EnvironmentNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "environment_kind")]
    public DatabaseEnvironmentKind EnvironmentKind { get; set; }

    [SugarColumn(ColumnName = "approval_required")]
    public bool ApprovalRequired { get; set; }

    [SugarColumn(ColumnName = "backup_required")]
    public bool BackupRequired { get; set; }

    [SugarColumn(ColumnName = "plan_ttl_seconds")]
    public int PlanTtlSeconds { get; set; }

    [SugarColumn(ColumnName = "plan_timeout_seconds")]
    public int PlanTimeoutSeconds { get; set; }

    [SugarColumn(ColumnName = "apply_timeout_seconds")]
    public int ApplyTimeoutSeconds { get; set; }

    [SugarColumn(ColumnName = "max_pre_migration_retries")]
    public int MaxPreMigrationRetries { get; set; }

    [SugarColumn(ColumnName = "policy_revision")]
    public int PolicyRevision { get; set; }
}
