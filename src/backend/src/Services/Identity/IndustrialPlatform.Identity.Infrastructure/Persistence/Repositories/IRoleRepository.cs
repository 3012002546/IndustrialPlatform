using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.SharedKernel.Interfaces;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// 角色聚合仓储。GetByIdAsync 载入活动权限关联(双重过滤:自身与父级影子列均未删除);
/// 认证用例按角色集查询 NId 与活动权限。
/// </summary>
public interface IRoleRepository : IRepository<Role>
{
    /// <summary>按角色 Id 集查询活动角色的 NId 映射(不存在的角色不包含)。</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default);

    /// <summary>按角色 Id 集查询活动且启用的权限(跨角色去重)。</summary>
    Task<IReadOnlyList<Permission>> GetActivePermissionsForRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default);

    /// <summary>新增角色,事务内与 Outbox 事件原子提交(§20);outboxEvents 为空时行为同基础接口。</summary>
    Task AddAsync(Role role, IReadOnlyCollection<OutboxEnvelope>? outboxEvents, CancellationToken cancellationToken = default);

    /// <summary>按双版本原子更新角色与权限关系 diff,事务内与 Outbox 事件原子提交(§20);outboxEvents 为空时行为同基础接口。</summary>
    Task UpdateAsync(Role role, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, IReadOnlyCollection<OutboxEnvelope>? outboxEvents, CancellationToken cancellationToken = default);
}
