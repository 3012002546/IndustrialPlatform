namespace IndustrialPlatform.Identity.Contracts.Management;

/// <summary>
/// 用户组列表过滤(§29A.5,GET /api/v1/user-groups)。属性可空;租户由令牌解析。
/// </summary>
public sealed record UserGroupListFilter(
    string? Name,
    string? Status,
    int PageIndex,
    int PageSize);

/// <summary>
/// 创建用户组请求(§29A.5,POST /api/v1/user-groups)。成员与角色可原子提交;
/// NId 为空时由服务端生成。
/// </summary>
public sealed record CreateUserGroupRequest(
    string? NId,
    string? Name,
    string? Description,
    IReadOnlyList<string>? MemberUserNIds,
    IReadOnlyList<string>? RoleNIds);

/// <summary>
/// 更新用户组资料请求(§29A.5,PUT /api/v1/user-groups/{groupNId})。NId 创建后不可修改。
/// </summary>
public sealed record UpdateUserGroupRequest(
    string? Name,
    string? Description,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>
/// 启用/禁用用户组请求(§29A.5,PUT /api/v1/user-groups/{groupNId}/status)。
/// 禁用时失效全部成员授权并按权威计数守卫最后系统管理员。
/// </summary>
public sealed record SetUserGroupStatusRequest(
    bool Enabled,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>
/// 设置用户组成员请求(§29A.5,PUT /api/v1/user-groups/{groupNId}/members)。
/// memberUserNIds 为最终成员集(幂等收敛)。
/// </summary>
public sealed record SetUserGroupMembersRequest(
    IReadOnlyList<string>? MemberUserNIds,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>
/// 设置用户组角色请求(§29A.5,PUT /api/v1/user-groups/{groupNId}/roles)。
/// roleNIds 为最终角色集(幂等收敛,不接受权限)。
/// </summary>
public sealed record SetUserGroupRolesRequest(
    IReadOnlyList<string>? RoleNIds,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>用户组列表项(§29A.5):含成员数与角色数,双版本供管理回传;isDeleted 标识墓碑(includeDeleted 列表用于恢复)。</summary>
public sealed record UserGroupSummaryDto(
    string GroupNId,
    string Name,
    string? Description,
    string Status,
    int MemberCount,
    int RoleCount,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    bool IsDeleted);

/// <summary>用户组详情(§29A.5):含成员与角色 NId 全量。</summary>
public sealed record UserGroupDetailDto(
    string GroupNId,
    string Name,
    string? Description,
    string Status,
    string TenantNId,
    IReadOnlyList<string> MemberUserNIds,
    IReadOnlyList<string> RoleNIds,
    long OptimisticVersion,
    Guid ConcurrencyVersion);
