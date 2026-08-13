using IndustrialPlatform.Identity.Domain.Sso;

namespace IndustrialPlatform.Identity.Application.Sso;

/// <summary>
/// 外部 IdP 适配器端口(§26.2):协议无关的授权地址构造、回调断言验证与联邦登出地址构造。
/// 适配器只接触 Provider 配置(经 ISsoSecretResolver 解析),绝不在日志/异常中输出密钥或 Token。
/// </summary>
public interface IExternalIdentityProvider
{
    /// <summary>适配器支持的协议。</summary>
    SsoProtocol Protocol { get; }

    /// <summary>构造 IdP 授权地址(含 state/nonce/PKCE 参数)。</summary>
    Task<ExternalAuthorizeResult> BuildAuthorizeUriAsync(ExternalAuthorizeContext context, CancellationToken cancellationToken);

    /// <summary>处理 IdP 回调并验证断言/令牌,返回已验证的外部身份。校验失败抛 <see cref="SsoCallbackValidationException"/>。</summary>
    Task<ExternalLoginResult> HandleCallbackAsync(ExternalCallbackContext context, CancellationToken cancellationToken);

    /// <summary>构造 IdP 联邦登出地址(Federated);无可用端点或协议不支持时返回空。</summary>
    Task<ExternalLogoutResult> BuildLogoutUriAsync(ExternalLogoutContext context, CancellationToken cancellationToken);

    /// <summary>连通性测试:拉取发现文档/元数据并校验关键配置。</summary>
    Task<ExternalConnectionTestResult> TestConnectionAsync(ExternalConnectionTestContext context, CancellationToken cancellationToken);
}

/// <summary>按协议解析适配器的工厂端口。</summary>
public interface IExternalIdentityProviderFactory
{
    /// <summary>按协议返回适配器;协议未注册抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
    IExternalIdentityProvider GetFor(SsoProtocol protocol);
}
