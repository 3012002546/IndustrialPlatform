using IndustrialPlatform.SystemData.Infrastructure.Scheduler;
using System.Text.Json;
using Hangfire;

using IndustrialPlatform.SystemData.Application.Scheduler;
using IndustrialPlatform.SystemData.Contracts.Scheduler;
using IndustrialPlatform.SystemData.Domain.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.RuntimeSupport.Jobs;

/// <summary>启动原生工作器，映射失联事实并引导唯一真实对账任务；不调度业务重试。</summary>
public partial class SchedulerWorker(HangfireSchedulerEngine engine, ISchedulerStore store,
    IServiceProvider services, IOptions<SchedulerOptions> options,
    ILogger<SchedulerWorker> logger) : BackgroundService, ISchedulerCommandDispatcher
{
    protected HangfireSchedulerEngine Engine => engine;
    protected ISchedulerStore Store => store;
    protected IOptions<SchedulerOptions> Options => options;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected virtual Task ConfigureAsync(CancellationToken token) => Task.CompletedTask;

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
                    await ConfigureAsync(stoppingToken);
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
