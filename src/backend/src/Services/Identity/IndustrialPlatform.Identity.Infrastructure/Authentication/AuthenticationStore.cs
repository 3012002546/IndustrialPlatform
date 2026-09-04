using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

namespace IndustrialPlatform.Identity.Infrastructure.Authentication;

/// <summary>
/// 认证用例持久化端口实现:组合用户/角色/用户组仓储,装配有效角色
/// (直接分配 ∪ 用户组继承,§29A.2)与角色 NId 快照。
/// </summary>
public sealed class AuthenticationStore : IAuthenticationStore
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IUserGroupRepository _userGroups;

    /// <summary>初始化认证仓储组合。</summary>
    public AuthenticationStore(IUserRepository users, IRoleRepository roles, IUserGroupRepository userGroups)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(userGroups);
        _users = users;
        _roles = roles;
        _userGroups = userGroups;
    }

    /// <inheritdoc/>
    public async Task<AuthenticatedUser?> FindByNormalizedLoginNameAsync(
        string tenantNId,
        string normalizedLoginName,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByNormalizedLoginNameAsync(tenantNId, normalizedLoginName, cancellationToken);
        return await BuildAsync(user, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AuthenticatedUser?> FindByNIdAsync(string userNId, CancellationToken cancellationToken)
    {
        var user = await _users.GetByNIdAsync(userNId, cancellationToken);
        return await BuildAsync(user, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AuthenticatedUser?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);
        return await BuildAsync(user, cancellationToken);
    }

    /// <inheritdoc/>
    public Task UpdateUserAsync(
        User user,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
        => _users.UpdateAsync(user, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Domain.Permissions.Permission>> GetPermissionsForRolesAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
        => _roles.GetActivePermissionsForRolesAsync(roleIds, cancellationToken);

    /// <summary>装配有效角色:直接分配 ∪ 组继承,过滤角色已删除/快照失效的关系,保持 RoleIds 与 RoleNIds 一一对应。</summary>
    private async Task<AuthenticatedUser?> BuildAsync(User? user, CancellationToken cancellationToken)
    {
        if (user is null)
        {
            return null;
        }

        var directRoleIds = user.UserRoles.Where(r => !r.IsDeleted).Select(r => r.RoleId).ToList();
        var groupRoleIds = await _userGroups.GetRoleIdsForUserAsync(user.Id, user.TenantNId, cancellationToken);
        var roleIds = directRoleIds.Concat(groupRoleIds).Distinct().ToList();
        var roleNIdByRoleId = await _roles.GetNIdsAsync(roleIds, cancellationToken);
        var activeRoleIds = roleIds.Where(roleNIdByRoleId.ContainsKey).ToList();
        var activeRoleNIds = activeRoleIds.Select(id => roleNIdByRoleId[id]).ToList();
        var isSystemAdmin = !user.IsDeleted
            && user.Status == UserStatus.Active
            && activeRoleNIds.Any(roleNId => string.Equals(roleNId, "SYSTEM_ADMIN", StringComparison.OrdinalIgnoreCase));
        return new AuthenticatedUser(user, activeRoleIds, activeRoleNIds, isSystemAdmin);
    }
}
