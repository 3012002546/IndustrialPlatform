namespace IndustrialPlatform.Identity.Contracts.Management;

/// <summary>
/// 安全删除用户组请求(§29A.3/§29A.5,DELETE /user-groups/{groupNId})。
/// 软删除并解除有效成员/组角色关系;最后系统管理员守卫由应用层按权威计数执行。
/// </summary>
public sealed record DeleteUserGroupRequest(
    string? Reason,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>
/// 恢复用户组墓碑请求(§29A.3/§29A.5,POST /user-groups/{groupNId}/restore)。
/// 仅恢复为 Disabled,不自动恢复成员/角色关系。
/// </summary>
public sealed record RestoreUserGroupRequest(
    string? Reason,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);
