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
    public Task<UserGroup?> GetUserGroupAggregateIncludingDeletedAsync(string groupNId, CancellationToken cancellationToken)
        => _groupRepository.GetByNIdIncludingDeletedAsync(groupNId, cancellationToken);

    /// <inheritdoc/>
    public async Task<StoredUserGroupPage> QueryUserGroupsAsync(UserGroupListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var dbQuery = _dbContext.SqlSugar.Queryable<UserGroupTable>()
            .Where(t => t.TenantNId == query.TenantNId && (query.IncludeDeleted || !t.IsDeleted));

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            dbQuery = dbQuery.Where(t => t.Name.Contains(query.Name.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.NId)) dbQuery = dbQuery.Where(t => t.NId.Contains(query.NId.Trim()));
        if (!string.IsNullOrWhiteSpace(query.Description)) dbQuery = dbQuery.Where(t => t.Description != null && t.Description.Contains(query.Description.Trim()));
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            dbQuery = dbQuery.Where(t => t.NId.Contains(keyword) || t.Name.Contains(keyword) || (t.Description != null && t.Description.Contains(keyword)));
        }

        if (query.Status is { } status)
        {
            dbQuery = dbQuery.Where(t => t.Status == status);
        }

        var total = await dbQuery.CountAsync(cancellationToken);
        var orderedQuery = query.SortField?.ToLowerInvariant() switch
        {
            "nid" => query.SortOrder == "desc" ? dbQuery.OrderByDescending(t => t.NId) : dbQuery.OrderBy(t => t.NId),
            "name" => query.SortOrder == "desc" ? dbQuery.OrderByDescending(t => t.Name) : dbQuery.OrderBy(t => t.Name),
            "status" => query.SortOrder == "desc" ? dbQuery.OrderByDescending(t => t.Status) : dbQuery.OrderBy(t => t.Status),
            _ => dbQuery.OrderBy(t => t.CreatedOn, OrderByType.Desc),
        };
        var rows = await orderedQuery
            .Skip((query.PageIndex - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var memberCounts = await BuildMemberCountMapAsync(rows.Select(r => r.Id).ToArray(), cancellationToken);
        var roleCounts = await BuildGroupRoleCountMapAsync(rows.Select(r => r.Id).ToArray(), cancellationToken);

        var items = rows.Select(r => new StoredUserGroup(
            r.NId,
            r.Name,
            r.Description,
            r.Status.ToString(),
            r.TenantNId,
            memberCounts.TryGetValue(r.Id, out var m) ? m : 0,
            roleCounts.TryGetValue(r.Id, out var rc) ? rc : 0,
            r.CreatedOn,
            r.LastUpdatedOn,
            r.OptimisticVersion,
            r.ConcurrencyVersion,
            r.IsDeleted)).ToList();

        return new StoredUserGroupPage(items, total);
    }

    private async Task<Dictionary<Guid, int>> BuildMemberCountMapAsync(Guid[] groupIds, CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, int>();
        if (groupIds.Length == 0)
        {
            return map;
        }

        var rows = await _dbContext.SqlSugar.Queryable<UserGroupMembershipTable>()
            .Where(m => groupIds.Contains(m.UserGroupId) && !m.IsDeleted && !m.UserGroupIsDeleted)
            .GroupBy(m => m.UserGroupId)
            .Select(m => new { UserGroupId = m.UserGroupId, Count = SqlFunc.AggregateCount(m.Id) })
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            map[row.UserGroupId] = row.Count;
        }

        return map;
    }

    private async Task<Dictionary<Guid, int>> BuildGroupRoleCountMapAsync(Guid[] groupIds, CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, int>();
        if (groupIds.Length == 0)
        {
            return map;
        }

        var rows = await _dbContext.SqlSugar.Queryable<UserGroupRoleTable>()
            .Where(r => groupIds.Contains(r.UserGroupId) && !r.IsDeleted && !r.UserGroupIsDeleted && !r.RoleIsDeleted)
            .GroupBy(r => r.UserGroupId)
            .Select(r => new { UserGroupId = r.UserGroupId, Count = SqlFunc.AggregateCount(r.Id) })
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            map[row.UserGroupId] = row.Count;
        }

        return map;
    }

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
    public Task RestoreAsync(
        UserGroup group,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
        => _groupRepository.RestoreAsync(group, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken);

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
