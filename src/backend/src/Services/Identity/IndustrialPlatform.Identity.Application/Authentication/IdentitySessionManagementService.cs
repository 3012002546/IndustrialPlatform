using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>
/// 有效刷新会话管理实现。所有查询都以租户为边界，撤销只触及一个会话行且幂等。
/// </summary>
public sealed class IdentitySessionManagementService : IIdentitySessionManagementService
{
    private readonly IRefreshSessionStore _store;
    private readonly ISessionRevocationStore _sessionRevocation;
    private readonly IOptions<AuthenticationOptions> _options;

    public IdentitySessionManagementService(
        IRefreshSessionStore store,
        ISessionRevocationStore sessionRevocation,
        IOptions<AuthenticationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sessionRevocation);
        ArgumentNullException.ThrowIfNull(options);
        _store = store;
        _sessionRevocation = sessionRevocation;
        _options = options;
    }

    public async Task<IReadOnlyList<IdentityActiveSession>> ListActiveAsync(
        string tenantNId,
        string? currentSessionNId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantNId);
        var sessions = await _store.ListActiveForTenantAsync(tenantNId, DateTimeOffset.UtcNow, cancellationToken);
        return sessions
            .Select(session => new IdentityActiveSession(
                session.SessionNId,
                session.UserNId,
                session.LoginName,
                session.Name,
                session.CreatedOn,
                session.LastRefreshedOn,
                session.ExpiresOn,
                string.Equals(session.SessionNId, currentSessionNId, StringComparison.Ordinal)))
            .OrderByDescending(session => session.LoginOn)
            .ThenBy(session => session.LoginName, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IdentitySessionRevokeResult> RevokeAsync(
        string tenantNId,
        string sessionNId,
        string? currentSessionNId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantNId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionNId);
        var found = await _store.RevokeByNIdAsync(
            tenantNId,
            sessionNId.Trim(),
            "admin_revoke",
            cancellationToken);
        if (found)
        {
            // Refresh row 撤销阻止后续旋转;sid 撤销立即阻止已经签发的 Access Token。
            // TTL 以 Access Token 最大生命周期为界,不把撤销键延长到 Refresh Token 生命周期。
            var minutes = Math.Clamp(_options.Value.AccessTokenRevocationMinutes, 1, 24 * 60);
            await _sessionRevocation.RevokeAsync(
                sessionNId.Trim(),
                TimeSpan.FromMinutes(minutes),
                cancellationToken);
        }
        return new IdentitySessionRevokeResult(
            found,
            found && string.Equals(sessionNId.Trim(), currentSessionNId, StringComparison.Ordinal));
    }
}
