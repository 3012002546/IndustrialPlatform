namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>
/// 会话撤销存储端口(§13/§14):把会话 `sid` 写入 Redis 撤销键直到 Access Token 到期,
/// 刷新/授权用例据此拒绝已注销会话。
/// 校验(IsRevokedAsync)必须 fail-closed:Redis 不可用时抛安全存储不可用,不得静默放行(§14)。
/// </summary>
public interface ISessionRevocationStore
{
    /// <summary>
    /// 撤销会话 `sid` 指定时长(通常为 Access Token 剩余有效期)。写入为尽力而为:
    /// 写入失败必须抛出安全存储不可用,调用端不得把未确认的撤销报告为成功。
    /// </summary>
    Task RevokeAsync(string sessionNId, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>
    /// 查询会话是否被撤销。Redis 不可用时抛 <see cref="SecurityStoreUnavailableException"/>
    /// (fail-closed),不得返回 <c>false</c> 放行。
    /// </summary>
    Task<bool> IsRevokedAsync(string sessionNId, CancellationToken cancellationToken);
}
