using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Outbox;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Exceptions;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// 用户聚合仓储实现:POCO ↔ 聚合双向映射、软删除过滤、双版本并发原子更新与用户角色关系 diff 同步。
/// 父级软删除/恢复由数据库复合外键 ON UPDATE CASCADE 同步子表影子列。
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化用户仓储。</summary>
    public UserRepository(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <summary>按需投影权威授权事实；不载入用户聚合、不缓存角色或权限。</summary>
    internal async Task<AuthorizationSnapshot?> GetAuthorizationSnapshotAsync(string tenantNId, string userNId, CancellationToken ct)
    {
        var normalized = NId.Create(userNId).Normalized;
        var user = await _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(u => u.NormalizedNId == normalized && u.TenantNId == tenantNId && !u.IsDeleted)
            .Select(u => new { u.Id, u.NId, u.TenantNId, u.Status, u.AuthVersion }).FirstAsync(ct);
        if (user is null) return null;
        // EXISTS合并直接/组继承角色；LEFT JOIN保留没有权限的有效角色（含工厂范围角色）。
        var facts = await _dbContext.SqlSugar.Queryable<RoleTable>()
            .LeftJoin<RolePermissionTable>((r, rp) => rp.RoleId == r.Id && !rp.IsDeleted && !rp.RoleIsDeleted)
            .LeftJoin<PermissionTable>((r, rp, p) => p.Id == rp.PermissionId && !p.IsDeleted && p.Status == PermissionStatus.Active)
            .Where((r, rp, p) => r.TenantNId == tenantNId && !r.IsDeleted &&
                (SqlFunc.Subqueryable<UserRoleTable>().Where(ur => ur.UserId == user.Id && ur.RoleId == r.Id && !ur.IsDeleted && !ur.UserIsDeleted).Any()
                 || SqlFunc.Subqueryable<UserGroupMembershipTable>()
                    .InnerJoin<UserGroupTable>((m, g) => m.UserGroupId == g.Id && g.TenantNId == tenantNId && !g.IsDeleted && g.Status == UserGroupStatus.Active)
                    .InnerJoin<UserGroupRoleTable>((m, g, gr) => gr.UserGroupId == m.UserGroupId && gr.RoleId == r.Id && !gr.IsDeleted && !gr.UserGroupIsDeleted && !gr.RoleIsDeleted)
                    .Where((m, g, gr) => m.UserId == user.Id && m.TenantNId == tenantNId && !m.IsDeleted && !m.UserIsDeleted && !m.UserGroupIsDeleted).Any()))
            .Select((r, rp, p) => new { RoleNId = r.NId, r.IsSystem, PermissionNId = p.NId })
            .ToListAsync(ct);
        return new(user.TenantNId, user.NId, user.Status, user.AuthVersion,
            facts.Select(f => f.PermissionNId).Where(p => !string.IsNullOrEmpty(p) && !AuthorizationPermissionPolicy.IsProtectedInitializationPermission(p))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            facts.Any(f => f.IsSystem && f.RoleNId.Equals("SYSTEM_ADMIN", StringComparison.OrdinalIgnoreCase)))
        {
            RoleNIds = facts.Select(f => f.RoleNId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
        };
    }

    /// <inheritdoc/>
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(t => t.Id == id && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : await LoadWithRolesAsync(row, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<User?> GetByNormalizedLoginNameAsync(
        string tenantNId,
        string normalizedLoginName,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(t => t.TenantNId == tenantNId && t.NormalizedLoginName == normalizedLoginName && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : await LoadWithRolesAsync(row, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<User?> GetByNIdAsync(string userNId, CancellationToken cancellationToken = default)
    {
        var normalized = NId.Create(userNId).Normalized;
        var row = await _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(t => t.NormalizedNId == normalized && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : await LoadWithRolesAsync(row, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<User?> GetByNIdIncludingDeletedAsync(string userNId, CancellationToken cancellationToken = default)
    {
        var normalized = NId.Create(userNId).Normalized;
        var row = await _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(t => t.NormalizedNId == normalized)
            .FirstAsync(cancellationToken);
        return row is null ? null : await LoadWithRolesAsync(row, cancellationToken);
    }

    /// <summary>载入活动角色关系(子表自身未删除 且 父级影子列未删除)。</summary>
    private async Task<User?> LoadWithRolesAsync(UserTable row, CancellationToken cancellationToken)
    {
        var roleRows = await _dbContext.SqlSugar.Queryable<UserRoleTable>()
            .Where(t => t.UserId == row.Id && !t.IsDeleted && !t.UserIsDeleted)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);
        return TableMapper.ToUser(row, roleRows.Select(TableMapper.ToUserRole).ToList());
    }

    /// <inheritdoc/>
    public Task AddAsync(User user, CancellationToken cancellationToken = default)
        => AddAsync(user, outboxEvents: null, cancellationToken);

    /// <summary>新增用户,事务内级联活动角色关系与 Outbox 事件原子提交(§20);outboxEvents 为空时跳过 Outbox 写入。</summary>
    public async Task AddAsync(User user, IReadOnlyCollection<OutboxEnvelope>? outboxEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await sugar.Insertable(TableMapper.ToTable(user)).ExecuteCommandAsync(cancellationToken);
            foreach (var relation in user.UserRoles.Where(r => !r.IsDeleted))
            {
                await sugar.Insertable(TableMapper.ToTable(relation)).ExecuteCommandAsync(cancellationToken);
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
        User user,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
        => UpdateAsync(user, expectedOptimisticVersion, expectedConcurrencyVersion, outboxEvents: null, cancellationToken);

    /// <summary>按双版本原子更新用户与角色关系 diff,事务内与 Outbox 事件原子提交(§20);冲突抛并发异常;outboxEvents 为空时跳过 Outbox 写入。</summary>
    public async Task UpdateAsync(
        User user,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        IReadOnlyCollection<OutboxEnvelope>? outboxEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            var affected = await UpdateParentAsync(
                user,
                expectedOptimisticVersion,
                expectedConcurrencyVersion,
                cancellationToken);
            EnsureSingleRowAffected(affected, user, "更新");

            await SyncUserRolesAsync(sugar, user, cancellationToken);

            // 墓碑删除(§29A.3):软删该用户的组成员关系,保证恢复后不自动复活授权关系。
            if (user.IsDeleted)
            {
                await SoftDeleteMembershipsForUserAsync(sugar, user.Id, cancellationToken);
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
    public async Task DeleteAsync(
        User user,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        // 软删除推进内存双版本;子表影子列由数据库复合外键级联同步
        user.MarkDeleted();
        var affected = await UpdateParentAsync(
            user,
            expectedOptimisticVersion,
            expectedConcurrencyVersion,
            cancellationToken);
        EnsureSingleRowAffected(affected, user, "删除");
    }

    /// <inheritdoc/>
    public Task RestoreAsync(
        User user,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
        => RestoreAsync(user, expectedOptimisticVersion, expectedConcurrencyVersion, outboxEvents: null, cancellationToken);

    /// <summary>按双版本原子恢复用户墓碑,事务内与 Outbox 事件原子提交(§29A.3);outboxEvents 为空时跳过 Outbox 写入。</summary>
    public async Task RestoreAsync(
        User user,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        IReadOnlyCollection<OutboxEnvelope>? outboxEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            user.Restore();
            var affected = await sugar.Updateable(TableMapper.ToTable(user))
                .Where(t => t.Id == user.Id
                    && t.IsDeleted
                    && t.OptimisticVersion == expectedOptimisticVersion
                    && t.ConcurrencyVersion == expectedConcurrencyVersion)
                .ExecuteCommandAsync(cancellationToken);
            EnsureSingleRowAffected(affected, user, "恢复");

            await OutboxRows.InsertAsync(sugar, outboxEvents, cancellationToken);

            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    private async Task<int> UpdateParentAsync(
        User user,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(user))
            .Where(t => t.Id == user.Id
                && !t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);

    /// <summary>按聚合内关系集与库内关系做 diff:新增插入,已解除软删。软删集不按父级影子过滤,保证父级墓碑删除时级联行也被软删(§29A.3)。</summary>
    private static async Task SyncUserRolesAsync(ISqlSugarClient sugar, User user, CancellationToken cancellationToken)
    {
        var activeRows = await sugar.Queryable<UserRoleTable>()
            .Where(t => t.UserId == user.Id && !t.IsDeleted)
            .ToListAsync(cancellationToken);
        var existingActiveIds = activeRows.Select(r => r.Id).ToHashSet();

        var aggregateActive = user.UserRoles.Where(r => !r.IsDeleted).ToList();
        var aggregateIds = aggregateActive.Select(r => r.Id).ToHashSet();

        foreach (var relation in aggregateActive.Where(r => !existingActiveIds.Contains(r.Id)))
        {
            await sugar.Insertable(TableMapper.ToTable(relation)).ExecuteCommandAsync(cancellationToken);
        }

        foreach (var row in activeRows.Where(r => !aggregateIds.Contains(r.Id)))
        {
            await sugar.Updateable<UserRoleTable>()
                .SetColumns(t => new UserRoleTable { IsDeleted = true })
                .Where(t => t.Id == row.Id && !t.IsDeleted)
                .ExecuteCommandAsync(cancellationToken);
        }
    }

    /// <summary>墓碑删除(§29A.3):软删该用户的全部组成员关系,保证恢复后不自动复活授权关系。
    /// 不按 user_is_deleted 过滤:父级删除的 ON UPDATE CASCADE 已把影子置 1,过滤会漏删。</summary>
    private static async Task SoftDeleteMembershipsForUserAsync(ISqlSugarClient sugar, Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await sugar.Updateable<UserGroupMembershipTable>()
            .SetColumns(t => new UserGroupMembershipTable
            {
                IsDeleted = true,
                LastUpdatedOn = now,
                OptimisticVersion = t.OptimisticVersion + 1,
                ConcurrencyVersion = Guid.NewGuid(),
            })
            .Where(t => t.UserId == userId && !t.IsDeleted)
            .ExecuteCommandAsync(cancellationToken);
    }

    private static void EnsureSingleRowAffected(int affected, User user, string operation)
    {
        if (affected != 1)
        {
            throw new ConcurrencyException($"实体 {user.EntityType} {operation}失败:并发版本不匹配或记录不存在。");
        }
    }
}
