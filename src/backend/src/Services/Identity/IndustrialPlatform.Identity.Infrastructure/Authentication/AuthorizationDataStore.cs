using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

namespace IndustrialPlatform.Identity.Infrastructure.Authentication;

/// <summary>
/// 授权数据存储实现(§14/§18/§29A.2):组合用户/角色/用户组仓储装载授权快照。
/// 有效角色 = 直接分配角色 ∪ 用户组继承角色;权限集为全部有效角色的权限去重排序。
/// 用户仓储按 NId 查询不区分租户,此处必须显式校验租户,防止跨租户越权。
/// </summary>
public sealed class AuthorizationDataStore : IAuthorizationDataStore
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IUserGroupRepository _userGroups;

    /// <summary>初始化授权数据存储组合。</summary>
    public AuthorizationDataStore(IUserRepository users, IRoleRepository roles, IUserGroupRepository userGroups)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(userGroups);
        _users = users;
        _roles = roles;
        _userGroups = userGroups;
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

        // 有效角色并集(§29A.2):直接分配 ∪ 组继承(组未删除且 Active,关系双重过滤)。
        var roleIds = user.UserRoles
            .Where(r => !r.IsDeleted)
            .Select(r => r.RoleId)
            .Concat(await _userGroups.GetRoleIdsForUserAsync(user.Id, tenantNId, cancellationToken))
            .Distinct()
            .ToList();

        // 权限集与登录/刷新返回的 AuthUser.PermissionNIds 一致(同一仓储方法、同样序去重)。
        var permissions = await _roles.GetActivePermissionsForRolesAsync(roleIds, cancellationToken);
        var roles = await Task.WhenAll(roleIds.Select(roleId => _roles.GetByIdAsync(roleId, cancellationToken)));
        var isSystemAdmin = roles.Where(role => role is not null).Select(role => role!).Any(role =>
            !role.IsDeleted
            && role.IsSystem
            && string.Equals(role.NId, "SYSTEM_ADMIN", StringComparison.OrdinalIgnoreCase)
            && string.Equals(role.TenantNId, tenantNId, StringComparison.Ordinal));
        var permissionNIds = permissions
            .Select(p => p.NId)
            // 初始化与数据库编排权限不是普通角色可继承的有效输出；其唯一资格
            // 由 SYSTEM_ADMIN 事实在 PermissionEvaluator 中动态判定。
            .Where(p => !AuthorizationPermissionPolicy.IsProtectedInitializationPermission(p))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new AuthorizationSnapshot(
            user.TenantNId,
            user.NId,
            user.Status,
            user.AuthVersion,
            permissionNIds,
            isSystemAdmin);
    }
}
