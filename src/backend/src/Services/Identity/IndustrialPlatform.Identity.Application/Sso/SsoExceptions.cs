namespace IndustrialPlatform.Identity.Application.Sso;

/// <summary>
/// SSO 用例业务异常基类:携带标准 HTTP 状态码与 §17 错误码,由 Api 映射为统一
/// ApiResult 信封。message 不得包含 Token、密钥、外部主体标识或存在性可枚举信息。
/// </summary>
public abstract class SsoException : Exception
{
    /// <summary>标准 HTTP 状态码。</summary>
    public int StatusCode { get; }

    /// <summary>§17 错误码。</summary>
    public string Code { get; }

    /// <summary>外部可见消息。</summary>
    protected SsoException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

/// <summary>Provider 不存在或跨租户访问(404)。</summary>
public sealed class SsoProviderNotFoundException : SsoException
{
    public SsoProviderNotFoundException()
        : base(404, "ID_SSO_PROVIDER_NOT_FOUND", "企业登录源不存在。")
    {
    }
}

/// <summary>SSO Client 不存在或跨租户访问(404)。</summary>
public sealed class SsoClientNotFoundException : SsoException
{
    public SsoClientNotFoundException()
        : base(404, "ID_SSO_CLIENT_NOT_FOUND", "平台客户端不存在。")
    {
    }
}

/// <summary>外部账号未绑定平台用户(403,§26.3 ExistingOnly)。</summary>
public sealed class SsoAccountNotLinkedException : SsoException
{
    public SsoAccountNotLinkedException()
        : base(403, "ID_SSO_ACCOUNT_NOT_LINKED", "该企业账号未绑定平台用户,请联系管理员完成绑定。")
    {
    }
}

/// <summary>外部账号已绑定其他平台用户(409)。</summary>
public sealed class SsoAccountLinkConflictException : SsoException
{
    public SsoAccountLinkConflictException()
        : base(409, "ID_SSO_ACCOUNT_LINK_CONFLICT", "该企业账号已绑定其他平台用户。")
    {
    }
}

/// <summary>OAuth state 无效、过期或已被消费(400)。</summary>
public sealed class SsoStateInvalidException : SsoException
{
    public SsoStateInvalidException()
        : base(400, "ID_SSO_STATE_INVALID", "登录状态无效或已过期,请重新发起登录。")
    {
    }
}

/// <summary>一次性登录票据无效、过期或已被消费(400)。</summary>
public sealed class SsoTicketInvalidException : SsoException
{
    public SsoTicketInvalidException()
        : base(400, "ID_SSO_TICKET_INVALID", "登录票据无效或已过期,请重新发起登录。")
    {
    }
}

/// <summary>Provider 已停用(403)。</summary>
public sealed class SsoProviderDisabledException : SsoException
{
    public SsoProviderDisabledException()
        : base(403, "ID_SSO_PROVIDER_DISABLED", "该企业登录源已停用。")
    {
    }
}

/// <summary>Provider 不可达或协议校验失败(503)。</summary>
public sealed class SsoProviderUnavailableException : SsoException
{
    public SsoProviderUnavailableException()
        : base(503, "ID_SSO_PROVIDER_UNAVAILABLE", "企业身份服务暂时不可用,请稍后重试。")
    {
    }
}

/// <summary>回调断言校验失败(签名/issuer/audience/nonce/PKCE/时间窗/重放)(400)。</summary>
public sealed class SsoCallbackValidationException : SsoException
{
    public SsoCallbackValidationException()
        : base(400, "ID_SSO_CALLBACK_INVALID", "企业登录回调校验失败。")
    {
    }
}

/// <summary>returnUrl 不在允许白名单内(400,§26.5)。</summary>
public sealed class SsoReturnUrlInvalidException : SsoException
{
    public SsoReturnUrlInvalidException()
        : base(400, "ID_SSO_RETURN_URL_INVALID", "回跳地址不合法。")
    {
    }
}

/// <summary>JIT 供给被禁止(域名未允许或未启用 JIT)(403,§26.3)。</summary>
public sealed class SsoJitNotAllowedException : SsoException
{
    public SsoJitNotAllowedException()
        : base(403, "ID_SSO_JIT_NOT_ALLOWED", "该企业邮箱域不允许自动开户,请联系管理员。")
    {
    }
}

/// <summary>联邦注销参数不合法(400)。</summary>
public sealed class SsoLogoutInvalidException : SsoException
{
    public SsoLogoutInvalidException()
        : base(400, "ID_SSO_LOGOUT_INVALID", "注销请求不合法。")
    {
    }
}

/// <summary>外部账号关联的平台用户不可用(已删除/停用)(403,§26.3)。</summary>
public sealed class SsoUserUnavailableException : SsoException
{
    public SsoUserUnavailableException()
        : base(403, "ID_SSO_USER_UNAVAILABLE", "该企业账号关联的平台用户不可用,请联系管理员。")
    {
    }
}
