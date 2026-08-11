namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>
/// 登录限流/锁定端口(§14 Redis 键)。实现允许在 Redis 不可用时明确降级放行(§14),
/// 持久化用户级锁定由领域层独立保证。
/// </summary>
public interface ILoginRateLimiter
{
    /// <summary>按来源 IP 限流(每次登录尝试消耗一次;超限返回 <c>true</c>)。</summary>
    Task<bool> IsIpRateLimitedAsync(string? clientIp, CancellationToken cancellationToken);

    /// <summary>按租户+登录名+IP 组合锁判定(§10.3;达到最大连续失败返回 <c>true</c>)。</summary>
    Task<bool> IsAccountLockedAsync(string tenantNId, string normalizedLoginName, string? clientIp, CancellationToken cancellationToken);

    /// <summary>记录一次组合登录失败并推进锁定计数。</summary>
    Task RecordLoginFailureAsync(string tenantNId, string normalizedLoginName, string? clientIp, CancellationToken cancellationToken);

    /// <summary>登录成功时清零组合失败计数。</summary>
    Task RecordLoginSuccessAsync(string tenantNId, string normalizedLoginName, string? clientIp, CancellationToken cancellationToken);
}
