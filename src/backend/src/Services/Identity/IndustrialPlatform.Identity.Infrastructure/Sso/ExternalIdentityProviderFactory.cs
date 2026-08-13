using IndustrialPlatform.Identity.Application.Sso;
using IndustrialPlatform.Identity.Domain.Sso;

namespace IndustrialPlatform.Identity.Infrastructure.Sso;

/// <summary>
/// 按协议解析外部 IdP 适配器的工厂:注册时每个协议恰好一个适配器,
/// 重复注册在启动阶段直接失败(配置错误应尽早暴露)。
/// </summary>
public sealed class ExternalIdentityProviderFactory : IExternalIdentityProviderFactory
{
    private readonly Dictionary<SsoProtocol, IExternalIdentityProvider> _providers;

    /// <summary>初始化适配器工厂。</summary>
    public ExternalIdentityProviderFactory(IEnumerable<IExternalIdentityProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToDictionary(p => p.Protocol);
    }

    /// <inheritdoc/>
    public IExternalIdentityProvider GetFor(SsoProtocol protocol)
        => _providers.TryGetValue(protocol, out var provider)
            ? provider
            : throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "未注册的外部身份协议适配器。");
}
