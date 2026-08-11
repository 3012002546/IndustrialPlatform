using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.SharedKernel.Interfaces;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// 角色聚合仓储。GetByIdAsync 载入活动权限关联(双重过滤:自身与父级影子列均未删除)。
/// </summary>
public interface IRoleRepository : IRepository<Role>
{
}
