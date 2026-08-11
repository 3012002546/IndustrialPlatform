namespace IndustrialPlatform.Identity.Domain.LoginSecurity;

/// <summary>
/// 登录失败锁定策略:达到最大连续失败次数后锁定指定时长。
/// </summary>
public sealed record LoginAttemptPolicy
{
    /// <summary>默认策略:连续失败 5 次锁定 15 分钟。</summary>
    public static LoginAttemptPolicy Default { get; } = new(5, TimeSpan.FromMinutes(15));

    /// <summary>触发锁定前的最大连续失败次数。</summary>
    public int MaxFailures { get; }

    /// <summary>达到阈值后的锁定持续时间。</summary>
    public TimeSpan LockDuration { get; }

    /// <summary>
    /// 初始化登录失败锁定策略。
    /// </summary>
    /// <param name="maxFailures">最大连续失败次数,必须为正数。</param>
    /// <param name="lockDuration">锁定持续时间,必须大于零。</param>
    /// <exception cref="ArgumentOutOfRangeException">参数不合法时抛出。</exception>
    public LoginAttemptPolicy(int maxFailures, TimeSpan lockDuration)
    {
        if (maxFailures <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFailures), "最大失败次数必须为正数。");
        }

        if (lockDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockDuration), "锁定持续时间必须大于零。");
        }

        MaxFailures = maxFailures;
        LockDuration = lockDuration;
    }
}
