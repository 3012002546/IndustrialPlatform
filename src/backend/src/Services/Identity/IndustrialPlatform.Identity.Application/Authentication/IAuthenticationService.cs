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
}
