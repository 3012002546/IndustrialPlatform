using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.ReferenceData.Api.Health;

public sealed class InitializationHealthCheck(ReferenceDataServiceInitializer initializer, IndustrialPlatform.ReferenceData.Api.Initialization.ReferenceDataHostContext contextFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var state = await initializer.InspectAsync(contextFactory.Create("readiness", "readiness"), cancellationToken);
            return state.Ready ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(state.Reason);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("REF-INITIALIZATION-UNAVAILABLE");
        }
    }
}
