using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.SharedKernel.Interfaces;

/// <summary>
/// 聚合仓储接口,由各服务领域层扩展或由 Infrastructure 提供实现。
/// </summary>
/// <typeparam name="TEntity">聚合实体类型。</typeparam>
public interface IRepository<TEntity>
    where TEntity : Entity
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
}
