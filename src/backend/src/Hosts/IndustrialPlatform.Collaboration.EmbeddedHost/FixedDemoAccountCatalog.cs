using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost;

/// <summary>
/// 独立开发演示账户白名单。URL 中的 xxA/xxB/xxC 只用于选择配置中的演示来源，
/// 不被当作生产认证凭据，也不参与后续 API 身份判断。
/// </summary>
public sealed record FixedDemoAccount(string AccountNId, string SourceSessionValue, string ExternalSubject);

public sealed class FixedDemoAccountCatalog(IConfiguration configuration)
{
    public FixedDemoAccount? Resolve(string? account)
    {
        var requested = account is null
            ? configuration["EmbeddedCollaboration:Demo:DefaultAccount"] ?? "xxA"
            : account;
        if (string.IsNullOrWhiteSpace(requested) || requested != requested.Trim())
            return null;

        var section = configuration.GetSection($"EmbeddedCollaboration:Demo:Accounts:{requested}");
        var sourceSessionValue = section["SourceSessionValue"];
        var externalSubject = section["ExternalSubject"];
        if (string.IsNullOrWhiteSpace(sourceSessionValue) || string.IsNullOrWhiteSpace(externalSubject))
        {
            // 兼容旧的固定 A 私有配置；新配置应显式列出三项账户。
            if (!string.Equals(requested, "xxA", StringComparison.Ordinal))
                return null;
            sourceSessionValue = configuration["EmbeddedCollaboration:Demo:SourceSessionValue"];
            externalSubject = "demo-a";
        }

        return string.IsNullOrWhiteSpace(sourceSessionValue) || string.IsNullOrWhiteSpace(externalSubject)
            ? null
            : new FixedDemoAccount(requested, sourceSessionValue, externalSubject);
    }
}
