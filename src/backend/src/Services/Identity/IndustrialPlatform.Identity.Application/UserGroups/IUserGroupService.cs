namespace IndustrialPlatform.Identity.Application.UserGroups;

/// <summary>
/// 用户组管理用例(§29A.2/§29A.6):创建、资料更新、启停、最终成员集与最终角色集。
/// 成员/组角色/禁用变化同步推进受影响用户授权版本、失效权限缓存并撤销会话,
/// 集成事件经 Outbox 与组写入同事务提交;敏感字段绝不进入审计与日志。
/// </summary>
public interface IUserGroupService
{
    /// <summary>创建用户组(可原子提交初始成员与角色)。</summary>
    Task<UserGroupSummary> CreateAsync(
        string tenantNId,
        string actorUserNId,
        CreateUserGroupCommand command,
        CancellationToken cancellationToken);

    /// <summary>修改用户组名称/描述(NId 不可修改)。</summary>
    Task<UserGroupSummary> UpdateAsync(
        string tenantNId,
        string actorUserNId,
        string groupNId,
        UpdateUserGroupCommand command,
        CancellationToken cancellationToken);

    /// <summary>启用/禁用用户组;禁用时按权威计数守卫最后系统管理员并失效全部成员授权。</summary>
    Task<UserGroupSummary> SetStatusAsync(
        string tenantNId,
        string actorUserNId,
        string groupNId,
        bool enabled,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken);

    /// <summary>设置最终成员集(幂等收敛),被移除成员不得剥夺租户最后一名系统管理员。</summary>
    Task<UserGroupSummary> SetMembersAsync(
        string tenantNId,
        string actorUserNId,
        string groupNId,
        IReadOnlyCollection<string> memberUserNIds,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken);

    /// <summary>设置最终组角色集(幂等收敛,不接受权限),移除系统角色按权威计数守卫。</summary>
    Task<UserGroupSummary> SetRolesAsync(
        string tenantNId,
        string actorUserNId,
        string groupNId,
        IReadOnlyCollection<string> roleNIds,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken);

    /// <summary>按业务标识查询用户组(含成员/角色 NId 与双版本);不存在或跨租户抛 404。</summary>
    Task<UserGroupSummary> GetAsync(string tenantNId, string groupNId, CancellationToken cancellationToken);
}
