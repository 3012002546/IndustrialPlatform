namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>
/// 新建刷新会话(§13)。RawToken 为随机会话令牌,仅在内存流转;
/// 实现只持久化 SHA-256 哈希,绝不落库原始值。
/// </summary>
public sealed record NewRefreshSession(
    string TenantNId,
    string NId,
    string FamilyNId,
    Guid UserId,
    string RawToken,
    DateTimeOffset ExpiresOn,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedOn);

/// <summary>刷新会话持久化端口(登录时写入,旋转/撤销由 TASK-ID-006)。</summary>
public interface IRefreshSessionStore
{
    Task AddAsync(NewRefreshSession session, CancellationToken cancellationToken);
}
