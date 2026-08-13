using IndustrialPlatform.Identity.Contracts.Authentication;

namespace IndustrialPlatform.Identity.Contracts.Sso;

/// <summary>SSO 发现结果 Provider 摘要(§26.4,仅公开配置,不含密钥引用)。Protocol 为协议枚举名。</summary>
public sealed record SsoDiscoveryProvider(
    string ProviderNId,
    string Name,
    string Protocol,
    bool AutoRedirect);

/// <summary>
/// 授权开始响应(§26.4):Reused=true 时携带一次性票据(<paramref name="Ticket"/>),前端直接交换;
/// 否则 <paramref name="NeedsSelection"/> 为 true 时进入 Provider 选择,或携带
/// <paramref name="AuthorizeUri"/> 跳转 IdP。<paramref name="ReturnUrl"/> 为已校验站内回跳地址。
/// </summary>
public sealed record SsoBeginResponse(
    bool Reused,
    string? Ticket,
    string? ReturnUrl,
    bool NeedsSelection,
    string? ProviderNId,
    string? AuthorizeUri,
    IReadOnlyList<SsoDiscoveryProvider>? Providers);

/// <summary>票据交换请求(§26.5):一次性票据由回调跳转携带。</summary>
public sealed record SsoExchangeRequest(string? Ticket);

/// <summary>
/// 票据交换响应(§26.5):完整认证会话与已校验站内回跳地址。
/// <paramref name="BrowserSessionHandle"/> 为浏览器 SSO 会话句柄,仅服务器用于写 Cookie,不返回前端。
/// </summary>
public sealed record SsoExchangeResponse(
    AuthSession Session,
    string? ReturnUrl,
    string BrowserSessionHandle);

/// <summary>
/// 登出请求(§26.9):postLogoutRedirectUri 必须为站内相对地址或已注册 Client 的
/// PostLogoutRedirect 端点(绝对 URL 精确匹配)。
/// </summary>
public sealed record SsoLogoutRequest(
    string? ClientId,
    string? PostLogoutRedirectUri,
    string? RefreshToken);

/// <summary>登出响应:IdpRedirectUri 非空时前端需跳转 IdP 完成联邦登出。</summary>
public sealed record SsoLogoutResponse(string? IdpRedirectUri, bool IsFederated);
