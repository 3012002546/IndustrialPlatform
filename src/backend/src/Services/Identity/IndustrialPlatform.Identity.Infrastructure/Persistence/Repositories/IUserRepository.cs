using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Interfaces;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;

/// <summary>
/// 用户聚合仓储。GetByIdAsync 载入活动用户角色关系(双重过滤:自身与父级影子列均未删除);
/// 登录用例按规范化登录名/业务标识查询。
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>按租户与规范化登录名查询用户(含活动角色关系);不存在返回 <c>null</c>。</summary>
    Task<User?> GetByNormalizedLoginNameAsync(string tenantNId, string normalizedLoginName, CancellationToken cancellationToken = default);

    /// <summary>按用户业务标识查询用户(含活动角色关系);不存在返回 <c>null</c>。</summary>
    Task<User?> GetByNIdAsync(string userNId, CancellationToken cancellationToken = default);

    /// <summary>新增用户,事务内与 Outbox 事件原子提交(§20);outboxEvents 为空时行为同基础接口。</summary>
    Task AddAsync(User user, IReadOnlyCollection<OutboxEnvelope>? outboxEvents, CancellationToken cancellationToken = default);

    /// <summary>按双版本原子更新用户与角色关系 diff,事务内与 Outbox 事件原子提交(§20);outboxEvents 为空时行为同基础接口。</summary>
    Task UpdateAsync(User user, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, IReadOnlyCollection<OutboxEnvelope>? outboxEvents, CancellationToken cancellationToken = default);
}
