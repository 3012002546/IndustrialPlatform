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
