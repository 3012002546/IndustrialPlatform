using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.RuntimeSupport.Jobs;
using IndustrialPlatform.SystemData.Domain.Scheduler;
using System.Text.Json;
namespace IndustrialPlatform.SystemData.Application.Scheduler;

public sealed partial class SchedulerAdministration : IScheduledTaskSubmission
{
    private static void Trusted(TrustedServiceContext context)
    {
        Key(context.TenantNId, "租户", 128); Key(context.ActorUserNId, "发起人", 128);
        if (context.ServiceKey != "platform-studio") throw Error("SCHEDULER_SERVICE_FORBIDDEN", "服务无权提交此任务。", 403);
    }
    private static string OnceKey(string operation) => "once:" + Hash(operation);
    public async Task<ScheduledTaskStateV1> ResolveOnceAsync(TrustedServiceContext context, string operationKey, ResolveOnceV1 request, CancellationToken token)
    {
        var accepted = await GetByOperationAsync(context, operationKey, token) ?? throw NotFound();
        var run = await store.GetRunAsync(context.TenantNId, accepted.RunNId, token) ?? throw NotFound();
        if (run.Status is "Succeeded" or "Failed")
        {
            using var summary = JsonDocument.Parse(run.ResultSummaryJson ?? "{}");
            if (summary.RootElement.TryGetProperty("resolution", out var resolution) && resolution.GetProperty("decision").GetString() == request.Decision && resolution.GetProperty("evidenceReference").GetString() == request.EvidenceReference)
                return (await GetStatusAsync(context, operationKey, token))!;
            throw Conflict("该任务已由不同依据核对。");
        }
        await ResolveAsync(context.TenantNId, context.ActorUserNId, operationKey, run.NId, new(request.StateVersion, request.Decision, request.Reason, request.EvidenceReference), token);
        return (await GetStatusAsync(context, operationKey, token))!;
    }
    public async Task<ScheduledTaskStateV1?> GetStatusAsync(TrustedServiceContext context, string operationKey, CancellationToken token)
    {
        var accepted = await GetByOperationAsync(context, operationKey, token);
        if (accepted is null) return null;
        var run = await store.GetRunAsync(context.TenantNId, accepted.RunNId, token) ?? throw NotFound();
        return new(run.NId, run.Status, Number(run.StateVersion), run.OperationKey, run.StartedOn, run.CompletedOn, run.ResultCode);
    }
    public async Task<ScheduledTaskStateV1> RequestCancelAsync(TrustedServiceContext context, string operationKey, string expectedStateVersion, CancellationToken token)
    {
        var accepted = await GetByOperationAsync(context, operationKey, token) ?? throw NotFound();
        await CancelAsync(context.TenantNId, context.ActorUserNId, operationKey, accepted.RunNId, new(expectedStateVersion), token);
        return (await GetStatusAsync(context, operationKey, token))!;
    }
    public async Task<ScheduledTaskSubmissionV1?> GetByOperationAsync(TrustedServiceContext context, string operationKey, CancellationToken token)
    {
        Trusted(context); Key(operationKey, "操作标识", 128);
        var run = await store.GetInvocationAsync(context.TenantNId, OnceKey(operationKey), token);
        if (run is null) return null;
        var job = await store.GetJobByIdAsync(context.TenantNId, run.JobId, token);
        if (job?.OwnerService != context.ServiceKey || run.RequestedByUserNId != context.ActorUserNId) throw Error("SCHEDULER_ACTOR_FORBIDDEN", "任务发起人不符。", 403);
        return new(job.NId, run.NId, Number(run.StateVersion));
    }
    public async Task<ScheduledTaskSubmissionV1> EnqueueOnceAsync(TrustedServiceContext context, EnqueueOnceV1 request, CancellationToken token)
    {
        Trusted(context); Key(request.OperationKey, "操作标识", 128);
        request = request with { DeadlineOn = request.DeadlineOn.ToUniversalTime() };
        if (request.RequestedBy != context.ActorUserNId || request.HandlerKey != "studio.report.render") throw Error("SCHEDULER_ACTOR_OR_HANDLER_FORBIDDEN", "发起人或处理器不受信。", 403);
        if (request.Parameters.ValueKind != JsonValueKind.Object || request.Parameters.EnumerateObject().Count() != 1
            || !request.Parameters.TryGetProperty("runNId", out var value) || value.ValueKind != JsonValueKind.String) throw Error("SCHEDULER_PARAMETERS_INVALID", "参数必须仅包含runNId。", 422);
        var owner = value.GetString()!; Key(owner, "报告运行", 128);
        var hash = Hash(JsonSerializer.Serialize(request, Json)); var key = OnceKey(request.OperationKey);
        var existing = await store.GetInvocationAsync(context.TenantNId, key, token);
        if (existing is not null)
        {
            if (existing.RequestHash != hash) throw Conflict("同operation的参数或发起人不同。");
            return (await GetByOperationAsync(context, request.OperationKey, token))!;
        }
        Enabled();
        var seconds = (int)Math.Ceiling((request.DeadlineOn - clock.GetUtcNow()).TotalSeconds);
        if (seconds is < 5 or > 3600) throw Error("SCHEDULER_DEADLINE_INVALID", "期限必须为5至3600秒。", 422);
        var handler = Handler(request.HandlerKey);
        if (seconds > handler.Definition.MaxTimeoutSeconds || handler.Definition.RetrySafety != "NoAutomaticRecovery") throw Error("SCHEDULER_HANDLER_INVALID", "处理器不符合一次性执行合同。", 422);
        var parameters = handler.ValidateParameters(request.Parameters).GetRawText();
        var job = new SchedulerJob(context.TenantNId, "once-" + Hash(owner), "报告一次性生成", "所属报告运行:" + owner,
            request.HandlerKey, parameters, "*/15 * * * *", "Etc/UTC", seconds, 1, "NoAutomaticRecovery", true);
        job.MakeOneShot(context.ServiceKey, owner);
        try
        {
            await transaction.ExecuteAsync(async () =>
            {
                await store.AddJobAsync(job, token);
                await AcceptAsync(job, context.ActorUserNId, request.OperationKey, key, hash, "Manual", null, request.OperationKey, token, request.DeadlineOn);
            }, token);
        }
        catch (SchedulerDuplicateException)
        {
            existing = await store.GetInvocationAsync(context.TenantNId, key, token);
            if (existing is null || existing.RequestHash != hash) throw Conflict("报告运行已关联其他受理。");
        }
        return (await GetByOperationAsync(context, request.OperationKey, token))!;
    }
}
