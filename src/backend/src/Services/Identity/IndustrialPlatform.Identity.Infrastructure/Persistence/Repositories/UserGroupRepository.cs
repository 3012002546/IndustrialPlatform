using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.Identity.Infrastructure.Outbox;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Exceptions;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// 用户组聚合仓储实现:POCO ↔ 聚合双向映射、软删除过滤、双版本并发原子更新与
/// 成员/组角色关系 diff 同步。父级软删除/恢复由数据库复合外键 ON UPDATE CASCADE 同步子表影子列。
/// </summary>
public sealed class UserGroupRepository : IUserGroupRepository
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化用户组仓储。</summary>
    public UserGroupRepository(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<UserGroup?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SqlSugar.Queryable<UserGroupTable>()
            .Where(t => t.Id == id && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : await LoadWithRelationsAsync(row, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UserGroup?> GetByNIdAsync(string groupNId, CancellationToken cancellationToken = default)
    {
        var normalized = NId.Create(groupNId).Normalized;
        var row = await _dbContext.SqlSugar.Queryable<UserGroupTable>()
            .Where(t => t.NormalizedNId == normalized && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : await LoadWithRelationsAsync(row, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UserGroup?> GetByNIdIncludingDeletedAsync(string groupNId, CancellationToken cancellationToken = default)
    {
        var normalized = NId.Create(groupNId).Normalized;
        var row = await _dbContext.SqlSugar.Queryable<UserGroupTable>()
            .Where(t => t.NormalizedNId == normalized)
            .FirstAsync(cancellationToken);
        return row is null ? null : await LoadWithRelationsAsync(row, cancellationToken);
    }

    /// <summary>载入活动成员与组角色关系(子表自身未删除 且 父级影子列未删除)。</summary>
    private async Task<UserGroup?> LoadWithRelationsAsync(UserGroupTable row, CancellationToken cancellationToken)
    {
        var memberships = await _dbContext.SqlSugar.Queryable<UserGroupMembershipTable>()
            .Where(t => t.UserGroupId == row.Id && !t.IsDeleted && !t.UserGroupIsDeleted)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);
        var roles = await _dbContext.SqlSugar.Queryable<UserGroupRoleTable>()
            .Where(t => t.UserGroupId == row.Id && !t.IsDeleted && !t.UserGroupIsDeleted)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);
        return TableMapper.ToUserGroup(
            row,
            memberships.Select(TableMapper.ToUserGroupMembership).ToList(),
            roles.Select(TableMapper.ToUserGroupRole).ToList());
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>> GetRoleIdsForUserAsync(
        Guid userId,
        string tenantNId,
        CancellationToken cancellationToken = default)
    {
        // 用户 → 活动成员关系(自身+两端影子列未删)→ 活动用户组(未删+Active)→ 组角色(自身+两端影子列未删)
        var roleIds = await _dbContext.SqlSugar.Queryable<UserGroupMembershipTable>()
            .InnerJoin<UserGroupTable>((m, g) => m.UserGroupId == g.Id && g.TenantNId == tenantNId && !g.IsDeleted && g.Status == UserGroupStatus.Active)
            .InnerJoin<UserGroupRoleTable>((m, g, r) => m.UserGroupId == r.UserGroupId && !r.IsDeleted && !r.UserGroupIsDeleted && !r.RoleIsDeleted)
            .Where((m, g, r) => m.UserId == userId
                && m.TenantNId == tenantNId
                && !m.IsDeleted
                && !m.UserIsDeleted
                && !m.UserGroupIsDeleted)
            .Select((m, g, r) => r.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return roleIds;
    }

    /// <inheritdoc/>
    public Task AddAsync(UserGroup group, CancellationToken cancellationToken = default)
        => AddAsync(group, outboxEvents: null, cancellationToken);

    /// <summary>新增用户组,事务内级联活动成员/组角色关系与 Outbox 事件原子提交(§20);outboxEvents 为空时跳过 Outbox 写入。</summary>
    public async Task AddAsync(
        UserGroup group,
        IReadOnlyCollection<OutboxEnvelope>? outboxEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await sugar.Insertable(TableMapper.ToTable(group)).ExecuteCommandAsync(cancellationToken);
            foreach (var membership in group.Memberships.Where(m => !m.IsDeleted && !m.UserIsDeleted))
            {
                await sugar.Insertable(TableMapper.ToTable(membership)).ExecuteCommandAsync(cancellationToken);
            }

            foreach (var role in group.Roles.Where(r => !r.IsDeleted && !r.RoleIsDeleted))
            {
                await sugar.Insertable(TableMapper.ToTable(role)).ExecuteCommandAsync(cancellationToken);
            }

            await OutboxRows.InsertAsync(sugar, outboxEvents, cancellationToken);

            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    /// <inheritdoc/>
    public Task UpdateAsync(
        UserGroup group,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
        => UpdateAsync(group, expectedOptimisticVersion, expectedConcurrencyVersion, outboxEvents: null, cancellationToken);

    /// <summary>按双版本原子更新用户组与成员/组角色关系 diff,事务内与 Outbox 事件原子提交(§20);冲突抛并发异常;outboxEvents 为空时跳过 Outbox 写入。</summary>
    public async Task UpdateAsync(
        UserGroup group,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        IReadOnlyCollection<OutboxEnvelope>? outboxEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            var affected = await UpdateParentAsync(
                group,
                expectedOptimisticVersion,
                expectedConcurrencyVersion,
                cancellationToken);
            EnsureSingleRowAffected(affected, group, "更新");

            await SyncMembershipsAsync(sugar, group, cancellationToken);
            await SyncRolesAsync(sugar, group, cancellationToken);

            await OutboxRows.InsertAsync(sugar, outboxEvents, cancellationToken);

            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        UserGroup group,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        // 软删除推进内存双版本;子表影子列由数据库复合外键级联同步
        group.MarkDeleted();
        var affected = await UpdateParentAsync(
            group,
            expectedOptimisticVersion,
            expectedConcurrencyVersion,
            cancellationToken);
        EnsureSingleRowAffected(affected, group, "删除");
    }

    /// <inheritdoc/>
    public async Task RestoreAsync(
        UserGroup group,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.Restore();
        var affected = await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(group))
            .Where(t => t.Id == group.Id
                && t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);
        EnsureSingleRowAffected(affected, group, "恢复");
    }

    private async Task<int> UpdateParentAsync(
        UserGroup group,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(group))
            .Where(t => t.Id == group.Id
                && !t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);

    /// <summary>按聚合内成员关系集与库内关系做 diff:新增插入,已移除软删。软删集不按父级影子过滤,保证父级墓碑删除时级联行也被软删(§29A.3)。</summary>
    private static async Task SyncMembershipsAsync(ISqlSugarClient sugar, UserGroup group, CancellationToken cancellationToken)
    {
        var activeRows = await sugar.Queryable<UserGroupMembershipTable>()
            .Where(t => t.UserGroupId == group.Id && !t.IsDeleted)
            .ToListAsync(cancellationToken);
        var existingActiveIds = activeRows.Select(r => r.Id).ToHashSet();

        var aggregateActive = group.Memberships.Where(m => !m.IsDeleted).ToList();
        var aggregateIds = aggregateActive.Select(m => m.Id).ToHashSet();

        foreach (var membership in aggregateActive.Where(m => !existingActiveIds.Contains(m.Id) && !m.UserIsDeleted))
        {
            await sugar.Insertable(TableMapper.ToTable(membership)).ExecuteCommandAsync(cancellationToken);
        }

        foreach (var row in activeRows.Where(r => !aggregateIds.Contains(r.Id)))
        {
            await sugar.Updateable<UserGroupMembershipTable>()
                .SetColumns(t => new UserGroupMembershipTable { IsDeleted = true })
                .Where(t => t.Id == row.Id && !t.IsDeleted)
                .ExecuteCommandAsync(cancellationToken);
        }
    }

    /// <summary>按聚合内组角色关系集与库内关系做 diff:新增插入,已解除软删。软删集不按父级影子过滤,保证父级墓碑删除时级联行也被软删(§29A.3)。</summary>
    private static async Task SyncRolesAsync(ISqlSugarClient sugar, UserGroup group, CancellationToken cancellationToken)
    {
        var activeRows = await sugar.Queryable<UserGroupRoleTable>()
            .Where(t => t.UserGroupId == group.Id && !t.IsDeleted)
            .ToListAsync(cancellationToken);
        var existingActiveIds = activeRows.Select(r => r.Id).ToHashSet();

        var aggregateActive = group.Roles.Where(r => !r.IsDeleted).ToList();
        var aggregateIds = aggregateActive.Select(r => r.Id).ToHashSet();

        foreach (var role in aggregateActive.Where(r => !existingActiveIds.Contains(r.Id) && !r.RoleIsDeleted))
        {
            await sugar.Insertable(TableMapper.ToTable(role)).ExecuteCommandAsync(cancellationToken);
        }

        foreach (var row in activeRows.Where(r => !aggregateIds.Contains(r.Id)))
        {
            await sugar.Updateable<UserGroupRoleTable>()
                .SetColumns(t => new UserGroupRoleTable { IsDeleted = true })
                .Where(t => t.Id == row.Id && !t.IsDeleted)
                .ExecuteCommandAsync(cancellationToken);
        }
    }

    private static void EnsureSingleRowAffected(int affected, UserGroup group, string operation)
    {
        if (affected != 1)
        {
            throw new ConcurrencyException($"实体 {group.EntityType} {operation}失败:并发版本不匹配或记录不存在。");
        }
    }
}
