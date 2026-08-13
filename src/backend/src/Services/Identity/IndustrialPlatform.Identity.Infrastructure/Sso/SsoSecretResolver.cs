using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Identity.Infrastructure.Sso;

/// <summary>SSO 客户端密钥/证书解析端口:按 Provider 的引用键解析明文凭据,仅存在于配置侧。</summary>
public interface ISsoSecretResolver
{
    /// <summary>按引用键解析明文凭据;引用为空返回 <c>null</c>。解析结果不得进入日志/异常。</summary>
    string? Resolve(string? reference);
}

/// <summary>
/// 基于配置的密钥解析实现:读取 <c>Identity:Sso:Secrets</c> 节下以引用键为名的值。
/// 生产环境应由密钥库注入替换本实现,Provider 存储只保存引用。
/// </summary>
public sealed class ConfigurationSsoSecretResolver : ISsoSecretResolver
{
    private readonly IConfiguration _configuration;

    /// <summary>初始化配置密钥解析器。</summary>
    public ConfigurationSsoSecretResolver(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    /// <inheritdoc/>
    public string? Resolve(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        return _configuration.GetSection("Identity:Sso:Secrets")[reference];
    }
}
