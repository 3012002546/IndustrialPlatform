namespace IndustrialPlatform.Identity.Contracts.Management;

/// <summary>
/// 创建用户请求(§16.3)。属性可空:空值由应用层校验抛校验异常,
/// 避免 [ApiController] 推断 [Required] 抢占标准 400 ProblemDetails 破坏统一信封。
/// 明文初始密码只在请求内存流转,绝不进入日志、审计或事件。
/// </summary>
public sealed record CreateUserRequest(
    string? NId,
    string? LoginName,
    string? Name,
    string? InitialPassword,
    string? Email,
    string? Phone,
    IReadOnlyList<string>? RoleNIds);

/// <summary>
/// 更新用户请求(§16.3)。登录名修改会推进安全版本并使旧会话失效;
/// 双版本为客户端读取详情时保存的原始值,冲突返回 409 ID_CONCURRENCY_CONFLICT。
/// </summary>
public sealed record UpdateUserRequest(
    string? LoginName,
    string? Name,
    string? Email,
    string? Phone,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>
/// 启用/禁用用户请求(§16.1,PUT /users/{userNId}/status)。
/// 禁用当前登录用户或最后一名系统管理员由应用层业务规则拒绝。
/// </summary>
public sealed record SetUserStatusRequest(
    bool Enabled,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>
/// 分配用户角色请求(§16.3)。roleNIds 为最终目标角色集(幂等收敛),
/// 不在目标集中的现有角色将被解除;最后一名系统管理员保护由应用层执行。
/// </summary>
public sealed record AssignUserRolesRequest(
    IReadOnlyList<string>? RoleNIds,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>
/// 管理员重置密码请求(§16.1,POST /users/{userNId}/reset-password)。
/// 新密码按密码策略校验,只存哈希;成功后撤销该用户全部会话。
/// </summary>
public sealed record ResetPasswordRequest(string? NewPassword);

/// <summary>
/// 安全删除用户请求(§29A.3,DELETE /users/{userNId})。删除为不可物理删除的墓碑:
/// 推进安全版本、撤销全部会话、软删直接角色与组成员关系;UserNId/NormalizedNId/
/// NormalizedLoginName 永久保留不复用。原因与双版本必填,禁止删除自己、内置 ADMIN 与最后一名系统管理员。
/// </summary>
public sealed record DeleteUserRequest(
    string? Reason,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>
/// 恢复用户墓碑请求(§29A.3,POST /users/{userNId}/restore)。仅恢复账号墓碑为 Disabled,
/// 不自动恢复角色/用户组/凭据有效性/会话;恢复后必须显式分配授权、重置密码并启用。
/// </summary>
public sealed record RestoreUserRequest(
    string? Reason,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>
/// 用户摘要(§16.3)。标识一律使用 NId,不暴露数据库 Guid;
/// 双版本供写接口乐观并发回传。状态为 UserStatus 枚举名(Active/Disabled)。
/// </summary>
public sealed record UserSummary(
    string UserNId,
    string LoginName,
    string Name,
    string? Email,
    string? Phone,
    string Status,
    string TenantNId,
    DateTimeOffset CreatedOn,
    DateTimeOffset? LastLoginOn,
    IReadOnlyList<string> RoleNIds,
    long OptimisticVersion,
    Guid ConcurrencyVersion);
