namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>
/// 认证用例配置(§10.3 登录限制、§14 Redis、§13 Refresh Token 有效期)。
/// 绑定自配置节 <c>Identity:Authentication</c>。
/// </summary>
public sealed class AuthenticationOptions
{
    /// <summary>登录请求未指定租户时使用的默认租户(§15.1 请求无租户字段)。</summary>
    public string DefaultTenantNId { get; set; } = "development";

    /// <summary>触发锁定前的最大连续失败次数(§10.3)。</summary>
    public int MaxLoginFailures { get; set; } = 5;

    /// <summary>达到阈值后的锁定持续时间(§10.3)。</summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>单 IP 登录限流窗口内最大尝试次数(§10.3,429)。</summary>
    public int IpRateLimitMaxAttempts { get; set; } = 20;

    /// <summary>单 IP 登录限流窗口(§10.3)。</summary>
    public TimeSpan IpRateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Refresh Token 默认有效期天数(§13,7 天)。</summary>
    public int RefreshTokenLifetimeDays { get; set; } = 7;
}
