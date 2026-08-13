using IndustrialPlatform.Identity.Domain.Sso;

namespace IndustrialPlatform.Identity.Application.Sso;

/// <summary>构造 IdP 授权地址所需上下文(§26.4)。</summary>
/// <param name="Provider">目标 Provider 配置。</param>
/// <param name="State">OAuth state(防 CSRF,5 分钟一次性)。</param>
/// <param name="Nonce">OIDC nonce(防重放)。</param>
/// <param name="CodeChallenge">PKCE code_challenge(已发送给 IdP)。</param>
/// <param name="RedirectUri">回调地址(注册的 Redirect URI)。</param>
/// <param name="Prompt">可选 prompt 参数(SAML 适配器忽略)。</param>
public sealed record ExternalAuthorizeContext(
    IdentitySsoProvider Provider,
    string State,
    string Nonce,
    string CodeChallenge,
    string RedirectUri,
    string? Prompt = null);

/// <summary>授权地址构造结果。</summary>
public sealed record ExternalAuthorizeResult(string RedirectUri);

/// <summary>处理 IdP 回调所需上下文(§26.5)。</summary>
/// <param name="Provider">目标 Provider 配置。</param>
/// <param name="Parameters">回调参数(OIDC 为 query,含 code/state;SAML 为表单,含 SAMLResponse)。</param>
/// <param name="ExpectedState">期望的 state,由服务从 Redis 一次性取回。</param>
/// <param name="ExpectedNonce">期望的 nonce(OIDC)。</param>
/// <param name="CodeVerifier">PKCE code_verifier(用于 token 交换与校验)。</param>
/// <param name="RedirectUri">预存回调地址(token 交换与 SAML Recipient 校验需回传)。</param>
public sealed record ExternalCallbackContext(
    IdentitySsoProvider Provider,
    IReadOnlyDictionary<string, string> Parameters,
    string ExpectedState,
    string? ExpectedNonce,
    string? CodeVerifier,
    string RedirectUri);

/// <summary>IdP 断言验证通过后的外部身份(§26.3)。</summary>
/// <param name="ExternalSubject">不可变外部主体标识,用于账号匹配。</param>
/// <param name="ExternalName">外部显示名称。</param>
/// <param name="ExternalEmail">外部邮箱(JIT 域名匹配依据)。</param>
public sealed record ExternalLoginResult(
    string ExternalSubject,
    string? ExternalName,
    string? ExternalEmail);

/// <summary>构造 IdP 登出地址所需上下文(§26.9 Federated)。</summary>
public sealed record ExternalLogoutContext(
    IdentitySsoProvider Provider,
    string? IdTokenHint,
    string PostLogoutRedirectUri);

/// <summary>登出地址构造结果;RedirectUri 为空表示无需 IdP 跳转。</summary>
public sealed record ExternalLogoutResult(string? RedirectUri);

/// <summary>管理端连接测试上下文。</summary>
public sealed record ExternalConnectionTestContext(IdentitySsoProvider Provider);

/// <summary>管理端连接测试结果。Message 不得包含密钥或完整外部主体标识。</summary>
public sealed record ExternalConnectionTestResult(bool Reachable, string Message);
