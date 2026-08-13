using IndustrialPlatform.Identity.Contracts.Sso;

namespace IndustrialPlatform.Identity.Application.Sso;

/// <summary>回调处理结果(仅 Api 内部用于 302 跳转,不序列化给前端)。</summary>
public sealed record SsoCallbackResult(string Ticket, string? ReturnUrl, string BrowserSessionHandle);

/// <summary>
/// SSO 认证用例(§26.4/§26.5/§26.9):发现、授权、回调、票据交换与登出。
/// 编排 SSO 存储、外部 IdP 适配器与认证端口;错误统一映射为 <see cref="SsoException"/>,
/// state/票据/句柄等敏感值绝不进入消息。
/// </summary>
public interface ISsoService
{
    /// <summary>列出启用 Provider(登录页展示;connection 为可选名称过滤)。</summary>
    Task<IReadOnlyList<SsoDiscoveryProvider>> DiscoveryAsync(string? connection, CancellationToken cancellationToken);

    /// <summary>开始授权:可选复用浏览器会话,否则选择/跳转 Provider 并预存 OAuth state。</summary>
    Task<SsoBeginResponse> BeginAuthorizeAsync(
        string tenantNId,
        string? clientId,
        string? returnUrl,
        string? providerNId,
        string requestBaseUrl,
        CancellationToken cancellationToken);

    /// <summary>复用浏览器会话:校验有效并签发一次性票据;无效返回 Reused=false。</summary>
    Task<SsoBeginResponse> ReuseBrowserSessionAsync(
        string sessionHandle,
        string tenantNId,
        string? clientId,
        string? returnUrl,
        CancellationToken cancellationToken);

    /// <summary>处理 OIDC 授权码回调(§26.5):校验 state/nonce/PKCE 并消费,返回一次性票据。</summary>
    Task<SsoCallbackResult> HandleOidcCallbackAsync(
        string providerNId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken);

    /// <summary>处理 SAML 回调(§26.5):校验 RelayState/签名/断言并消费,返回一次性票据。</summary>
    Task<SsoCallbackResult> HandleSamlCallbackAsync(
        string providerNId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken);

    /// <summary>消费一次性票据并签发完整认证会话(§26.5)。</summary>
    Task<SsoExchangeResponse> ExchangeTicketAsync(
        string ticket,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken);

    /// <summary>登出:撤销刷新会话族与浏览器会话,必要时构造 IdP 联邦登出地址。</summary>
    Task<SsoLogoutResponse> LogoutAsync(
        string? sessionHandle,
        string? refreshToken,
        string? clientId,
        string? postLogoutRedirectUri,
        string? accessTokenSessionNId,
        TimeSpan sidRevocationTtl,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken);
}
