using System.Text.Json;
using Hangfire;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Scheduler;
using IndustrialPlatform.SystemData.Contracts.Scheduler;
using IndustrialPlatform.SystemData.Domain.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Infrastructure.Scheduler;

/// <summary>启动原生工作器，映射失联事实并引导唯一真实对账任务；不调度业务重试。</summary>
public sealed partial class SchedulerRuntime(HangfireSchedulerEngine engine, ISchedulerStore store,
    IControlPlaneTenantSource tenants, IServiceProvider services, IOptions<SchedulerOptions> options,
    ILogger<SchedulerRuntime> logger) : BackgroundService, ISchedulerCommandDispatcher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!engine.Enabled) return;
        BackgroundJobServer? server = null;
        var nextCleanup = DateTimeOffset.MinValue;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    server ??= new BackgroundJobServer(new BackgroundJobServerOptions
                    {
                        WorkerCount = Math.Clamp(options.Value.WorkerConcurrency, 1, 32), Activator = new ServiceJobActivator(services),
                        ServerName = "systemdata-scheduler", Queues = ["default"], HeartbeatInterval = TimeSpan.FromSeconds(15),
                        ServerTimeout = TimeSpan.FromSeconds(60), SchedulePollingInterval = TimeSpan.FromSeconds(1),
                    }, engine.Storage);
                    foreach (var tenant in await tenants.GetTenantNIdsAsync(stoppingToken))
                    {
                        var job = await store.GetJobAsync(tenant, "systemdata.reconcile", stoppingToken);
                        if (job is null)
                        {
                            job = new(tenant, "systemdata.reconcile", "权限与负责人快照对账", "复用所属模块的一次对账。",
                                "systemdata.reconcile", "{\"schemaVersion\":1}", "*/15 * * * *", "Etc/UTC", 300, 1, "NoAutomaticRecovery", true);
                            if (options.Value.ReconciliationMode == "Scheduler") job.SetEnabled(true);
                            try { await store.AddJobAsync(job, stoppingToken); }
                            catch (SchedulerDuplicateException) { job = await store.GetJobAsync(tenant, job.NId, stoppingToken) ?? job; }
                        }
                        foreach (var configured in await store.ListJobsAsync(tenant, stoppingToken))
                            if ((await engine.GetPlanAsync(configured, stoppingToken)).SyncState != "Applied")
                                await engine.SynchronizeAsync(configured, stoppingToken);
                    }
                    foreach (var pending in await store.GetPendingExecutionsAsync(stoppingToken))
                        await engine.EnqueueAsync(pending, stoppingToken);
                    await RefreshUnknownAsync(stoppingToken);
                    if (DateTimeOffset.UtcNow >= nextCleanup)
                    {
                        await store.CleanupHistoryAsync(DateTimeOffset.UtcNow.AddDays(-Math.Clamp(options.Value.HistoryDays, 90, 3650)), stoppingToken);
                        nextCleanup = DateTimeOffset.UtcNow.AddHours(1);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception exception) { LogUnavailable(logger, exception.GetType().Name); }
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
        finally { server?.Dispose(); }
    }

    public async Task DispatchAsync(string eventType, string tenantNId, string payload, CancellationToken token)
    {
        using var json = JsonDocument.Parse(payload);
        if (eventType == "Scheduler.JobChanged.v1")
        {
            var job = await store.GetJobAsync(tenantNId, json.RootElement.GetProperty("jobNId").GetString()!, token);
            if (job is not null) await engine.SynchronizeAsync(job, token);
        }
        else if (eventType == "Scheduler.RunRequested.v1")
        {
            var run = await store.GetRunAsync(tenantNId, json.RootElement.GetProperty("runNId").GetString()!, token);
            if (run is not null && !run.IsTerminal) await engine.EnqueueAsync(run, token);
        }
        else throw new InvalidOperationException("未知调度内部命令。");
    }

    public async Task RefreshUnknownAsync(CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var run in await store.GetActiveAsync(token))
        {
            if (run.Status == "Unknown") continue;
            if ((run.DeadlineOn is not null && now > run.DeadlineOn.Value.AddSeconds(5)) || await engine.IsWorkerAliveAsync(run.WorkerReference, token) == false)
                await store.SaveRunAsync(run with { Status = "Unknown", CompletedOn = now, ResultCode = "EXECUTION_RESULT_UNKNOWN", StateVersion = run.StateVersion + 1 }, run.StateVersion, token);
        }
    }

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning, Message = "调度能力尚不可用:{FailureType}；保留事实并稍后重试。")]
    private static partial void LogUnavailable(ILogger logger, string failureType);

    private sealed class ServiceJobActivator(IServiceProvider provider) : JobActivator
    {
        public override JobActivatorScope BeginScope(JobActivatorContext context) => new Scope(provider.CreateScope());
        private sealed class Scope(IServiceScope scope) : JobActivatorScope
        {
            public override object Resolve(Type type) => scope.ServiceProvider.GetRequiredService(type);
            public override void DisposeScope() => scope.Dispose();
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
