using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Interfaces;

namespace IndustrialPlatform.Infrastructure.Repository;

/// <summary>
/// 基于 SqlSugar 的通用仓储实现。
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
        => await _dbContext.SqlSugar.Queryable<TEntity>().InSingleAsync(id);

    /// <inheritdoc/>
    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _dbContext.SqlSugar.Insertable(entity).ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _dbContext.SqlSugar.Updateable(entity).ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _dbContext.SqlSugar.Deleteable(entity).ExecuteCommandAsync(cancellationToken);
    }
}
