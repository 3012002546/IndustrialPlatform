using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

namespace IndustrialPlatform.Identity.Infrastructure.Authentication;

/// <summary>
/// 认证用例持久化端口实现:组合用户/角色仓储,装配活动角色与角色 NId 快照。
/// </summary>
public sealed class AuthenticationStore : IAuthenticationStore
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;

    /// <summary>初始化认证仓储组合。</summary>
    public AuthenticationStore(IUserRepository users, IRoleRepository roles)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(roles);
        _users = users;
        _roles = roles;
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

    /// <summary>装配活动角色:过滤角色已删除/快照失效的关系,保持 RoleIds 与 RoleNIds 一一对应。</summary>
    private async Task<AuthenticatedUser?> BuildAsync(User? user, CancellationToken cancellationToken)
    {
        if (user is null)
        {
            return null;
        }

        var roleIds = user.UserRoles.Where(r => !r.IsDeleted).Select(r => r.RoleId).ToList();
        var roleNIdByRoleId = await _roles.GetNIdsAsync(roleIds, cancellationToken);
        var activeRoleIds = roleIds.Where(roleNIdByRoleId.ContainsKey).ToList();
        var activeRoleNIds = activeRoleIds.Select(id => roleNIdByRoleId[id]).ToList();
        return new AuthenticatedUser(user, activeRoleIds, activeRoleNIds);
    }
}
