using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_database_plan 表的持久化模型(9 公共列 + 业务列,snake_case)。
/// 不可变计划由 <see cref="DatabaseProvisionPlan"/> 聚合维护;步骤存于 plan_step 子表。
/// </summary>
[SugarTable("system_data_database_plan")]
public sealed class DatabaseProvisionPlanTable
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

    [SugarColumn(ColumnName = "plan_n_id")]
    public string PlanNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "environment_n_id")]
    public string EnvironmentNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "service_key")]
    public string ServiceKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "requested_migration_version")]
    public string RequestedMigrationVersion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "current_migration_version")]
    public string CurrentMigrationVersion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "target_state_fingerprint")]
    public string TargetStateFingerprint { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "plan_checksum")]
    public string PlanChecksum { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "risk_level")]
    public RiskLevel RiskLevel { get; set; }

    [SugarColumn(ColumnName = "destructive_change_detected")]
    public bool DestructiveChangeDetected { get; set; }

    [SugarColumn(ColumnName = "required_policies")]
    public DatabasePlanRequiredPolicies RequiredPolicies { get; set; }

    [SugarColumn(ColumnName = "expires_on")]
    public DateTimeOffset ExpiresOn { get; set; }

    [SugarColumn(ColumnName = "created_by_user_n_id")]
    public string CreatedByUserNId { get; set; } = string.Empty;
}
