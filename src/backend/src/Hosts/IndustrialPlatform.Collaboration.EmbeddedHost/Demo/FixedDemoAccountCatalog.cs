using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Demo;

/// <summary>
/// 独立开发演示账户目录。URL 中的 account 只用于选择 SourceSessions 中的演示来源，
/// 不被当作生产认证凭据，也不参与后续 API 身份判断。
/// </summary>
public sealed class FixedDemoAccountCatalog(IConfiguration configuration)
{
    public FixedDemoAccount? Resolve(string? account)
    {
        var requested = account ?? configuration["EmbeddedCollaboration:Demo:DefaultAccount"];
        if (string.IsNullOrWhiteSpace(requested) || requested != requested.Trim()
            || requested.Length > 128 || requested.Contains(':', StringComparison.Ordinal))
            return null;

        var section = configuration.GetSection($"EmbeddedCollaboration:SourceSessions:{requested}");
        var externalSubject = section["ExternalSubject"];
        var active = string.IsNullOrWhiteSpace(section["Status"])
            || string.Equals(section["Status"], "Active", StringComparison.OrdinalIgnoreCase);
        var enabled = string.IsNullOrWhiteSpace(section["Enabled"])
            || bool.TryParse(section["Enabled"], out var isEnabled) && isEnabled;
        return !active || !enabled || string.IsNullOrWhiteSpace(section["SessionNId"]) || string.IsNullOrWhiteSpace(externalSubject)
            ? null
            : new FixedDemoAccount(requested, externalSubject);
    }
}
