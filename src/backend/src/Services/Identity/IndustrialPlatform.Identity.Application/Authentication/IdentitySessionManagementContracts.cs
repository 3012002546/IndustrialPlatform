namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>管理端在线会话摘要，不承载任何可重放或设备敏感信息。</summary>
public sealed record IdentityActiveSession(
    string SessionNId,
    string UserNId,
    string LoginName,
    string Name,
    DateTimeOffset LoginOn,
    DateTimeOffset LastRefreshedOn,
    DateTimeOffset ExpiresOn,
    bool IsCurrent);

/// <summary>单会话撤销结果。</summary>
public sealed record IdentitySessionRevokeResult(bool Found, bool IsCurrent);

/// <summary>Identity 有效刷新会话管理用例。</summary>
public interface IIdentitySessionManagementService
{
    Task<IReadOnlyList<IdentityActiveSession>> ListActiveAsync(
        string tenantNId,
        string? currentSessionNId,
        CancellationToken cancellationToken);

    Task<IdentitySessionRevokeResult> RevokeAsync(
        string tenantNId,
        string sessionNId,
        string? currentSessionNId,
        CancellationToken cancellationToken);
}
