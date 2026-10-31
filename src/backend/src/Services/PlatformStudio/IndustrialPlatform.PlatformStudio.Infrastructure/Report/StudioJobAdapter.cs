using System.Text.Json;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.RuntimeSupport.Jobs;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

/// <summary>两模式消费同一公开受信调度端口；不跨Scheduler仓储、不启动另一套执行循环。</summary>
public sealed class StudioJobAdapter(TimeProvider clock, IScheduledTaskSubmission? submission = null) : IStudioJobs
{
    private IScheduledTaskSubmission Service => submission ?? throw new StudioException("STUDIO_JOBS_UNAVAILABLE", "报告调度适配尚不可用。", 503);
    private static TrustedServiceContext Context(StudioReportRun run) => new(run.TenantNId, "platform-studio", run.RequestedBy);
    private static ReportJobState State(ScheduledTaskStateV1 state) => new(state.RunNId, state.Status, state.StateVersion, state.OperationKey, state.StartedOn, state.CompletedOn, state.ReasonCode);
    public async Task<ReportJobState?> GetByOperationAsync(StudioReportRun run, CancellationToken token)
    {
        var state = await Service.GetStatusAsync(Context(run), run.OperationKey, token);
        return state is null ? null : State(state);
    }
    public async Task<ReportJobState> SubmitOnceAsync(StudioReportRun run, CancellationToken token)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { runNId = run.NId }));
        // 期限从持久受理时间派生，重放不因当前时间变化而改变请求hash。
        var deadline = run.CreatedOn.AddSeconds(300);
        if (deadline <= clock.GetUtcNow()) throw new StudioException("STUDIO_REPORT_DISPATCH_EXPIRED", "报告受理期限已过，先核对原operation，不自动重新生成。", 409);
        await Service.EnqueueOnceAsync(Context(run), new("studio.report.render", run.OperationKey, json.RootElement.Clone(), run.RequestedBy, deadline), token);
        return await GetByOperationAsync(run, token) ?? throw new StudioException("STUDIO_REPORT_DISPATCH_UNCONFIRMED", "调度受理结果尚无法确认，请按原operation核对。", 503);
    }
    public async Task<ReportJobState> RequestCancelAsync(StudioReportRun run, string expectedStateVersion, CancellationToken token) => State(await Service.RequestCancelAsync(Context(run), run.OperationKey, expectedStateVersion, token));
    public async Task<ReportJobState> ResolveAsync(StudioReportRun run, string expectedStateVersion, string decision, string reason, string evidenceReference, CancellationToken token) => State(await Service.ResolveOnceAsync(Context(run), run.OperationKey, new(expectedStateVersion, decision, reason, evidenceReference), token));
}
