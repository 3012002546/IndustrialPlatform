namespace IndustrialPlatform.SystemData.Domain.Scheduler;

/// <summary>一次逻辑运行的不可变参数快照与最小执行保护，不套配置软删字段。</summary>
public sealed record SchedulerRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string NId { get; init; } = Guid.NewGuid().ToString("N");
    public string TenantNId { get; init; } = string.Empty;
    public DateTimeOffset CreatedOn { get; init; }
    public Guid JobId { get; init; }
    public string Source { get; init; } = "Manual";
    public string InvocationKey { get; init; } = string.Empty;
    public string? RequestHash { get; init; }
    public string? OriginalRunNId { get; init; }
    public string OperationKey { get; init; } = string.Empty;
    public string? RequestedByUserNId { get; init; }
    public DateTimeOffset AcceptedOn { get; init; }
    public string Status { get; init; } = "Queued";
    public string JobNameSnapshot { get; init; } = string.Empty;
    public string HandlerKey { get; init; } = string.Empty;
    public string ParametersJson { get; init; } = "{}";
    public int TimeoutSeconds { get; init; }
    public int MaxAttempts { get; init; }
    public string RetrySafety { get; init; } = "NoAutomaticRecovery";
    public Guid? ActiveJobId { get; init; }
    public Guid? ExecutionToken { get; init; }
    public string? WorkerReference { get; init; }
    public int AttemptsUsed { get; init; }
    public DateTimeOffset? StartedOn { get; init; }
    public DateTimeOffset? DeadlineOn { get; init; }
    public DateTimeOffset? CompletedOn { get; init; }
    public DateTimeOffset? CancelRequestedOn { get; init; }
    public string? ResultCode { get; init; }
    public string? ResultSummaryJson { get; init; }
    public string? EngineReference { get; init; }
    public string TraceId { get; init; } = string.Empty;
    public long StateVersion { get; init; }

    public bool IsTerminal => Status is "Succeeded" or "Failed" or "Cancelled" or "TimedOut";
}
