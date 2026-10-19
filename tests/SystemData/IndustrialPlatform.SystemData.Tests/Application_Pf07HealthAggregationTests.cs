using IndustrialPlatform.SystemData.Application.PlatformHealth;
using IndustrialPlatform.SystemData.Contracts.PlatformHealth;

namespace IndustrialPlatform.SystemData.Application.Tests;

public sealed class Pf07HealthAggregationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Empty_enabled_service_is_unknown_and_disabled_service_is_explicit()
    {
        Assert.Equal("Unknown", PlatformHealthAggregation.Build("s", "S", [], Now).State);
        Assert.Equal("Disabled", PlatformHealthAggregation.Build("s", "S", [], Now, enabled: false).State);
    }

    [Fact]
    public void Stale_source_and_missing_time_cannot_be_refreshed_by_receiving_new_response()
    {
        var stale = Check("Required", "Healthy", Now.AddMinutes(-3), Now);
        var result = PlatformHealthAggregation.Build("s", "S", [stale], Now);
        Assert.Equal("Unknown", result.State);
        Assert.Equal("STALE_OBSERVATION", result.Checks[0].ReasonCode);
        Assert.Equal("Healthy", result.Checks[0].ReportedState);
        Assert.Equal(stale.CheckedOn, result.LastObservedOn);
        var missing = PlatformHealthAggregation.Build("s", "S", [stale with { CheckedOn = null }], Now);
        Assert.Equal("Unknown", missing.State);
        Assert.Null(missing.LastObservedOn);
        Assert.Null(missing.Checks[0].SourceTraceId);
    }

    [Fact]
    public void Required_degradation_and_optional_failure_are_degraded_not_healthy()
    {
        Assert.Equal("Degraded", PlatformHealthAggregation.Build("s", "S", [Check("Required", "Degraded", Now, Now)], Now).State);
        var optional = PlatformHealthAggregation.Build("s", "S", [Check("Required", "Healthy", Now, Now), Check("Capability", "Unavailable", Now, Now)], Now);
        Assert.Equal("Degraded", optional.State);
        Assert.Equal(1, optional.UnavailableCount);
        Assert.Equal("Unavailable", PlatformHealthAggregation.Build("s", "S", [Check("Required", "Unavailable", Now, Now)], Now).State);
    }

    [Fact]
    public void Disabled_and_future_source_time_are_not_healthy_checks()
    {
        var disabled = PlatformHealthAggregation.Build("s", "S", [Check("Capability", "Disabled", null, null)], Now);
        Assert.Equal("Unknown", disabled.State);
        Assert.Equal(1, disabled.DisabledCount);
        var future = PlatformHealthAggregation.Build("s", "S", [Check("Required", "Healthy", Now.AddSeconds(6), Now)], Now);
        Assert.Equal("Unknown", future.State);
        Assert.Equal("SOURCE_CLOCK_SKEW", future.Checks[0].ReasonCode);
    }

    private static PlatformHealthCheckDto Check(string criticality, string state, DateTimeOffset? checkedOn, DateTimeOffset? receivedOn) =>
        new("s", Guid.NewGuid().ToString("N"), "能力", criticality, state, state, checkedOn, receivedOn, null, null, "real-trace", null);
}
