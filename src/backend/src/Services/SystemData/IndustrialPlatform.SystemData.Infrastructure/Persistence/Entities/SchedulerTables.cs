using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>一项任务及唯一周期配置；通用生命周期来自现有实体映射。</summary>
[SugarTable("scheduler_job")]
public sealed class SchedulerJobTable : ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "n_id")] public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "description", IsNullable = true)] public string? Description { get; set; }
    [SugarColumn(ColumnName = "handler_key")] public string HandlerKey { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "parameters_json")] public string ParametersJson { get; set; } = "{}";
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = "Disabled";
    [SugarColumn(ColumnName = "cron_expression")] public string CronExpression { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "time_zone_id")] public string TimeZoneId { get; set; } = "Etc/UTC";
    [SugarColumn(ColumnName = "timeout_seconds")] public int TimeoutSeconds { get; set; }
    [SugarColumn(ColumnName = "max_attempts")] public int MaxAttempts { get; set; }
    [SugarColumn(ColumnName = "retry_safety")] public string RetrySafety { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "is_system_owned")] public bool IsSystemOwned { get; set; }
}

/// <summary>一次逻辑执行的快照和活动占用；状态版本拒绝并发命令和迟到回包。</summary>
[SugarTable("scheduler_run")]
public sealed class SchedulerRunTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "n_id")] public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "job_id")] public Guid JobId { get; set; }
    [SugarColumn(ColumnName = "source")] public string Source { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "invocation_key")] public string InvocationKey { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "request_hash", IsNullable = true)] public string? RequestHash { get; set; }
    [SugarColumn(ColumnName = "original_run_n_id", IsNullable = true)] public string? OriginalRunNId { get; set; }
    [SugarColumn(ColumnName = "operation_key")] public string OperationKey { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "requested_by_user_n_id", IsNullable = true)] public string? RequestedByUserNId { get; set; }
    [SugarColumn(ColumnName = "accepted_on")] public DateTimeOffset AcceptedOn { get; set; }
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "job_name_snapshot")] public string JobNameSnapshot { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "handler_key")] public string HandlerKey { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "parameters_json")] public string ParametersJson { get; set; } = "{}";
    [SugarColumn(ColumnName = "timeout_seconds")] public int TimeoutSeconds { get; set; }
    [SugarColumn(ColumnName = "max_attempts")] public int MaxAttempts { get; set; }
    [SugarColumn(ColumnName = "retry_safety")] public string RetrySafety { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "active_job_id", IsNullable = true)] public Guid? ActiveJobId { get; set; }
    [SugarColumn(ColumnName = "execution_token", IsNullable = true)] public Guid? ExecutionToken { get; set; }
    [SugarColumn(ColumnName = "worker_reference", IsNullable = true)] public string? WorkerReference { get; set; }
    [SugarColumn(ColumnName = "attempts_used")] public int AttemptsUsed { get; set; }
    [SugarColumn(ColumnName = "started_on", IsNullable = true)] public DateTimeOffset? StartedOn { get; set; }
    [SugarColumn(ColumnName = "deadline_on", IsNullable = true)] public DateTimeOffset? DeadlineOn { get; set; }
    [SugarColumn(ColumnName = "completed_on", IsNullable = true)] public DateTimeOffset? CompletedOn { get; set; }
    [SugarColumn(ColumnName = "cancel_requested_on", IsNullable = true)] public DateTimeOffset? CancelRequestedOn { get; set; }
    [SugarColumn(ColumnName = "result_code", IsNullable = true)] public string? ResultCode { get; set; }
    [SugarColumn(ColumnName = "result_summary_json", IsNullable = true)] public string? ResultSummaryJson { get; set; }
    [SugarColumn(ColumnName = "engine_reference", IsNullable = true)] public string? EngineReference { get; set; }
    [SugarColumn(ColumnName = "trace_id")] public string TraceId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "state_version")] public long StateVersion { get; set; }
}
