using System.Collections.Concurrent;
using System.Diagnostics;
using IndustrialPlatform.SystemData.Application.PlatformHealth;
using IndustrialPlatform.SystemData.Contracts.PlatformHealth;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Api.Health;

/// <summary>每项只保留最新真实检查，按轮次防止迟到结果覆盖；不依赖调度引擎。</summary>
public sealed partial class PlatformHealthSampler(IServiceProvider services, IOptions<HealthCheckServiceOptions> healthOptions,
    ILogger<PlatformHealthSampler> logger, IOptions<PlatformHealthOptions> options) : BackgroundService, IPlatformHealthSnapshot
{
    private readonly ConcurrentDictionary<string, PlatformHealthCheckDto> _latest = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PlatformHealthCheckDto> Read(string serviceNId) => _latest.Values
        .Where(check => check.ServiceNId.Equals(serviceNId, StringComparison.OrdinalIgnoreCase)).OrderBy(check => check.CapabilityKey, StringComparer.Ordinal).ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SampleAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { LogUnavailable(logger, exception.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.SampleIntervalSeconds, 5, 300)), stoppingToken);
        }
    }

    public async Task SampleAsync(CancellationToken token)
    {
        var registrations = healthOptions.Value.Registrations.Where(registration => registration.Name != "platform-health").ToArray();
        await Parallel.ForEachAsync(registrations, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(options.Value.SampleConcurrency, 1, 16), CancellationToken = token }, async (registration, cancellation) =>
        {
            var pieces = registration.Name.Split('.', 2);
            var service = pieces.Length == 2 ? pieces[0] : "systemdata";
            var capability = pieces[^1];
            var required = capability is "postgres" or "database" or "initialization";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.CheckTimeoutSeconds, 1, 30)));
            var trace = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            PlatformHealthCheckDto result;
            try
            {
                using var scope = services.CreateScope();
                var checker = registration.Factory(scope.ServiceProvider);
                var task = checker.CheckHealthAsync(new HealthCheckContext { Registration = registration }, timeout.Token);
                var health = await task.WaitAsync(timeout.Token);
                var observed = DateTimeOffset.UtcNow;
                var state = health.Status == HealthStatus.Healthy ? "Healthy" : health.Status == HealthStatus.Degraded ? "Degraded" : required ? "Unavailable" : "Degraded";
                if (health.Data.TryGetValue("reportedState", out var explicitState) && explicitState is string value && value is "Disabled" or "Unknown" or "Degraded") state = value;
                if (capability == "scheduler" && health.Status == HealthStatus.Unhealthy) state = "Unavailable";
                var reasonCode = health.Data.TryGetValue("reasonCode", out var reasonValue) && reasonValue is string code && code.Length <= 64 ? code : health.Status == HealthStatus.Healthy ? null : "DEPENDENCY_UNAVAILABLE";
                var reasonSummary = health.Data.TryGetValue("reasonSummary", out var summaryValue) && summaryValue is string summary && summary.Length <= 1000 ? summary : health.Status == HealthStatus.Healthy ? null : "能力检查未通过。";
                result = new(service, capability, registration.Name, required ? "Required" : "Capability", state, state,
                    observed, observed, reasonCode, reasonSummary, trace, null);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                result = new(service, capability, registration.Name, required ? "Required" : "Capability", "Unknown", "Unknown", now, now, "CHECK_TIMEOUT", "检查超时。", trace, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var now = DateTimeOffset.UtcNow;
                result = new(service, capability, registration.Name, required ? "Required" : "Capability", "Unknown", "Unknown", now, now, "CHECK_UNAVAILABLE", "检查适配不可用。", trace, null);
            }
            _latest[service + ":" + capability] = result;
        });
        foreach (var capability in new[] { "realtime", "remote-assistance" })
        {
            var key = "collaboration:" + capability;
            if (!_latest.ContainsKey(key)) _latest[key] = new("collaboration", capability, capability, "Capability", "Unknown", "Unknown", null, DateTimeOffset.UtcNow, "SOURCE_ADAPTER_UNAVAILABLE", "所属模块未提供受控能力摘要。", null, null);
        }
    }

    [LoggerMessage(EventId = 7102, Level = LogLevel.Warning, Message = "平台健康采样不可用:{FailureType}")]
    private static partial void LogUnavailable(ILogger logger, string failureType);
}
