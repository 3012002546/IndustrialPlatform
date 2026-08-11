using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Interfaces;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// 用户聚合仓储。GetByIdAsync 载入活动用户角色关系(双重过滤:自身与父级影子列均未删除)。
/// </summary>
public interface IUserRepository : IRepository<User>
{
}
