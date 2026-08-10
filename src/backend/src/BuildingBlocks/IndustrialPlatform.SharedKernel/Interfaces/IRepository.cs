using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.SharedKernel.Interfaces;

/// <summary>
/// 聚合仓储接口,由各服务领域层扩展或由 Infrastructure 提供实现。
/// 更新、删除与恢复接收调用方读取实体时保存的两个原始版本值,冲突抛出
/// <see cref="IndustrialPlatform.SharedKernel.Exceptions.ConcurrencyException"/>。
/// </summary>
/// <typeparam name="TEntity">聚合实体类型。</typeparam>
public interface IRepository<TEntity>
    where TEntity : Entity
{
    /// <summary>按主键查询,默认排除已软删除记录。</summary>
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>按双版本原子更新;冲突抛出并发异常。</summary>
    Task UpdateAsync(
        TEntity entity,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);

    /// <summary>按双版本原子软删除;冲突抛出并发异常。</summary>
    Task DeleteAsync(
        TEntity entity,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);

    /// <summary>按双版本原子恢复软删除实体;冲突抛出并发异常。</summary>
    Task RestoreAsync(
        TEntity entity,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken = default);
}
