using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.Identity.Domain.Users;

namespace IndustrialPlatform.Identity.Application.UserGroups;

/// <summary>
/// 用户组创建命令(§29A.2)。NId 为空时由服务生成;成员与角色可在创建时原子提交。
/// </summary>
public sealed record CreateUserGroupCommand(
    string? NId,
    string Name,
    string? Description,
    IReadOnlyCollection<string> MemberUserNIds,
    IReadOnlyCollection<string> RoleNIds);

/// <summary>用户组资料更新命令(§29A.2)。NId 创建后不可修改,携带双版本供乐观并发。</summary>
public sealed record UpdateUserGroupCommand(
    string Name,
    string? Description,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>用户组查询投影(§29A.2):含成员/角色 NId 与双版本供管理回传。</summary>
public sealed record UserGroupSummary(
    string NId,
    string Name,
    string? Description,
    string Status,
    string TenantNId,
    IReadOnlyList<string> MemberUserNIds,
    IReadOnlyList<string> RoleNIds,
    long OptimisticVersion,
    Guid ConcurrencyVersion);

/// <summary>用户组列表过滤(§29A.5):租户隔离在 SQL 层实施;Name 包含匹配,Status 可选(Active/Disabled)。</summary>
public sealed record UserGroupListQuery(
    string TenantNId,
    string? Name,
    UserGroupStatus? Status,
    int PageIndex,
    int PageSize,
    bool IncludeDeleted = false,
    string? SortField = null,
    string? SortOrder = null,
    string? NId = null,
    string? Description = null,
    string? Keyword = null);

/// <summary>
/// 用户组持久化端口(§29A.2/§29A.6):聚合装载、唯一性检查、原子写与授权求值辅助查询。
/// 按 NId 查询不区分租户,租户隔离由应用层显式校验,跨租户返回 <c>null</c> 后映射 404。
/// </summary>
public interface IUserGroupStore
{
    /// <summary>按业务标识装载完整用户组聚合(含活动成员与组角色关系);不存在返回 <c>null</c>。</summary>
    Task<UserGroup?> GetUserGroupAggregateAsync(string groupNId, CancellationToken cancellationToken);

    /// <summary>按租户分页查询用户组(含成员数/角色数,§29A.5),按创建时间倒序。</summary>
    Task<StoredUserGroupPage> QueryUserGroupsAsync(UserGroupListQuery query, CancellationToken cancellationToken);

    /// <summary>按业务标识装载用户组墓碑聚合(含已软删除记录,§29A.3),供恢复操作;不存在返回 <c>null</c>。</summary>
    Task<UserGroup?> GetUserGroupAggregateIncludingDeletedAsync(string groupNId, CancellationToken cancellationToken);

    /// <summary>用户组业务标识是否已存在(含软删除,§29A.2 NId 全历史唯一且不复用)。</summary>
    Task<bool> UserGroupExistsByNIdAsync(string groupNId, CancellationToken cancellationToken);

    /// <summary>按业务标识装载一批用户聚合(仅未删除);找不到的用户不包含,数量不足由调用方判定无效。</summary>
    Task<IReadOnlyList<User>> GetUsersByNIdsAsync(IReadOnlyCollection<string> userNIds, CancellationToken cancellationToken);

    /// <summary>新增用户组,事务内级联活动成员/组角色关系与 Outbox 事件原子提交(§20)。</summary>
    Task AddAsync(UserGroup group, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新用户组与成员/组角色关系 diff,事务内与 Outbox 事件原子提交(§20);冲突抛并发异常。</summary>
    Task UpdateAsync(
        UserGroup group,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        IReadOnlyCollection<OutboxEnvelope> outboxEvents,
        CancellationToken cancellationToken);

    /// <summary>按双版本原子恢复用户组墓碑(§29A.3);冲突抛并发异常。</summary>
    Task RestoreAsync(
        UserGroup group,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken);

    /// <summary>装载用户组活动成员用户聚合(成员关系与两端父级均未删除,租户已校验)。</summary>
    Task<IReadOnlyList<User>> GetMembersAsync(Guid groupId, string tenantNId, CancellationToken cancellationToken);

    /// <summary>查询用户组活动成员的用户业务标识集(按 NId 排序)。</summary>
    Task<IReadOnlyList<string>> GetMemberUserNIdsAsync(Guid groupId, string tenantNId, CancellationToken cancellationToken);

    /// <summary>查询用户在租户内经活动用户组继承的角色 Id 集(认证/授权求值,§29A.2)。</summary>
    Task<IReadOnlyList<Guid>> GetRoleIdsForUserAsync(Guid userId, string tenantNId, CancellationToken cancellationToken);

    /// <summary>用户是否在本组之外持有指定角色(直接分配 或 其他活动用户组继承)。</summary>
    Task<bool> UserHoldsRoleOutsideGroupAsync(
        Guid userId,
        Guid groupId,
        Guid roleId,
        string tenantNId,
        CancellationToken cancellationToken);

    /// <summary>是否存在仅经本组持有指定角色的活动成员(用于最后系统管理员守卫,§29A.3)。</summary>
    Task<bool> AnyMemberHoldsRoleOnlyViaGroupAsync(
        Guid groupId,
        Guid roleId,
        string tenantNId,
        CancellationToken cancellationToken);
}
