namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// 管理用例业务异常基类:携带标准 HTTP 状态码与 §17 错误码。
/// 由 Api 控制器映射为统一 ApiResult 信封,message 不得包含密码、Token、内部哈希
/// 或其他实体是否存在等可枚举信息。
/// </summary>
public abstract class ManagementException : Exception
{
    /// <summary>标准 HTTP 状态码。</summary>
    public int StatusCode { get; }

    /// <summary>§17 错误码。</summary>
    public string Code { get; }

    /// <summary>外部可见消息。</summary>
    protected ManagementException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

/// <summary>用户业务标识冲突(§17 ID_USER_NID_CONFLICT)。</summary>
public sealed class UserNIdConflictException : ManagementException
{
    public UserNIdConflictException()
        : base(409, "ID_USER_NID_CONFLICT", "用户业务标识已存在。")
    {
    }
}

/// <summary>登录名冲突(§17 ID_USER_LOGIN_NAME_CONFLICT)。</summary>
public sealed class UserLoginNameConflictException : ManagementException
{
    public UserLoginNameConflictException()
        : base(409, "ID_USER_LOGIN_NAME_CONFLICT", "登录名已被其他用户使用。")
    {
    }
}

/// <summary>角色业务标识冲突(§17 ID_ROLE_NID_CONFLICT)。</summary>
public sealed class RoleNIdConflictException : ManagementException
{
    public RoleNIdConflictException()
        : base(409, "ID_ROLE_NID_CONFLICT", "角色业务标识已存在。")
    {
    }
}

/// <summary>权限业务标识冲突(§17 ID_PERMISSION_NID_CONFLICT)。</summary>
public sealed class PermissionNIdConflictException : ManagementException
{
    public PermissionNIdConflictException()
        : base(409, "ID_PERMISSION_NID_CONFLICT", "权限业务标识已存在。")
    {
    }
}

/// <summary>用户组业务标识冲突(§29A.5 ID_GROUP_NID_CONFLICT)。</summary>
public sealed class GroupNIdConflictException : ManagementException
{
    public GroupNIdConflictException()
        : base(409, "ID_GROUP_NID_CONFLICT", "用户组业务标识已存在。")
    {
    }
}

/// <summary>乐观并发冲突(§17 ID_CONCURRENCY_CONFLICT)。</summary>
public sealed class ConcurrencyConflictException : ManagementException
{
    public ConcurrencyConflictException()
        : base(409, "ID_CONCURRENCY_CONFLICT", "数据已被其他操作修改，请刷新后重试。")
    {
    }
}

/// <summary>
/// 资源不存在或跨租户访问。两者返回相同 404,避免泄漏其他租户资源存在性(§16)。
/// </summary>
public sealed class ResourceNotFoundException : ManagementException
{
    public ResourceNotFoundException()
        : base(404, "404", "资源不存在。")
    {
    }
}

/// <summary>
/// 业务规则拒绝(如最后一名系统管理员保护、禁用当前登录用户、系统角色保护)。
/// 400 稳定错误码,message 说明可诊断原因但不含敏感信息。
/// </summary>
public sealed class BusinessRuleViolationException : ManagementException
{
    public BusinessRuleViolationException(string message)
        : base(400, "ID_BUSINESS_RULE_VIOLATION", message)
    {
    }
}

/// <summary>最后一名系统管理员保护(§29A.3 ID_LAST_ADMIN_REQUIRED):禁用/删除/恢复等路径的权威计数守卫。</summary>
public sealed class LastAdminRequiredException : ManagementException
{
    public LastAdminRequiredException()
        : base(400, "ID_LAST_ADMIN_REQUIRED", "不能移除最后一名系统管理员，除非经过独立恢复流程。")
    {
    }
}

/// <summary>用户已删除或用户墓碑状态与操作不匹配(§29A.5 ID_USER_DELETED)。</summary>
public sealed class UserDeletedException : ManagementException
{
    public UserDeletedException()
        : base(400, "ID_USER_DELETED", "用户已删除或未处于可操作状态。")
    {
    }
}

/// <summary>登录名被系统保留(§29A.5 ID_USER_LOGIN_NAME_RESERVED):内置 admin 登录名等系统标识不可被普通用户占用。</summary>
public sealed class UserLoginNameReservedException : ManagementException
{
    public UserLoginNameReservedException()
        : base(400, "ID_USER_LOGIN_NAME_RESERVED", "该登录名为系统保留登录名,不可使用。")
    {
    }
}

/// <summary>用户组不存在或跨租户(§29A.5 ID_GROUP_NOT_FOUND)。</summary>
public sealed class GroupNotFoundException : ManagementException
{
    public GroupNotFoundException()
        : base(404, "ID_GROUP_NOT_FOUND", "用户组不存在。")
    {
    }
}

/// <summary>目标用户组已禁用,不允许该操作(§29A.5 ID_GROUP_DISABLED)。</summary>
public sealed class GroupDisabledException : ManagementException
{
    public GroupDisabledException()
        : base(400, "ID_GROUP_DISABLED", "用户组已禁用,不能执行该操作。")
    {
    }
}

/// <summary>分配给用户组的角色无效(不存在/跨租户/已删除)(§29A.5 ID_GROUP_ROLE_INVALID)。</summary>
public sealed class GroupRoleInvalidException : ManagementException
{
    public GroupRoleInvalidException()
        : base(400, "ID_GROUP_ROLE_INVALID", "存在无效或不可用的角色。")
    {
    }
}

/// <summary>幂等键冲突(§29A.5 ID_IDEMPOTENCY_CONFLICT):同一 Idempotency-Key 携带不同请求内容。</summary>
public sealed class IdempotencyConflictException : ManagementException
{
    public IdempotencyConflictException()
        : base(409, "ID_IDEMPOTENCY_CONFLICT", "幂等键已被不同请求使用,请更换或复用原请求。")
    {
    }
}
