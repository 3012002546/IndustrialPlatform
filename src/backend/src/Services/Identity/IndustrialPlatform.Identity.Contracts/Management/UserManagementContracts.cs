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
