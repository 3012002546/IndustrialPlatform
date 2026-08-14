using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_seed_observation 表的持久化模型(9 公共列 + 业务列,snake_case)。
/// 最近一次种子观察,只追加,由 <see cref="DatabaseSeedObservation"/> 聚合维护。
/// 仅脱敏观察(版本/校验和/状态),不含种子内容、Secret、SQL 或凭据。
/// </summary>
[SugarTable("system_data_seed_observation")]
public sealed class DatabaseSeedObservationTable
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

    [SugarColumn(ColumnName = "seed_key")]
    public string SeedKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "seed_version")]
    public string SeedVersion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "checksum")]
    public string Checksum { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "scope")]
    public SeedScope Scope { get; set; }

    [SugarColumn(ColumnName = "status")]
    public SeedStatus Status { get; set; }

    [SugarColumn(ColumnName = "applied_on")]
    public DateTimeOffset? AppliedOn { get; set; }

    [SugarColumn(ColumnName = "operation_n_id")]
    public string? OperationNId { get; set; }

    [SugarColumn(ColumnName = "verification_status")]
    public VerificationStatus VerificationStatus { get; set; }
}
