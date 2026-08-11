using IndustrialPlatform.Identity.Domain.Users;

namespace IndustrialPlatform.Identity.Application.Authorization;

/// <summary>
/// 权限缓存条目:用户在指定安全版本下的状态。键按
/// <c>{tenant}:{userNId}:v{authVersion}</c> 隔离,命中即版本一致(§14)。
/// </summary>
public sealed record UserSecurityCacheEntry(UserStatus Status);

/// <summary>
/// 权限缓存端口(§14):版本化缓存键
/// <c>identity:user:{tenantNId}:{userNId}:v{authVersion}</c> 与
/// <c>identity:permission:{tenantNId}:{userNId}:v{authVersion}</c>。
/// 实现必须自愈:Redis 不可用时读取返回 <c>null</c>、写入静默忽略(降级到数据库)。
/// </summary>
public interface IPermissionCache
{
    /// <summary>按租户+用户+安全版本读取状态条目;未命中或缓存不可用返回 <c>null</c>。</summary>
    Task<UserSecurityCacheEntry?> TryGetUserSnapshotAsync(
        string tenantNId,
        string userNId,
        int authVersion,
        CancellationToken cancellationToken);

    /// <summary>按租户+用户+安全版本读取权限 NId 集;未命中或缓存不可用返回 <c>null</c>。</summary>
    Task<IReadOnlyList<string>?> TryGetPermissionsAsync(
        string tenantNId,
        string userNId,
        int authVersion,
        CancellationToken cancellationToken);

    /// <summary>写入/刷新授权快照(两个版本化键)并设置 TTL。</summary>
    Task SetAsync(AuthorizationSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>删除该用户全部安全版本的缓存键(SCAN pattern 匹配 <c>:v*</c>)。</summary>
    Task InvalidateAsync(string tenantNId, string userNId, CancellationToken cancellationToken);
}
