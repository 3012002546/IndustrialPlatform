namespace IndustrialPlatform.Identity.Domain.Sso;

/// <summary>联邦登录协议(§26.2)。OIDC 为第一优先,SAML2 为企业兼容。CAS/LDAP 通过后续适配器扩展。</summary>
public enum SsoProtocol
{
    /// <summary>OpenID Connect(Authorization Code + PKCE)。</summary>
    Oidc,

    /// <summary>SAML 2.0 Service Provider。</summary>
    Saml2,
}

/// <summary>外部账号供给策略(§26.3)。默认 ExistingOnly,禁止默认 JIT。</summary>
public enum SsoProvisioningMode
{
    /// <summary>外部账号必须预先绑定平台用户,不自动创建。</summary>
    ExistingOnly,

    /// <summary>由租户管理员显式启用,按允许邮箱域与默认角色自动创建本地用户。</summary>
    JustInTime,
}

/// <summary>注销策略(§26.9)。默认 LocalOnly,避免意外退出客户全部系统。</summary>
public enum SsoLogoutMode
{
    /// <summary>仅撤销 Industrial Platform 会话,不调用 IdP 登出。</summary>
    LocalOnly,

    /// <summary>调用 OIDC end-session 或 SAML Single Logout,并校验精确 post-logout redirect。</summary>
    Federated,
}

/// <summary>平台 SSO Client 端点类型(§26.7)。Redirect URI 精确匹配,不支持通配符域名。</summary>
public enum SsoClientEndpointType
{
    /// <summary>授权回调重定向地址。</summary>
    Redirect,

    /// <summary>登出后重定向地址。</summary>
    PostLogoutRedirect,

    /// <summary>显式 CORS 来源(仅开发跨端口调试)。</summary>
    Origin,
}

/// <summary>平台浏览器 SSO 会话撤销原因(§26.6)。</summary>
public enum SsoBrowserSessionRevokeReason
{
    /// <summary>用户主动注销。</summary>
    Logout,

    /// <summary>安全事件(AuthVersion 推进、密码变更等)触发。</summary>
    Security,

    /// <summary>会话过期。</summary>
    Expired,
}
