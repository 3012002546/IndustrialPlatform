using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Exceptions;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// 角色聚合仓储实现:POCO ↔ 聚合双向映射、软删除过滤、双版本并发原子更新与权限关联 diff 同步。
/// 父级软删除/恢复由数据库复合外键 ON UPDATE CASCADE 同步子表影子列。
/// </summary>
public sealed class RoleRepository : IRoleRepository
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化角色仓储。</summary>
    public RoleRepository(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SqlSugar.Queryable<RoleTable>()
            .Where(t => t.Id == id && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        // 活动权限关联:子表自身未删除 且 父级影子列未删除
        var permissionRows = await _dbContext.SqlSugar.Queryable<RolePermissionTable>()
            .Where(t => t.RoleId == id && !t.IsDeleted && !t.RoleIsDeleted)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);
        return TableMapper.ToRole(row, permissionRows.Select(TableMapper.ToRolePermission).ToList());
    }

    /// <inheritdoc/>
    public async Task AddAsync(Role role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await sugar.Insertable(TableMapper.ToTable(role)).ExecuteCommandAsync(cancellationToken);
            foreach (var relation in role.Permissions.Where(p => !p.IsDeleted))
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
        Role role,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            var affected = await UpdateParentAsync(
                role,
                expectedOptimisticVersion,
                expectedConcurrencyVersion,
                cancellationToken);
            EnsureSingleRowAffected(affected, role, "更新");

            await SyncPermissionsAsync(sugar, role, cancellationToken);

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
        Role role,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        // 软删除推进内存双版本;子表影子列由数据库复合外键级联同步
        role.MarkDeleted();
        var affected = await UpdateParentAsync(
            role,
            expectedOptimisticVersion,
            expectedConcurrencyVersion,
            cancellationToken);
        EnsureSingleRowAffected(affected, role, "删除");
    }

    /// <inheritdoc/>
    public async Task RestoreAsync(
        Role role,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        role.Restore();
        var affected = await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(role))
            .Where(t => t.Id == role.Id
                && t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);
        EnsureSingleRowAffected(affected, role, "恢复");
    }

    private async Task<int> UpdateParentAsync(
        Role role,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(role))
            .Where(t => t.Id == role.Id
                && !t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);

    /// <summary>按聚合内权限关联集与库内活动关联做 diff:新增插入,已解除软删。</summary>
    private static async Task SyncPermissionsAsync(ISqlSugarClient sugar, Role role, CancellationToken cancellationToken)
    {
        var activeRows = await sugar.Queryable<RolePermissionTable>()
            .Where(t => t.RoleId == role.Id && !t.IsDeleted && !t.RoleIsDeleted)
            .ToListAsync(cancellationToken);
        var existingActiveIds = activeRows.Select(r => r.Id).ToHashSet();

        var aggregateActive = role.Permissions.Where(p => !p.IsDeleted).ToList();
        var aggregateIds = aggregateActive.Select(p => p.Id).ToHashSet();

        foreach (var relation in aggregateActive.Where(p => !existingActiveIds.Contains(p.Id)))
        {
            await sugar.Insertable(TableMapper.ToTable(relation)).ExecuteCommandAsync(cancellationToken);
        }

        foreach (var row in activeRows.Where(r => !aggregateIds.Contains(r.Id)))
        {
            await sugar.Updateable<RolePermissionTable>()
                .SetColumns(t => new RolePermissionTable { IsDeleted = true })
                .Where(t => t.Id == row.Id && !t.IsDeleted)
                .ExecuteCommandAsync(cancellationToken);
        }
    }

    private static void EnsureSingleRowAffected(int affected, Role role, string operation)
    {
        if (affected != 1)
        {
            throw new ConcurrencyException($"实体 {role.EntityType} {operation}失败:并发版本不匹配或记录不存在。");
        }
    }
}
