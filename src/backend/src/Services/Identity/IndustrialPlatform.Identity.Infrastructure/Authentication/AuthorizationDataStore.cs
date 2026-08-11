using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

namespace IndustrialPlatform.Identity.Infrastructure.Authentication;

/// <summary>
/// 授权数据存储实现(§14/§18):组合用户/角色仓储装载授权快照。
/// 用户仓储按 NId 查询不区分租户,此处必须显式校验租户,防止跨租户越权。
/// </summary>
public sealed class AuthorizationDataStore : IAuthorizationDataStore
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;

    /// <summary>初始化授权数据存储组合。</summary>
    public AuthorizationDataStore(IUserRepository users, IRoleRepository roles)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(roles);
        _users = users;
        _roles = roles;
    }

    /// <inheritdoc/>
    public async Task<AuthorizationSnapshot?> GetSnapshotAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByNIdAsync(userNId, cancellationToken);
        if (user is null || !string.Equals(user.TenantNId, tenantNId, StringComparison.Ordinal))
        {
            return null;
        }

        // 权限集与登录/刷新返回的 AuthUser.PermissionNIds 一致(同一仓储方法、同样序去重)。
        var roleIds = user.UserRoles.Where(r => !r.IsDeleted).Select(r => r.RoleId).ToList();
        var permissions = await _roles.GetActivePermissionsForRolesAsync(roleIds, cancellationToken);
        var permissionNIds = permissions
            .Select(p => p.NId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new AuthorizationSnapshot(
            user.TenantNId,
            user.NId,
            user.Status,
            user.AuthVersion,
            permissionNIds);
    }
}
