namespace IndustrialPlatform.Identity.Contracts.Management;

/// <summary>
/// 创建角色请求(§16.3)。属性可空:空值由应用层校验抛校验异常。
/// 平台 API 创建的角色均为非系统角色;系统角色只由种子数据维护。
/// </summary>
public sealed record CreateRoleRequest(
    string? NId,
    string? Name,
    string? Description,
    IReadOnlyList<string>? PermissionNIds);

/// <summary>
/// 更新角色请求(§16.3)。业务标识与系统角色标记创建后不可变;
/// 双版本为客户端读取详情时保存的原始值,冲突返回 409 ID_CONCURRENCY_CONFLICT。
/// </summary>
public sealed record UpdateRoleRequest(
    string? Name,
    string? Description,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>
/// 分配角色权限请求(§16.3)。permissionNIds 为最终目标权限集(幂等收敛),
/// 不在目标集中的现有权限将被解除;变化后失效相关用户权限缓存。
/// </summary>
public sealed record AssignRolePermissionsRequest(
    IReadOnlyList<string>? PermissionNIds,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>
/// 角色摘要(§16.3)。标识一律使用 NId,不暴露数据库 Guid;
/// 双版本供写接口乐观并发回传。
/// </summary>
public sealed record RoleSummary(
    string RoleNId,
    string Name,
    string? Description,
    bool IsSystem,
    string TenantNId,
    IReadOnlyList<string> PermissionNIds,
    long OptimisticVersion,
    Guid ConcurrencyVersion);
