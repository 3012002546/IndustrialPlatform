using IndustrialPlatform.ReferenceData.Api.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class CapabilityHealthTests
{
    [Fact]
    public void Ready_checks_exclude_optional_capabilities_and_include_local_initialization()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddReferenceDataHealthChecks();
        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.Contains(registrations, item => item.Name == "initialization" && item.Tags.Contains("ready"));
        foreach (var name in new[] { "redis", "rabbitmq", "seq" })
        {
            var check = Assert.Single(registrations, item => item.Name == name);
            Assert.Contains("capability", check.Tags);
            Assert.DoesNotContain("ready", check.Tags);
            Assert.Equal(HealthStatus.Degraded, check.FailureStatus);
        }
    }
}
