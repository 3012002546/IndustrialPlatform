using IndustrialPlatform.SystemData.Infrastructure.Scheduler;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.SystemData.Api.Health;

public sealed class SchedulerHealthCheck(HangfireSchedulerEngine engine) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!engine.Enabled) return Task.FromResult(HealthCheckResult.Healthy(data: new Dictionary<string, object> { ["reportedState"] = "Disabled" }));
        try
        {
            var live = engine.Storage.GetMonitoringApi().Servers().Any(server => server.Heartbeat > DateTime.UtcNow.AddSeconds(-60));
            return Task.FromResult(live ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("没有新鲜的工作器心跳。"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return Task.FromResult(HealthCheckResult.Unhealthy("调度存储不可用。")); }
    }
}
