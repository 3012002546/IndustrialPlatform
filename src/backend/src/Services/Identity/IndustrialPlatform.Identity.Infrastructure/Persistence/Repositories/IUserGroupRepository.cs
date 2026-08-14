using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.SharedKernel.Interfaces;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// 用户组聚合仓储。GetByIdAsync/GetByNIdAsync 载入活动成员与组角色关系
/// (双重过滤:自身与父级影子列均未删除);认证/授权用例按用户查询组继承角色。
/// </summary>
public interface IUserGroupRepository : IRepository<UserGroup>
{
    /// <summary>按用户组业务标识查询聚合(含活动成员与组角色关系);不存在返回 <c>null</c>。</summary>
    Task<UserGroup?> GetByNIdAsync(string groupNId, CancellationToken cancellationToken = default);

    /// <summary>查询用户在租户内经活动用户组继承的角色 Id 集(组未删除且 Active、关系双重过滤)。</summary>
    Task<IReadOnlyList<Guid>> GetRoleIdsForUserAsync(
        Guid userId,
        string tenantNId,
        CancellationToken cancellationToken = default);

    /// <summary>新增用户组,事务内级联活动成员/组角色关系与 Outbox 事件原子提交(§20);outboxEvents 为空时行为同基础接口。</summary>
    Task AddAsync(UserGroup group, IReadOnlyCollection<OutboxEnvelope>? outboxEvents, CancellationToken cancellationToken = default);

    /// <summary>按双版本原子更新用户组与成员/组角色关系 diff,事务内与 Outbox 事件原子提交(§20);outboxEvents 为空时行为同基础接口。</summary>
    Task UpdateAsync(UserGroup group, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, IReadOnlyCollection<OutboxEnvelope>? outboxEvents, CancellationToken cancellationToken = default);
}
