using IndustrialPlatform.Identity.Domain.LoginSecurity;

namespace IndustrialPlatform.Identity.Domain.Tests;

/// <summary>
/// 登录失败锁定策略测试。
/// </summary>
public sealed class LoginAttemptPolicyTests
{
    [Fact]
    public void Default_HasFiveMaxFailures()
    {
        Assert.Equal(5, LoginAttemptPolicy.Default.MaxFailures);
    }

    [Fact]
    public void Default_HasFifteenMinuteLockDuration()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), LoginAttemptPolicy.Default.LockDuration);
    }

    [Fact]
    public void CustomValues_ArePreserved()
    {
        var policy = new LoginAttemptPolicy(3, TimeSpan.FromMinutes(5));

        Assert.Equal(3, policy.MaxFailures);
        Assert.Equal(TimeSpan.FromMinutes(5), policy.LockDuration);
    }

    [Fact]
    public void NonPositiveMaxFailures_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoginAttemptPolicy(0, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void NonPositiveLockDuration_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoginAttemptPolicy(5, TimeSpan.Zero));
    }
}
