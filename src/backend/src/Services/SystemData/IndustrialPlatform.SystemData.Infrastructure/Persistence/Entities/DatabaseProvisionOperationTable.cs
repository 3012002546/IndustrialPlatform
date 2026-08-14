using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_database_operation 表的持久化模型(9 公共列 + 业务列,snake_case)。
/// Operation 状态机由 <see cref="DatabaseProvisionOperation"/> 聚合维护;步骤存于 operation_step 子表。
/// </summary>
[SugarTable("system_data_database_operation")]
public sealed class DatabaseProvisionOperationTable
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

    [SugarColumn(ColumnName = "operation_n_id")]
    public string OperationNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "kind")]
    public OperationKind Kind { get; set; }

    [SugarColumn(ColumnName = "environment_n_id")]
    public string EnvironmentNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "service_key")]
    public string ServiceKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "module_key")]
    public string ModuleKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "plan_n_id")]
    public string? PlanNId { get; set; }

    [SugarColumn(ColumnName = "requested_version")]
    public string RequestedVersion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "idempotency_key")]
    public string IdempotencyKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "request_hash")]
    public string RequestHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "status")]
    public OperationStatus Status { get; set; }

    [SugarColumn(ColumnName = "phase")]
    public OperationPhase Phase { get; set; }

    [SugarColumn(ColumnName = "attempt")]
    public int Attempt { get; set; }

    [SugarColumn(ColumnName = "lease_owner")]
    public string? LeaseOwner { get; set; }

    [SugarColumn(ColumnName = "lease_expires_on")]
    public DateTimeOffset? LeaseExpiresOn { get; set; }

    [SugarColumn(ColumnName = "heartbeat_on")]
    public DateTimeOffset? HeartbeatOn { get; set; }

    [SugarColumn(ColumnName = "queued_on")]
    public DateTimeOffset QueuedOn { get; set; }

    [SugarColumn(ColumnName = "started_on")]
    public DateTimeOffset? StartedOn { get; set; }

    [SugarColumn(ColumnName = "completed_on")]
    public DateTimeOffset? CompletedOn { get; set; }

    [SugarColumn(ColumnName = "timeout_on")]
    public DateTimeOffset TimeoutOn { get; set; }

    [SugarColumn(ColumnName = "sanitized_error_code")]
    public string? SanitizedErrorCode { get; set; }

    [SugarColumn(ColumnName = "sanitized_error_summary")]
    public string? SanitizedErrorSummary { get; set; }

    [SugarColumn(ColumnName = "trace_id")]
    public string TraceId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "created_by_user_n_id")]
    public string CreatedByUserNId { get; set; } = string.Empty;
}
