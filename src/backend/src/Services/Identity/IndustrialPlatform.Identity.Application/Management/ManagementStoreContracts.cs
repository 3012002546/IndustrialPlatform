using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Querying.Descriptors;

namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// 管理用例分页结果(应用层承载,避免 Application 反向引用 BuildingBlocks Web 的 PageResult)。
/// Api 控制器映射为统一 PageResult 信封。
/// </summary>
public sealed record ManagementPage<T>(IReadOnlyList<T> Items, long Total, int PageIndex, int PageSize);

/// <summary>
/// 用户管理查询投影(不含密码哈希等敏感字段,含角色来源摘要与双版本供乐观并发回传,§29A.5)。
/// DirectRoleNIds 为直接分配;GroupRoleNIds 为经活动用户组继承;EffectiveRoleNIds 为两者并集。
/// IsDeleted 标识墓碑(includeDeleted 列表用于恢复操作)。
/// </summary>
public sealed record StoredUser(
    Guid Id,
    string TenantNId,
    string NId,
    string LoginName,
    string Name,
    string? Email,
    string? Phone,
    UserStatus Status,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    DateTimeOffset? LastLoginOn,
    bool MustChangePassword,
    IReadOnlyList<string> DirectRoleNIds,
    IReadOnlyList<string> GroupRoleNIds,
    IReadOnlyList<string> EffectiveRoleNIds,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    bool IsDeleted);

/// <summary>用户查询分页结果。</summary>
public sealed record StoredUserPage(IReadOnlyList<StoredUser> Items, long Total);

/// <summary>Identity Users 受控查询端口;基础租户/软删除范围由实现先行施加。</summary>
public interface IUserQueryStore
{
    Task<StoredUserPage> QueryUsersAsync(
        string tenantNId,
        QueryDescriptor descriptor,
        CancellationToken cancellationToken);
}

/// <summary>角色管理查询投影(含权限 NId 与双版本)。</summary>
public sealed record StoredRole(
    Guid Id,
    string TenantNId,
    string NId,
    string Name,
    string? Description,
    bool IsSystem,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    IReadOnlyList<string> PermissionNIds);

/// <summary>角色查询分页结果。</summary>
public sealed record StoredRolePage(IReadOnlyList<StoredRole> Items, long Total);

/// <summary>用户列表过滤(§16.1/§29A.5)。租户隔离在 SQL 层实施;NId/LoginName/Name 为包含匹配,Status 可选;GroupNId/RoleNId 过滤有效成员/直接角色;IncludeDeleted 为 true 时同时返回墓碑(§29A.3)。</summary>
public sealed record UserListFilter(
    string TenantNId,
    string? NId,
    string? LoginName,
    string? Name,
    UserStatus? Status,
    int PageIndex,
    int PageSize,
    bool IncludeDeleted = false,
    string? GroupNId = null,
    string? RoleNId = null,
    string? SortField = null,
    string? SortOrder = null,
    string? Keyword = null,
    string? Email = null,
    string? Phone = null,
    bool? MustChangePassword = null,
    DateTimeOffset? LastLoginFrom = null,
    DateTimeOffset? LastLoginTo = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null);

/// <summary>用户组管理查询投影(§29A.5):含成员数与角色数,双版本供乐观并发回传;IsDeleted 标识墓碑。</summary>
public sealed record StoredUserGroup(
    string NId,
    string Name,
    string? Description,
    string Status,
    string TenantNId,
    int MemberCount,
    int RoleCount,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    bool IsDeleted);

/// <summary>用户组查询分页结果。</summary>
public sealed record StoredUserGroupPage(IReadOnlyList<StoredUserGroup> Items, long Total);

/// <summary>角色列表过滤(§16.2)。租户隔离在 SQL 层实施;NId/Name 为包含匹配。</summary>
public sealed record RoleListFilter(string TenantNId, string? NId, string? Name, int PageIndex, int PageSize, string? SortField = null, string? SortOrder = null, string? Keyword = null, string? Description = null, bool? IsSystem = null);

/// <summary>登录审计查询过滤(§16.3)。租户隔离在 SQL 层实施;UserNId 精确匹配,Success 可选过滤。</summary>
public sealed record LoginAuditFilter(string TenantNId, string? UserNId, bool? Success, int PageIndex, int PageSize, string? SortField = null, string? SortOrder = null, string? Keyword = null, string? LoginNameSnapshot = null, string? FailureCode = null, string? IpAddressHash = null, string? UserAgentHash = null, string? TraceId = null, DateTimeOffset? OccurredFrom = null, DateTimeOffset? OccurredTo = null);

/// <summary>登录审计查询投影(只含哈希摘要,不含原始 IP/User-Agent)。</summary>
public sealed record LoginAuditRow(
    string TenantNId,
    string? UserNId,
    string LoginNameSnapshot,
    bool Success,
    string? FailureCode,
    string IpAddressHash,
    string UserAgentHash,
    string TraceId,
    DateTimeOffset OccurredOn);

/// <summary>登录审计查询分页结果。</summary>
public sealed record LoginAuditPage(IReadOnlyList<LoginAuditRow> Items, long Total);

