namespace IndustrialPlatform.SharedKernel.Interfaces;

/// <summary>
/// 工作单元接口,负责事务提交。
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
