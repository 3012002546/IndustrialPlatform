using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Application.UserGroups;
using IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Infrastructure.Database;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Management;

/// <summary>
/// 用户组持久化端口实现(§29A.2/§29A.6):组合用户组/用户仓储并补充成员、唯一性与授权求值辅助查询。
/// 按 NId 查询不区分租户,租户隔离由应用层显式校验。
/// </summary>
public sealed class UserGroupStore : IUserGroupStore
{
    private readonly SqlSugarDbContext _dbContext;
    private readonly IUserGroupRepository _groupRepository;
    private readonly IUserRepository _userRepository;

    /// <summary>初始化用户组存储。</summary>
    public UserGroupStore(
        SqlSugarDbContext dbContext,
        IUserGroupRepository groupRepository,
        IUserRepository userRepository)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(groupRepository);
        ArgumentNullException.ThrowIfNull(userRepository);
        _dbContext = dbContext;
        _groupRepository = groupRepository;
        _userRepository = userRepository;
    }

    /// <inheritdoc/>
    public Task<UserGroup?> GetUserGroupAggregateAsync(string groupNId, CancellationToken cancellationToken)
        => _groupRepository.GetByNIdAsync(groupNId, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> UserGroupExistsByNIdAsync(string groupNId, CancellationToken cancellationToken)
    {
        var normalized = NId.Create(groupNId).Normalized;
        return await _dbContext.SqlSugar.Queryable<UserGroupTable>()
            .Where(t => t.NormalizedNId == normalized)
            .AnyAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<User>> GetUsersByNIdsAsync(IReadOnlyCollection<string> userNIds, CancellationToken cancellationToken)
    {
        if (userNIds.Count == 0)
        {
            return [];
        }

        var normalizedNIds = userNIds.Select(n => NId.Create(n).Normalized).Distinct(StringComparer.Ordinal).ToArray();
        var rows = await _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(t => normalizedNIds.Contains(t.NormalizedNId) && !t.IsDeleted)
            .ToListAsync(cancellationToken);

        var users = new List<User>(rows.Count);
        foreach (var row in rows)
        {
            var user = await _userRepository.GetByIdAsync(row.Id, cancellationToken);
            if (user is not null)
            {
                users.Add(user);
            }
        }

        return users;
    }

    /// <inheritdoc/>
    public Task AddAsync(UserGroup group, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken)
        => _groupRepository.AddAsync(group, outboxEvents, cancellationToken);

    /// <inheritdoc/>
    public Task UpdateAsync(
        UserGroup group,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        IReadOnlyCollection<OutboxEnvelope> outboxEvents,
        CancellationToken cancellationToken)
        => _groupRepository.UpdateAsync(group, expectedOptimisticVersion, expectedConcurrencyVersion, outboxEvents, cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<User>> GetMembersAsync(Guid groupId, string tenantNId, CancellationToken cancellationToken)
    {
        var userIds = await GetActiveMemberUserIdsAsync(groupId, tenantNId, cancellationToken);
        if (userIds.Count == 0)
        {
            return [];
        }

        var members = new List<User>(userIds.Count);
        foreach (var userId in userIds)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user is not null && string.Equals(user.TenantNId, tenantNId, StringComparison.Ordinal))
            {
                members.Add(user);
            }
        }

        return members;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetMemberUserNIdsAsync(Guid groupId, string tenantNId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<UserGroupMembershipTable>()
            .Where(t => t.UserGroupId == groupId
                && t.TenantNId == tenantNId
                && !t.IsDeleted
                && !t.UserGroupIsDeleted
                && !t.UserIsDeleted)
            .InnerJoin<UserTable>((m, u) => m.UserId == u.Id && !u.IsDeleted)
            .Select((m, u) => u.NId)
            .ToListAsync(cancellationToken);
        return rows.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> GetRoleIdsForUserAsync(Guid userId, string tenantNId, CancellationToken cancellationToken)
        => _groupRepository.GetRoleIdsForUserAsync(userId, tenantNId, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> UserHoldsRoleOutsideGroupAsync(
        Guid userId,
        Guid groupId,
        Guid roleId,
        string tenantNId,
        CancellationToken cancellationToken)
    {
        // 直接分配
        var direct = await _dbContext.SqlSugar.Queryable<UserRoleTable>()
            .Where(t => t.UserId == userId
                && t.RoleId == roleId
                && t.TenantNId == tenantNId
                && !t.IsDeleted
                && !t.UserIsDeleted
                && !t.RoleIsDeleted)
            .AnyAsync(cancellationToken);
        if (direct)
        {
            return true;
        }

        // 其他活动用户组继承(排除本组)
        return await _dbContext.SqlSugar.Queryable<UserGroupMembershipTable>()
            .InnerJoin<UserGroupTable>((m, g) => m.UserGroupId == g.Id
                && g.TenantNId == tenantNId
                && !g.IsDeleted
                && g.Status == UserGroupStatus.Active)
            .InnerJoin<UserGroupRoleTable>((m, g, r) => m.UserGroupId == r.UserGroupId
                && r.RoleId == roleId
                && !r.IsDeleted
                && !r.UserGroupIsDeleted
                && !r.RoleIsDeleted)
            .Where((m, g, r) => m.UserId == userId
                && m.UserGroupId != groupId
                && m.TenantNId == tenantNId
                && !m.IsDeleted
                && !m.UserIsDeleted
                && !m.UserGroupIsDeleted)
            .AnyAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> AnyMemberHoldsRoleOnlyViaGroupAsync(
        Guid groupId,
        Guid roleId,
        string tenantNId,
        CancellationToken cancellationToken)
    {
        var memberIds = await GetActiveMemberUserIdsAsync(groupId, tenantNId, cancellationToken);
        if (memberIds.Count == 0)
        {
            return false;
        }

        foreach (var userId in memberIds)
        {
            // 成员仅经本组持有该角色(本组已持有,只需确认组外无路径)。
            if (!await UserHoldsRoleOutsideGroupAsync(userId, groupId, roleId, tenantNId, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>查询用户组活动成员的用户主键集(成员关系与两端父级均未删除,租户已过滤)。</summary>
    private async Task<IReadOnlyList<Guid>> GetActiveMemberUserIdsAsync(Guid groupId, string tenantNId, CancellationToken cancellationToken)
    {
        var ids = await _dbContext.SqlSugar.Queryable<UserGroupMembershipTable>()
            .Where(t => t.UserGroupId == groupId
                && t.TenantNId == tenantNId
                && !t.IsDeleted
                && !t.UserGroupIsDeleted
                && !t.UserIsDeleted)
            .Select(t => t.UserId)
            .ToListAsync(cancellationToken);
        return ids.OrderBy(id => id).ToList();
    }
}
