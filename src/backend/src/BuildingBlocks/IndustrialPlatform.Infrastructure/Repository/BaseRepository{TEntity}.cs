using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SharedKernel.Interfaces;

namespace IndustrialPlatform.Infrastructure.Repository;

/// <summary>
/// 基于 SqlSugar 的通用仓储实现,提供软删除过滤与双版本并发安全的原子更新。
/// </summary>
/// <typeparam name="TEntity">聚合实体类型,需具备公共无参构造函数以支持 SqlSugar 实体实例化。</typeparam>
public sealed class BaseRepository<TEntity> : IRepository<TEntity>
    where TEntity : Entity, new()
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>
    /// 创建通用仓储。
    /// </summary>
    /// <param name="dbContext">数据库上下文。</param>
    public BaseRepository(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _dbContext.SqlSugar
            .Queryable<TEntity>()
            .Where(it => it.Id == id && !it.IsDeleted)
            .FirstAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _dbContext.SqlSugar.Insertable(entity).ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(
        TEntity entity,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var affected = await _dbContext.SqlSugar
            .Updateable(entity)
            .Where(it => it.Id == entity.Id
                && !it.IsDeleted
                && it.OptimisticVersion == expectedOptimisticVersion
                && it.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);

        EnsureSingleRowAffected(affected, entity, "更新");
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        TEntity entity,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.MarkDeleted();

        var affected = await _dbContext.SqlSugar
            .Updateable(entity)
            .Where(it => it.Id == entity.Id
                && !it.IsDeleted
                && it.OptimisticVersion == expectedOptimisticVersion
                && it.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);

        EnsureSingleRowAffected(affected, entity, "删除");
    }

    /// <inheritdoc/>
    public async Task RestoreAsync(
        TEntity entity,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.Restore();

        var affected = await _dbContext.SqlSugar
            .Updateable(entity)
            .Where(it => it.Id == entity.Id
                && it.IsDeleted
                && it.OptimisticVersion == expectedOptimisticVersion
                && it.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);

        EnsureSingleRowAffected(affected, entity, "恢复");
    }

    private static void EnsureSingleRowAffected(int affected, TEntity entity, string operation)
    {
        if (affected != 1)
        {
            throw new ConcurrencyException($"实体 {entity.EntityType} {operation}失败:并发版本不匹配或记录不存在。");
        }
    }
}
