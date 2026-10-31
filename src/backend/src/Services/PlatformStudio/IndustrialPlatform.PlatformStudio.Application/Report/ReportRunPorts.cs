using IndustrialPlatform.PlatformStudio.Domain;
namespace IndustrialPlatform.PlatformStudio.Application.Report;

/// <summary>报告本库受理及持久CAS端口；调用者必须先冻结、校验并授权定义，不开放给浏览器。</summary>
public interface IReportRunStore
{
    Task<StudioReportRun?> FindAsync(string tenant, string runNId, CancellationToken token);
    Task<StudioReportRun?> FindOperationAsync(string tenant, string operationKey, CancellationToken token);
    Task AcceptAsync(StudioReportRun run, StudioOperationReceipt receipt, CancellationToken token);
    Task<bool> AdvanceAsync(StudioReportRun run, long expectedVersion, long expectedEpoch, CancellationToken token);
    Task<bool> CompleteAsync(StudioReportRun run, IReadOnlyList<StudioReportArtifact> confirmedArtifacts, CancellationToken token) => Task.FromException<bool>(new NotSupportedException("Report output confirmation is unavailable."));
    Task<bool> RecordProgressAsync(StudioReportRun claim, CancellationToken token) => Task.FromException<bool>(new NotSupportedException("Report progress storage is unavailable."));
    Task<bool> ResolveUnknownAsync(StudioReportRun run, IReadOnlyList<StudioReportArtifact> confirmedArtifacts, CancellationToken token) => Task.FromException<bool>(new NotSupportedException("Report resolution is unavailable."));
}

/// <summary>报告只消费公开任务语义；原生与Embedded映射到同一个PF07执行状态机。</summary>
public interface IStudioJobs
{
    Task<ReportJobState?> GetByOperationAsync(StudioReportRun run, CancellationToken token);
    Task<ReportJobState> SubmitOnceAsync(StudioReportRun run, CancellationToken token);
    Task<ReportJobState> RequestCancelAsync(StudioReportRun run, string expectedStateVersion, CancellationToken token);
    Task<ReportJobState> ResolveAsync(StudioReportRun run, string expectedStateVersion, string decision, string reason, string evidenceReference, CancellationToken token) => Task.FromException<ReportJobState>(new NotSupportedException("Report job resolution is unavailable."));
}
public sealed record ReportJobState(string RunNId, string Status, string StateVersion, string OperationKey, DateTimeOffset? StartedOn, DateTimeOffset? CompletedOn, string? ReasonCode);

/// <summary>由后续已授权规划器提交的不可变输入；不是HTTP请求合同，不承诺布局已经准入。</summary>
public sealed record FrozenReportInput(string ExecutionKind, Guid? TemplateRevisionId, string? PreviewSnapshotJson, string ParametersJson, string Format, string TraceId, string SortJson = "[]", Guid? RetryOfRunId = null, string? RetryMode = null);
public sealed record ReportRunState(string RunNId, string ExecutionKind, string Status, string StateVersion, string ExecutionEpoch, DateTimeOffset? ExpiresOn, string? ErrorCode);

/// <summary>只接受状态机允许的转换。过期预览仍需确认停止；Unknown必须经有证据的核对端口处理。</summary>
public static class ReportRunTransitions
{
    public static bool Allows(string before, string after) => before switch
    {
        "PendingDispatch" => after is "Queued" or "Cancelled",
        "Queued" => after is "Running" or "CancelRequested" or "Cancelled" or "Failed" or "Unknown",
        "Running" => after is "CancelRequested" or "Succeeded" or "Failed" or "Cancelled" or "Unknown",
        "CancelRequested" => after is "Succeeded" or "Failed" or "Cancelled" or "Unknown",
        _ => false,
    };
}
