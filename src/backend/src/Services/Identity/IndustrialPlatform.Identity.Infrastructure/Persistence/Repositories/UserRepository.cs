using IndustrialPlatform.Identity.Domain.Users;
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

    /// <inheritdoc/>
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(t => t.Id == id && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        // 活动角色关系:子表自身未删除 且 父级影子列未删除
        var roleRows = await _dbContext.SqlSugar.Queryable<UserRoleTable>()
            .Where(t => t.UserId == id && !t.IsDeleted && !t.UserIsDeleted)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);
        return TableMapper.ToUser(row, roleRows.Select(TableMapper.ToUserRole).ToList());
    }

    /// <inheritdoc/>
    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
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

            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(
        User user,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
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
    public async Task RestoreAsync(
        User user,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        user.Restore();
        var affected = await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(user))
            .Where(t => t.Id == user.Id
                && t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);
        EnsureSingleRowAffected(affected, user, "恢复");
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

    /// <summary>按聚合内关系集与库内活动关系做 diff:新增插入,已解除软删。</summary>
    private static async Task SyncUserRolesAsync(ISqlSugarClient sugar, User user, CancellationToken cancellationToken)
    {
        var activeRows = await sugar.Queryable<UserRoleTable>()
            .Where(t => t.UserId == user.Id && !t.IsDeleted && !t.UserIsDeleted)
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

    private static void EnsureSingleRowAffected(int affected, User user, string operation)
    {
        if (affected != 1)
        {
            throw new ConcurrencyException($"实体 {user.EntityType} {operation}失败:并发版本不匹配或记录不存在。");
        }
    }
}
