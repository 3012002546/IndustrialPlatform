using IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// 权限聚合仓储实现:POCO ↔ 聚合双向映射、软删除过滤与双版本并发原子更新。
/// 权限无子项,父级影子列由数据库复合外键 ON UPDATE CASCADE 同步。
/// </summary>
public sealed class PermissionRepository : IPermissionRepository
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化权限仓储。</summary>
    public PermissionRepository(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<Permission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SqlSugar.Queryable<PermissionTable>()
            .Where(t => t.Id == id && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToPermission(row);
    }

    /// <inheritdoc/>
    public async Task<Permission?> GetByNIdAsync(string nId, CancellationToken cancellationToken = default)
    {
        var normalized = NId.Create(nId).Normalized;
        var row = await _dbContext.SqlSugar.Queryable<PermissionTable>()
            .Where(t => t.NormalizedNId == normalized && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToPermission(row);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.SqlSugar.Queryable<PermissionTable>()
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.NormalizedNId)
            .ToListAsync(cancellationToken);
        return rows.Select(TableMapper.ToPermission).ToList();
    }

    /// <inheritdoc/>
    public async Task AddAsync(Permission permission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permission);
        await _dbContext.SqlSugar.Insertable(TableMapper.ToTable(permission)).ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(
        Permission permission,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permission);

        var affected = await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(permission))
            .Where(t => t.Id == permission.Id
                && !t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);
        EnsureSingleRowAffected(affected, permission, "更新");
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        Permission permission,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permission);

        // 软删除推进内存双版本;子表影子列由数据库复合外键级联同步
        permission.MarkDeleted();
        var affected = await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(permission))
            .Where(t => t.Id == permission.Id
                && !t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);
        EnsureSingleRowAffected(affected, permission, "删除");
    }

    /// <inheritdoc/>
    public async Task RestoreAsync(
        Permission permission,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permission);

        permission.Restore();
        var affected = await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(permission))
            .Where(t => t.Id == permission.Id
                && t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);
        EnsureSingleRowAffected(affected, permission, "恢复");
    }

    private static void EnsureSingleRowAffected(int affected, Permission permission, string operation)
    {
        if (affected != 1)
        {
            throw new ConcurrencyException($"实体 {permission.EntityType} {operation}失败:并发版本不匹配或记录不存在。");
        }
    }
}
