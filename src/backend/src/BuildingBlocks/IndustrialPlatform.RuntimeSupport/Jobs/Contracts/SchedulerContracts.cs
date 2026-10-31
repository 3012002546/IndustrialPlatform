using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndustrialPlatform.SystemData.Contracts.Scheduler;

/// <summary>任务的可编辑配置；参数必须通过所选处理器的明确校验。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record SchedulerJobValues
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public JsonElement Parameters { get; init; }
    public string CronExpression { get; init; } = "*/15 * * * *";
    public string TimeZoneId { get; init; } = "Etc/UTC";
    public int TimeoutSeconds { get; init; } = 300;
    public int MaxAttempts { get; init; } = 1;
}

/// <summary>创建任务；租户和重试安全性由服务端确定。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedulerJobCreate : SchedulerJobValues
{
    public string NId { get; init; } = string.Empty;
    public string HandlerKey { get; init; } = string.Empty;
}

/// <summary>更新任务及双并发版本。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedulerJobUpdate : SchedulerJobValues
{
    public string OptimisticVersion { get; init; } = string.Empty;
    public Guid ConcurrencyVersion { get; init; }
}

/// <summary>任务配置动作的双版本。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedulerVersionCommand(string OptimisticVersion, Guid ConcurrencyVersion);

/// <summary>运行操作版本，不能提交内部执行令牌。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record SchedulerRunVersionCommand(string StateVersion);

/// <summary>未知结果核对；没有强制解锁动作。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedulerRunResolution(string StateVersion, string Decision, string Reason, string EvidenceReference)
    : SchedulerRunVersionCommand(StateVersion);

/// <summary>预览唯一周期计划，不创建运行。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SchedulerScheduleInput(string CronExpression, string TimeZoneId);

/// <summary>部署代码注册的处理器目录。</summary>
public sealed record SchedulerHandlerDto(string HandlerKey, string Name, string? Description,
    string RetrySafety, bool SupportsCancellation, int MaxTimeoutSeconds, bool ParametersEditable);

/// <summary>任务详情；原生计划查询失败时下一次时间可空。</summary>
public sealed record SchedulerJobDto
{
    public string JobKind { get; init; } = "Recurring";
    public string? OwnerService { get; init; }
    public string? OwnerObjectNId { get; init; }
    public string NId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string HandlerKey { get; init; } = string.Empty;
    public JsonElement Parameters { get; init; }
    public string Status { get; init; } = "Disabled";
    public string CronExpression { get; init; } = string.Empty;
    public string TimeZoneId { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; }
    public int MaxAttempts { get; init; }
    public string RetrySafety { get; init; } = string.Empty;
    public bool IsSystemOwned { get; init; }
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset LastUpdatedOn { get; init; }
    public string OptimisticVersion { get; init; } = "0";
    public Guid ConcurrencyVersion { get; init; }
    public string SyncState { get; init; } = "Pending";
    public string? SyncErrorCode { get; init; }
    public DateTimeOffset? NextFireOn { get; init; }
}

/// <summary>分页；总数使用十进制字符串。</summary>
public sealed record SchedulerPage<T>(IReadOnlyList<T> Items, string TotalCount, int Page, int PageSize);
public sealed record SchedulerOccurrence(DateTimeOffset Utc, string ZonedTime);
public sealed record SchedulerPreview(string TimeZoneId, DateTimeOffset EvaluatedOn, IReadOnlyList<SchedulerOccurrence> Occurrences);
public sealed record SchedulerRunAccepted(string RunNId, string Status, DateTimeOffset AcceptedOn);
public sealed record SchedulerRunActions(bool CanCancel, bool CanRetry, bool CanResolve);

/// <summary>业务结果摘要；不回传引擎原始参数或异常堆栈。</summary>
public sealed record ScheduledTaskResult(string Outcome, string ResultCode, string? Message = null,
    string? AffectedCount = null, bool Retryable = false)
{
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>一次实际处理器调用的可信上下文。</summary>
public sealed record TaskExecutionContext(string TenantNId, string JobNId, string RunNId,
    string OperationKey, JsonElement Parameters, DateTimeOffset DeadlineOn, string TraceId);

/// <summary>后续业务Job实现并通过部署代码注册的稳定接口。</summary>
public interface IScheduledTaskHandler
{
    SchedulerHandlerDto Definition { get; }
    /// <summary>返回规范化、允许持久化的参数；非法字段须拒绝。</summary>
    JsonElement ValidateParameters(JsonElement parameters);
    Task<ScheduledTaskResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>运行事实及受控动作能力。</summary>
public sealed record SchedulerRunDto
{
    public string NId { get; init; } = string.Empty;
    public string JobNId { get; init; } = string.Empty;
    public string JobName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? OriginalRunNId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset AcceptedOn { get; init; }
    public DateTimeOffset? StartedOn { get; init; }
    public DateTimeOffset? DeadlineOn { get; init; }
    public DateTimeOffset? CompletedOn { get; init; }
    public DateTimeOffset? CancelRequestedOn { get; init; }
    public int AttemptsUsed { get; init; }
    public int MaxAttempts { get; init; }
    public string RetrySafety { get; init; } = string.Empty;
    public string? ResultCode { get; init; }
    public ScheduledTaskResult? ResultSummary { get; init; }
    public string TraceId { get; init; } = string.Empty;
    public string StateVersion { get; init; } = "0";
    public SchedulerRunActions Actions { get; init; } = new(false, false, false);
}

public sealed record SchedulerAttemptDto(int Number, string Status, DateTimeOffset? StartedOn,
    DateTimeOffset? CompletedOn, string? ResultCode, string? ErrorSummary, string? TraceId);
public sealed record SchedulerAttemptHistory(string Availability, IReadOnlyList<SchedulerAttemptDto> Items, string? ReasonCode = null);
public sealed record SchedulerResolutionDto(string Decision, string Reason, string EvidenceReference,
    string ActorUserNId, DateTimeOffset OccurredOn, string TraceId);
public sealed record SchedulerRunDetail(SchedulerRunDto Run, string HandlerKey, JsonElement Parameters,
    string OperationKey, SchedulerAttemptHistory AttemptHistory, SchedulerResolutionDto? Resolution);
