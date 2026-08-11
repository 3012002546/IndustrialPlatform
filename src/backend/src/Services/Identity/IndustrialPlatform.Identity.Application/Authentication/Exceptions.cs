namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>
/// 认证业务异常基类:携带标准 HTTP 状态码与 §17 错误码。
/// 由 Api 控制器映射为统一 ApiResult 信封,message 不得包含密码、Token、内部哈希或用户是否存在。
/// </summary>
public abstract class AuthenticationException : Exception
{
    /// <summary>标准 HTTP 状态码。</summary>
    public int StatusCode { get; }

    /// <summary>§17 错误码。</summary>
    public string Code { get; }

    /// <summary>外部可见消息。</summary>
    protected AuthenticationException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

/// <summary>用户名或密码错误(不存在用户与错误密码返回相同错误,防枚举)。</summary>
public sealed class InvalidCredentialsException : AuthenticationException
{
    public InvalidCredentialsException()
        : base(401, "ID_AUTH_INVALID_CREDENTIALS", "用户名或密码错误。")
    {
    }
}

/// <summary>账号已禁用。</summary>
public sealed class AccountDisabledException : AuthenticationException
{
    public AccountDisabledException()
        : base(403, "ID_AUTH_ACCOUNT_DISABLED", "账号不可用，请联系管理员。")
    {
    }
}

/// <summary>登录请求受限(按 IP/账号限流或临时锁定)。</summary>
public sealed class RateLimitExceededException : AuthenticationException
{
    public RateLimitExceededException(string? message = null)
        : base(429, "ID_AUTH_RATE_LIMITED", message ?? "登录请求过于频繁，请稍后再试。")
    {
    }
}

/// <summary>刷新会话等安全存储不可用。</summary>
public sealed class SecurityStoreUnavailableException : AuthenticationException
{
    public SecurityStoreUnavailableException()
        : base(503, "ID_AUTH_SECURITY_STORE_UNAVAILABLE", "认证服务暂时不可用，请稍后再试。")
    {
    }
}

/// <summary>会话已失效(令牌合法但用户不存在/被删除)。</summary>
public sealed class SessionInvalidException : AuthenticationException
{
    public SessionInvalidException()
        : base(401, "401", "登录已失效，请重新登录。")
    {
    }
}

/// <summary>Refresh Token 无效/过期/已撤销(§17 ID_AUTH_REFRESH_INVALID)。</summary>
public sealed class RefreshTokenInvalidException : AuthenticationException
{
    public RefreshTokenInvalidException()
        : base(401, "ID_AUTH_REFRESH_INVALID", "刷新令牌无效或已过期，请重新登录。")
    {
    }
}

/// <summary>检测到 Refresh Token 重放(§17 ID_AUTH_REFRESH_REUSED);已撤销整个 Family,要求重新登录。</summary>
public sealed class RefreshTokenReusedException : AuthenticationException
{
    public RefreshTokenReusedException()
        : base(401, "ID_AUTH_REFRESH_REUSED", "检测到刷新令牌重用，会话已撤销，请重新登录。")
    {
    }
}
