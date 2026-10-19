using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Domain.Scheduler;

namespace IndustrialPlatform.SystemData.Domain.Tests;

public sealed class Pf07SchedulerJobTests
{
    [Fact]
    public void Setting_same_target_state_preserves_versions_and_enabled_configuration_cannot_change()
    {
        var job = Create();
        var token = job.ConcurrencyVersion;
        Assert.False(job.SetEnabled(false));
        Assert.Equal(0, job.OptimisticVersion);
        Assert.Equal(token, job.ConcurrencyVersion);
        Assert.True(job.SetEnabled(true));
        Assert.Equal(1, job.OptimisticVersion);
        Assert.Throws<BusinessException>(() => job.Update("Changed", null, "{}", "0 * * * *", "Etc/UTC", 30, 1));
        Assert.Equal("任务", job.Name);
    }

    [Fact]
    public void Frozen_configuration_rejects_enable_without_changing_state()
    {
        var job = Create(); job.Freeze();
        Assert.ThrowsAny<BusinessException>(() => job.SetEnabled(true));
        Assert.Equal("Disabled", job.Status);
    }

    private static SchedulerJob Create() => new("tenant", "job", "任务", null, "systemdata.reconcile",
        "{\"schemaVersion\":1}", "*/15 * * * *", "Etc/UTC", 300, 1, "NoAutomaticRecovery");
}
