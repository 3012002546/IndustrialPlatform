using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.PlatformStudio.Api;

/// <summary>运行时仅检查Studio自有账本和物理结构，不以进程存活代替业务就绪。</summary>
public sealed class StudioReadinessHealthCheck(StudioServiceInitializer initializer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,CancellationToken cancellationToken=default)
    {
        var state=await initializer.InspectLocalAsync(cancellationToken);
        return state.Ready?HealthCheckResult.Healthy("Studio schema and seed verified."):HealthCheckResult.Unhealthy(state.Reason??"STUDIO_NOT_READY");
    }
}
