namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>
/// 有效刷新会话管理实现。所有查询都以租户为边界，撤销只触及一个会话行且幂等。
/// </summary>
public sealed class IdentitySessionManagementService : IIdentitySessionManagementService
{
    private readonly IRefreshSessionStore _store;

    public IdentitySessionManagementService(IRefreshSessionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
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
        return new IdentitySessionRevokeResult(
            found,
            found && string.Equals(sessionNId.Trim(), currentSessionNId, StringComparison.Ordinal));
    }
}