/// <summary>
/// 管理用例持久化端口(§16):用户/角色/权限的查询、冲突检查与写操作。
/// 组合领域仓储实现;所有按 NId 查询不区分租户,租户隔离由应用层显式校验,
/// 跨租户返回 <c>null</c> 后由应用层映射为 404。
/// </summary>
public interface IManagementStore
{
    /// <summary>分页查询用户(含活动角色 NId),按创建时间倒序。</summary>
    Task<StoredUserPage> QueryUsersAsync(UserListFilter filter, CancellationToken cancellationToken);

    /// <summary>按业务标识查询用户投影(含活动角色 NId);不存在返回 <c>null</c>。</summary>
    Task<StoredUser?> GetUserAsync(string userNId, CancellationToken cancellationToken);

    /// <summary>按业务标识装载完整用户聚合(含活动角色关系),供写操作;不存在返回 <c>null</c>。</summary>
    Task<User?> GetUserAggregateAsync(string userNId, CancellationToken cancellationToken);

    /// <summary>按业务标识装载用户墓碑聚合(含已软删除记录,§29A.3),供恢复操作;不存在返回 <c>null</c>。</summary>
    Task<User?> GetUserAggregateIncludingDeletedAsync(string userNId, CancellationToken cancellationToken);

    /// <summary>业务标识是否已存在(含软删除,§8 NId 全历史唯一且删除后不复用)。</summary>
    Task<bool> UserExistsByNIdAsync(string userNId, CancellationToken cancellationToken);

    /// <summary>角色业务标识是否已存在(含软删除,§9.1 NId 创建后不可变且不复用)。</summary>
    Task<bool> RoleExistsByNIdAsync(string roleNId, CancellationToken cancellationToken);

    /// <summary>登录名在租户内是否已被活动用户占用(软删除可复用,§8 活动记录唯一)。</summary>
    Task<bool> LoginNameExistsAsync(string tenantNId, string loginName, CancellationToken cancellationToken);

    /// <summary>分页查询角色(含权限 NId),按创建时间倒序。</summary>
    Task<StoredRolePage> QueryRolesAsync(RoleListFilter filter, CancellationToken cancellationToken);

    /// <summary>按业务标识查询角色投影(含权限 NId);不存在返回 <c>null</c>。</summary>
    Task<StoredRole?> GetRoleAsync(string roleNId, CancellationToken cancellationToken);

    /// <summary>按业务标识装载完整角色聚合(含活动权限关系),供写操作;不存在返回 <c>null</c>。</summary>
    Task<Role?> GetRoleAggregateAsync(string roleNId, CancellationToken cancellationToken);

    /// <summary>按业务标识装载一批角色聚合(含活动权限关系);找不到或已删除的角色不包含。</summary>
    Task<IReadOnlyList<Role>> GetRolesByNIdsAsync(IReadOnlyCollection<string> roleNIds, CancellationToken cancellationToken);

    /// <summary>按数据库主键装载一批角色聚合(含活动权限关系);找不到或已删除的角色不包含。</summary>
    Task<IReadOnlyList<Role>> GetRolesByIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);

    /// <summary>按角色主键集查询活动角色 NId 映射。</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetRoleNIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);

    /// <summary>按业务标识装载一批权限聚合;找不到或已删除的权限不包含。</summary>
    Task<IReadOnlyList<Permission>> GetPermissionsByNIdsAsync(IReadOnlyCollection<string> permissionNIds, CancellationToken cancellationToken);

    /// <summary>按数据库主键装载一批权限聚合;找不到或已删除的权限不包含。</summary>
    Task<IReadOnlyList<Permission>> GetPermissionsByIdsAsync(IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken);

    /// <summary>查询全部未删除权限,按规范化业务标识排序(权限目录树)。</summary>
    Task<IReadOnlyList<Permission>> GetAllPermissionsAsync(CancellationToken cancellationToken);

    /// <summary>统计角色在租户内的活动持有者数(用户与角色均未删除、关系有效)。</summary>
    Task<int> CountActiveRoleHoldersAsync(Guid roleId, string tenantNId, CancellationToken cancellationToken);

    /// <summary>查询持有角色的活动用户业务标识集(用于角色权限变化后的缓存失效)。</summary>
    Task<IReadOnlyList<string>> GetUserNIdsForRoleAsync(Guid roleId, string tenantNId, CancellationToken cancellationToken);

    /// <summary>新增用户(事务内级联活动角色关系,并与 Outbox 事件同事务提交)。</summary>
    Task AddUserAsync(User user, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新用户与角色关系 diff(并与 Outbox 事件同事务提交);冲突抛并发异常。</summary>
    Task UpdateUserAsync(User user, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken);

    /// <summary>按双版本原子恢复用户墓碑(并与 Outbox 事件同事务提交,§29A.3);冲突抛并发异常。</summary>
    Task RestoreUserAsync(User user, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken);

    /// <summary>新增角色(事务内级联活动权限关系,并与 Outbox 事件同事务提交)。</summary>
    Task AddRoleAsync(Role role, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新角色与权限关系 diff(并与 Outbox 事件同事务提交);冲突抛并发异常。</summary>
    Task UpdateRoleAsync(Role role, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken);
}

/// <summary>登录审计查询端口(只读,§19.1/§16.3)。</summary>
public interface ILoginAuditQueryStore
{
    /// <summary>按租户分页查询登录审计,按发生时间倒序。</summary>
    Task<LoginAuditPage> QueryAsync(LoginAuditFilter filter, CancellationToken cancellationToken);
}
