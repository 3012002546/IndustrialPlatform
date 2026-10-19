using IndustrialPlatform.SystemData.Application.Files;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.SystemData.Api.Health;

public sealed class FileScanHealthCheck(IFileScanner scanner, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (scanner is not IFileScannerHealth health) return HealthCheckResult.Degraded(data: new Dictionary<string, object> { ["reportedState"] = "Unknown" });
        var state = await health.ReadHealthAsync(cancellationToken);
        if (state == "Degraded" && configuration.GetValue<bool>("SystemData:ClamAv:DevelopmentBypass"))
            return HealthCheckResult.Degraded(data: new Dictionary<string, object> { ["reportedState"] = state, ["reasonCode"] = "FILE_SCAN_DEVELOPMENT_BYPASS", ["reasonSummary"] = "开发环境跳过病毒扫描，不代表扫描通过。" });
        return state == "Healthy" ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded(data: new Dictionary<string, object> { ["reportedState"] = state });
    }
}
