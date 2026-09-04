using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_database_migration_observation 表的持久化模型(9 公共列 + 业务列,snake_case)。
/// 最近一次迁移观察,只追加,由 <see cref="DatabaseMigrationObservation"/> 聚合维护。
/// </summary>
[SugarTable("system_data_database_migration_observation")]
public sealed class DatabaseMigrationObservationTable
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

    [SugarColumn(ColumnName = "service_key")]
    public string ServiceKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "module_key")]
    public string? ModuleKey { get; set; }

    [SugarColumn(ColumnName = "database_identity_fingerprint")]
    public string DatabaseIdentityFingerprint { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "observed_version")]
    public string ObservedVersion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "artifact_checksum")]
    public string ArtifactChecksum { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "observed_on")]
    public DateTimeOffset ObservedOn { get; set; }

    [SugarColumn(ColumnName = "operation_n_id")]
    public string? OperationNId { get; set; }

    [SugarColumn(ColumnName = "verification_status")]
    public VerificationStatus VerificationStatus { get; set; }
}
