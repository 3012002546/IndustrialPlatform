using IndustrialPlatform.RuntimeSupport.Jobs;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Scheduler;
using IndustrialPlatform.SystemData.Contracts.Scheduler;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
namespace IndustrialPlatform.SystemData.Infrastructure.Scheduler;

/// <summary>平台专属种子与ReconciliationMode门面；执行、补投及Unknown保护复用中立工作器。</summary>
public sealed class SchedulerRuntime(HangfireSchedulerEngine engine, ISchedulerStore store,
    IControlPlaneTenantSource tenants, IServiceProvider services, IOptions<SchedulerOptions> options,
    ILogger<SchedulerRuntime> logger) : SchedulerWorker(engine, store, services, options, logger)
{
    protected override async Task ConfigureAsync(CancellationToken token)
    {
                    foreach (var tenant in await tenants.GetTenantNIdsAsync(token))
                    {
                        var job = await Store.GetJobAsync(tenant, "systemdata.reconcile", token);
                        if (job is null)
                        {
                            job = new(tenant, "systemdata.reconcile", "权限与负责人快照对账", "复用所属模块的一次对账。",
                                "systemdata.reconcile", "{\"schemaVersion\":1}", "*/15 * * * *", "Etc/UTC", 300, 1, "NoAutomaticRecovery", true);
                            if (Options.Value.ReconciliationMode == "Scheduler") job.SetEnabled(true);
                            try { await Store.AddJobAsync(job, token); }
                            catch (SchedulerDuplicateException) { job = await Store.GetJobAsync(tenant, job.NId, token) ?? job; }
                        }
                        foreach (var configured in await Store.ListJobsAsync(tenant, token))
                            if ((await Engine.GetPlanAsync(configured, token)).SyncState != "Applied")
                                await Engine.SynchronizeAsync(configured, token);
                    }

    }
}
/// <summary>首个实际处理器只接受固定明确参数，未证明幂等前禁止重试。</summary>
public sealed class SystemDataReconciliationTask(ISystemDataReconciliation reconciliation, IOptions<SchedulerOptions> options) : IScheduledTaskHandler
{
    public SchedulerHandlerDto Definition { get; } = new("systemdata.reconcile", "权限与负责人快照对账",
        "复用SystemData公开一次执行入口。", "NoAutomaticRecovery", true, 3600, false);
    public JsonElement ValidateParameters(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object || parameters.EnumerateObject().Count() != 1
            || !parameters.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out var version) || version != 1)
            throw SchedulerAdministration.Error("SCHEDULER_PARAMETERS_INVALID", "对账参数必须仅包含schemaVersion=1。", 422);
        using var normalized = JsonDocument.Parse("{\"schemaVersion\":1}"); return normalized.RootElement.Clone();
    }
    public async Task<ScheduledTaskResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        if (options.Value.ReconciliationMode != "Scheduler") return new("Failed", "RECONCILIATION_LEGACY_MODE", "对账仍为Legacy模式，请排空旧模式后显式切换部署配置。");
        await reconciliation.ReconcileTenantAsync(context.TenantNId, cancellationToken);
        return new("Succeeded", "RECONCILED", "权限与负责人快照对账完成。");
    }
}
