using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_database_registration 表的持久化模型(9 公共列 + 业务列,snake_case)。
/// 逻辑/物理身份与拓扑 revision 快照,由 <see cref="DatabaseRegistration"/> 聚合维护。
/// </summary>
[SugarTable("system_data_database_registration")]
public sealed class DatabaseRegistrationTable
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
    public string ModuleKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "seed_sets")]
    public string? SeedSets { get; set; }

    [SugarColumn(ColumnName = "provider")]
    public string Provider { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "logical_database_name")]
    public string LogicalDatabaseName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "physical_database_name")]
    public string PhysicalDatabaseName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "is_shared_physical_database")]
    public bool IsSharedPhysicalDatabase { get; set; }

    [SugarColumn(ColumnName = "topology_mode")]
    public string TopologyMode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "topology_revision")]
    public string TopologyRevision { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "migration_artifact_id")]
    public string MigrationArtifactId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "migration_version")]
    public string MigrationVersion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "artifact_checksum")]
    public string ArtifactChecksum { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "artifact_signature")]
    public string? ArtifactSignature { get; set; }

    [SugarColumn(ColumnName = "owner_n_id")]
    public string OwnerNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "desired_state")]
    public DesiredState DesiredState { get; set; }

    [SugarColumn(ColumnName = "auto_provision")]
    public bool AutoProvision { get; set; }

    [SugarColumn(ColumnName = "auto_migrate")]
    public bool AutoMigrate { get; set; }

    [SugarColumn(ColumnName = "manifest_version")]
    public string ManifestVersion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "manifest_checksum")]
    public string ManifestChecksum { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "status")]
    public RegistrationStatus Status { get; set; }
}
