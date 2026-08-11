using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.SharedKernel.Interfaces;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// 权限聚合仓储。权限无子项,额外提供按业务标识查询与全量查询。
/// </summary>
public interface IPermissionRepository : IRepository<Permission>
{
    /// <summary>按业务标识查询(大小写不敏感),未找到返回 null。</summary>
    Task<Permission?> GetByNIdAsync(string nId, CancellationToken cancellationToken = default);

    /// <summary>查询全部未删除权限,按规范化业务标识排序。</summary>
    Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken cancellationToken = default);
}
