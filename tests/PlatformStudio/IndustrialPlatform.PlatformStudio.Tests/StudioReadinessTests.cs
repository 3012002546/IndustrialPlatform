using IndustrialPlatform.PlatformStudio.Api;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioReadinessTests
{
    [Fact]
    public async Task Readiness_reflects_physical_drift_without_repairing_it()
    {
        await using var fixture=await StudioTestContext.CreateAsync();
        var check=new StudioReadinessHealthCheck(new StudioServiceInitializer(fixture.Database,fixture.Profile,TimeProvider.System));
        Assert.Equal(HealthStatus.Healthy,(await check.CheckHealthAsync(new())).Status);
        await fixture.Database.Client.Ado.ExecuteCommandAsync("DROP INDEX ix_st_resource_list");
        var result=await check.CheckHealthAsync(new());Assert.Equal(HealthStatus.Unhealthy,result.Status);
        Assert.Contains("DRIFT",result.Description!,StringComparison.Ordinal);
        Assert.Equal(0,await fixture.Database.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_st_resource_list'"));
    }
}
