using IndustrialPlatform.Identity.Contracts.Authentication;

namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>登录与当前用户用例。</summary>
public interface IAuthenticationService
{
    /// <summary>登录:校验凭证、限流与锁定策略,签发 Access/Refresh Token,写登录审计。</summary>
    Task<AuthSession> LoginAsync(
        LoginRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken);

    /// <summary>按令牌 sub(=UserNId)返回当前用户契约。</summary>
    Task<AuthUser> GetCurrentUserAsync(string userNId, CancellationToken cancellationToken);

    /// <summary>刷新:旋转 Refresh Token(同 Family),校验会话状态/过期/撤销与重放,返回完整新 AuthSession。</summary>
    Task<AuthSession> RefreshAsync(
        RefreshRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken);

    /// <summary>单会话注销:撤销当前 Refresh Token 所在 Family,并把 sid 写入撤销键直到 Access Token 到期。幂等。</summary>
    Task LogoutAsync(
        LogoutRequest request,
        string? sessionNId,
        TimeSpan sidRevocationTtl,
        CancellationToken cancellationToken);

    /// <summary>全部会话注销:撤销该用户全部 RefreshSession 并推进安全版本。</summary>
    Task LogoutAllAsync(string userNId, CancellationToken cancellationToken);

    /// <summary>修改密码:校验当前密码与复杂度,更新哈希并撤销该用户全部刷新会话(前端需重新登录)。</summary>
    Task ChangePasswordAsync(ChangePasswordRequest request, string userNId, CancellationToken cancellationToken);
}
