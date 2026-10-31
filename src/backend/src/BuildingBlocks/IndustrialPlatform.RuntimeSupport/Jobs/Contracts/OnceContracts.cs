using IndustrialPlatform.RuntimeSupport.Files;
using System.Text.Json;
namespace IndustrialPlatform.RuntimeSupport.Jobs;

/// <summary>仅服务端认证后构建受信上下文；参数固定为所属报告运行标识。</summary>
public sealed record EnqueueOnceV1(string HandlerKey, string OperationKey, JsonElement Parameters, string RequestedBy, DateTimeOffset DeadlineOn);
public sealed record ScheduledTaskSubmissionV1(string JobNId, string RunNId, string StateVersion);
public sealed record ScheduledTaskStateV1(string RunNId, string Status, string StateVersion, string OperationKey, DateTimeOffset? StartedOn, DateTimeOffset? CompletedOn, string? ReasonCode);
public sealed record ResolveOnceV1(string StateVersion, string Decision, string Reason, string EvidenceReference);
public interface IScheduledTaskSubmission
{
    Task<ScheduledTaskSubmissionV1> EnqueueOnceAsync(TrustedServiceContext context, EnqueueOnceV1 request, CancellationToken token);
    Task<ScheduledTaskSubmissionV1?> GetByOperationAsync(TrustedServiceContext context, string operationKey, CancellationToken token);
    Task<ScheduledTaskStateV1?> GetStatusAsync(TrustedServiceContext context, string operationKey, CancellationToken token);
    Task<ScheduledTaskStateV1> RequestCancelAsync(TrustedServiceContext context, string operationKey, string expectedStateVersion, CancellationToken token);
    Task<ScheduledTaskStateV1> ResolveOnceAsync(TrustedServiceContext context, string operationKey, ResolveOnceV1 request, CancellationToken token) => Task.FromException<ScheduledTaskStateV1>(new NotSupportedException("Once resolution is unavailable."));
}
