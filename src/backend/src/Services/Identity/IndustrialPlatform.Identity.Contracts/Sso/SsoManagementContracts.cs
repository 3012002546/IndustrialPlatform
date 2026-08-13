namespace IndustrialPlatform.Identity.Contracts.Sso;

/// <summary>创建企业登录源请求(§26.8)。Protocol 为枚举名(Oidc/Saml2),ProvisioningMode/LogoutMode 同理。</summary>
public sealed record CreateSsoProviderRequest(
    string? Name,
    string? Protocol,
    string? AuthorityOrMetadataUrl,
    string? ClientIdOrEntityId,
    string? SecretOrCertificateReference,
    string? CallbackPath,
    bool AutoRedirect,
    string? ProvisioningMode,
    string? LogoutMode,
    IReadOnlyList<string>? AllowedEmailDomains,
    IReadOnlyList<string>? JitDefaultRoleNIds);

/// <summary>更新企业登录源请求(§26.8)。协议与授权地址变更需重新连接测试。</summary>
public sealed record UpdateSsoProviderRequest(
    string? Name,
    string? Protocol,
    string? AuthorityOrMetadataUrl,
    string? ClientIdOrEntityId,
    string? CallbackPath,
    bool AutoRedirect,
    string? ProvisioningMode,
    string? LogoutMode,
    IReadOnlyList<string>? AllowedEmailDomains,
    IReadOnlyList<string>? JitDefaultRoleNIds,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>更新登录源密钥引用请求(§26.8):只写引用(配置节键名),不接收密钥明文。</summary>
public sealed record UpdateSsoProviderSecretRequest(
    string? SecretOrCertificateReference,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>启用/停用企业登录源请求。</summary>
public sealed record SetSsoProviderEnabledRequest(
    bool Enabled,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>连接测试结果(§26.8)。</summary>
public sealed record ProviderTestResult(bool Reachable, string Message);

/// <summary>企业登录源摘要(§26.8)。HasSecretReference 只暴露是否已配置密钥引用,不暴露键名。</summary>
public sealed record ProviderSummary(
    string ProviderNId,
    string Name,
    string Protocol,
    string AuthorityOrMetadataUrl,
    string ClientIdOrEntityId,
    bool HasSecretReference,
    string CallbackPath,
    bool Enabled,
    bool AutoRedirect,
    string ProvisioningMode,
    string LogoutMode,
    IReadOnlyList<string> AllowedEmailDomains,
    IReadOnlyList<string> JitDefaultRoleNIds,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    long OptimisticVersion,
    Guid ConcurrencyVersion);

/// <summary>绑定外部账号请求(§26.8):externalSubject 为 IdP 侧主体标识,仅管理员录入,不回显。</summary>
public sealed record BindSsoAccountRequest(
    string? UserNId,
    string? ExternalSubject,
    string? ExternalName,
    string? ExternalEmail);

/// <summary>外部账号摘要(不暴露 external subject)。</summary>
public sealed record ExternalAccountSummary(
    string AccountNId,
    string ProviderNId,
    string UserNId,
    string UserLoginName,
    string UserName,
    string? ExternalName,
    string? ExternalEmail,
    DateTimeOffset? LastLoginOn,
    long OptimisticVersion,
    Guid ConcurrencyVersion);

/// <summary>创建平台 SSO Client 请求(§26.7)。</summary>
public sealed record CreateSsoClientRequest(
    string? Name,
    string? OAuthClientId);

/// <summary>更新平台 SSO Client 请求。</summary>
public sealed record UpdateSsoClientRequest(
    string? Name,
    string? OAuthClientId,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>启用/停用平台 SSO Client 请求。</summary>
public sealed record SetSsoClientEnabledRequest(
    bool Enabled,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>登记端点请求(§26.7)。Type 为枚举名(Redirect/PostLogoutRedirect/Origin),版本为所属 Client 双版本。</summary>
public sealed record AddSsoEndpointRequest(
    string? NId,
    string? Type,
    string? Uri,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>启用/停用端点请求。</summary>
public sealed record SetSsoEndpointEnabledRequest(
    bool Enabled,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

/// <summary>端点摘要。</summary>
public sealed record SsoEndpointSummary(
    string EndpointNId,
    string Type,
    string Uri,
    bool Enabled);

/// <summary>平台 SSO Client 摘要(含端点)。</summary>
public sealed record SsoClientSummary(
    string ClientNId,
    string Name,
    string OAuthClientId,
    bool Enabled,
    IReadOnlyList<SsoEndpointSummary> Endpoints,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    long OptimisticVersion,
    Guid ConcurrencyVersion);
