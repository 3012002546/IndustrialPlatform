namespace IndustrialPlatform.Identity.Application.Sso;

/// <summary>
/// SSO 用例选项(配置节 <c>Identity:Sso</c>)。OAuth state 与一次性票据 TTL 的默认值
/// 见 <c>DefaultOAuthStateLifetime</c>/<c>DefaultLoginTicketLifetime</c>(§26.5);
/// 浏览器会话空闲/绝对期限由领域常量 <see cref="IdentitySsoBrowserSession"/> 统一定义。
/// </summary>
public sealed class SsoOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Identity:Sso";

    /// <summary>OAuth state 默认有效期(5 分钟)。</summary>
    public static readonly TimeSpan DefaultOAuthStateLifetime = TimeSpan.FromMinutes(5);

    /// <summary>一次性登录票据默认有效期(60 秒)。</summary>
    public static readonly TimeSpan DefaultLoginTicketLifetime = TimeSpan.FromSeconds(60);

    /// <summary>前端 SSO 回调页基地址(回调 302 跳转 <c>{FrontendBaseUrl}/auth/sso/callback?ticket=…</c>)。</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>SSO 会话 Cookie 是否加 Secure(本地 http 调试置 false)。</summary>
    public bool UseSecureCookies { get; set; } = true;

    /// <summary>SSO 会话 Cookie 名(仅携带浏览器会话句柄)。</summary>
    public string SessionCookieName { get; set; } = "industrial_platform_sso";

    /// <summary>OAuth state 有效期(一次性消费)。</summary>
    public TimeSpan OAuthStateLifetime { get; set; } = DefaultOAuthStateLifetime;

    /// <summary>一次性登录票据有效期。</summary>
    public TimeSpan LoginTicketLifetime { get; set; } = DefaultLoginTicketLifetime;
}
