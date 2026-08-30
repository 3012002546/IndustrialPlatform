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

/// <summary>
/// 刷新会话持久化投影(§13 业务字段)。仅承载校验与旋转所需字段,
/// 不含 TokenHash 原文之外的可重放信息;供旋转/撤销用例使用。
/// </summary>
public sealed record StoredRefreshSession(
    Guid Id,
    string TenantNId,
    string NId,
    string FamilyNId,
    Guid UserId,
    bool UserIsDeleted,
    DateTimeOffset ExpiresOn,
    DateTimeOffset? UsedOn,
    DateTimeOffset? RevokedOn,
    string? RevokeReason,
    string? ReplacedBySessionNId);

/// <summary>
/// 管理端有效刷新会话摘要。绝不包含 refresh token、token hash、IP 或 User-Agent。
/// </summary>
public sealed record ActiveRefreshSession(
    string SessionNId,
    string UserNId,
    string LoginName,
    string Name,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastRefreshedOn,
    DateTimeOffset ExpiresOn,
    Guid UserId);

/// <summary>旋转操作结果:Rotated=成功、Reused=Token 已被消费(重放)、Invalid=已撤销/过期/不存在。</summary>
public enum RefreshRotationStatus
{
    /// <summary>旋转成功,新会话已落库。</summary>
    Rotated,

    /// <summary>Token 已被消费(顺序或并发重用),应撤销整个 Family。</summary>
    Reused,

    /// <summary>会话不存在/已撤销/已过期,视为无效令牌。</summary>
    Invalid,
}

/// <summary>
/// 刷新会话持久化端口(§13):登录追加写入,刷新旋转(原子防重放),注销/改密撤销。
/// 实现按原始 Token 的 SHA-256 哈希定位,原始值绝不下库。
/// </summary>
public interface IRefreshSessionStore
{
    /// <summary>按原始刷新令牌查找会话(实现内部哈希);不存在返回 <c>null</c>。</summary>
    Task<StoredRefreshSession?> FindByRawTokenAsync(string rawToken, CancellationToken cancellationToken);

    /// <summary>
    /// 追加写入新会话(登录)。Token 哈希唯一,碰撞时抛存储异常由用例映射安全存储不可用。
    /// </summary>
    Task AddAsync(NewRefreshSession session, CancellationToken cancellationToken);

    /// <summary>
    /// 原子旋转:事务内把当前会话标记已用并写入替代会话(同一 Family),替代会话通过
    /// <c>ReplacedBySessionId</c> 自引用。当前会话已被消费时返回 <c>Reused</c>,
    /// 已撤销/过期/不存在返回 <c>Invalid</c>,成功返回 <c>Rotated</c>。
    /// </summary>
    /// <param name="currentSessionId">当前会话 Id。</param>
    /// <param name="replacement">替代会话(新 NId/FamilyNId=当前 FamilyNId/新 Token)。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<RefreshRotationStatus> RotateAsync(Guid currentSessionId, NewRefreshSession replacement, CancellationToken cancellationToken);

    /// <summary>撤销整个 Family 的全部会话(幂等),用于重放检测、单会话注销。</summary>
    Task RevokeFamilyAsync(string familyNId, string reason, CancellationToken cancellationToken);

    /// <summary>撤销某用户全部会话(幂等),用于全部注销与密码修改。</summary>
    Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken);

    /// <summary>列出租户内当前有效刷新会话，服务端先施加完整有效性条件。</summary>
    Task<IReadOnlyList<ActiveRefreshSession>> ListActiveForTenantAsync(
        string tenantNId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ActiveRefreshSession>>([]);

    /// <summary>按租户撤销单个刷新会话；不存在返回 false，已撤销重复调用返回 true。</summary>
    Task<bool> RevokeByNIdAsync(
        string tenantNId,
        string sessionNId,
        string reason,
        CancellationToken cancellationToken)
        => Task.FromResult(false);
}
