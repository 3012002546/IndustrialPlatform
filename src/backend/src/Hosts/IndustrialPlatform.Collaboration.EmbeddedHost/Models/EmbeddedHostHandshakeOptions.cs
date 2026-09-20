using System.Net;
using System.Net.NetworkInformation;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Models;

/// <summary>嵌入握手配置：允许的前端来源、Cookie 名称以及挑战、断言和会话的有效期。</summary>
public sealed record EmbeddedHostHandshakeOptions(IReadOnlyCollection<string> AllowedParentOrigins)
{
    public bool AllowLocalDevelopmentOrigins { get; init; }

    public bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;
        if (AllowedParentOrigins.Contains(origin, StringComparer.Ordinal)) return true;
        if (!AllowLocalDevelopmentOrigins || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || uri.GetLeftPart(UriPartial.Authority) != origin
            || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 5173
            || !IPAddress.TryParse(uri.Host, out var address)) return false;
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Any(local => local.Address.Equals(address));
    }

    public string CookieName { get; init; } = "industrial_embedded_session";
    public string BrowserBindingCookieName { get; init; } = "industrial_embedded_binding";
    public string SourceSessionCookieName { get; init; } = "embedded_host_session";
    public TimeSpan ChallengeLifetime { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan AssertionLifetime { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromSeconds(60);
}
