using IndustrialPlatform.Identity.Contracts.Sso;

namespace IndustrialPlatform.Identity.Application.Sso;

/// <summary>
/// SSO 管理用例(§26.7/§26.8):企业登录源/外部账号/平台 Client 与端点管理。
/// 写操作携带双版本乐观并发并写操作审计;密钥只更新引用(写后不回显)。
/// </summary>
public interface ISsoManagementService
{
    /// <summary>列出企业登录源(管理端,含停用)。</summary>
    Task<IReadOnlyList<ProviderSummary>> ListProvidersAsync(string tenantNId, CancellationToken cancellationToken);

    /// <summary>查询企业登录源详情。</summary>
    Task<ProviderSummary> GetProviderAsync(string tenantNId, string providerNId, CancellationToken cancellationToken);

    /// <summary>创建企业登录源。</summary>
    Task<ProviderSummary> CreateProviderAsync(string tenantNId, string actorUserNId, CreateSsoProviderRequest request, CancellationToken cancellationToken);

    /// <summary>更新企业登录源配置。</summary>
    Task<ProviderSummary> UpdateProviderAsync(string tenantNId, string actorUserNId, string providerNId, UpdateSsoProviderRequest request, CancellationToken cancellationToken);

    /// <summary>更新企业登录源密钥引用(只写引用,不接收明文)。</summary>
    Task<ProviderSummary> UpdateProviderSecretAsync(string tenantNId, string actorUserNId, string providerNId, UpdateSsoProviderSecretRequest request, CancellationToken cancellationToken);

    /// <summary>启用/停用企业登录源。</summary>
    Task<ProviderSummary> SetProviderEnabledAsync(string tenantNId, string actorUserNId, string providerNId, SetSsoProviderEnabledRequest request, CancellationToken cancellationToken);

    /// <summary>连接测试企业登录源。</summary>
    Task<ProviderTestResult> TestProviderAsync(string tenantNId, string providerNId, CancellationToken cancellationToken);

    /// <summary>列出 Provider 的外部账号(含平台用户展示字段)。</summary>
    Task<IReadOnlyList<ExternalAccountSummary>> ListAccountsAsync(string tenantNId, string providerNId, CancellationToken cancellationToken);

    /// <summary>绑定外部账号到平台用户。</summary>
    Task<ExternalAccountSummary> BindAccountAsync(string tenantNId, string actorUserNId, string providerNId, BindSsoAccountRequest request, CancellationToken cancellationToken);

    /// <summary>解绑外部账号(幂等)。</summary>
    Task UnbindAccountAsync(string tenantNId, string actorUserNId, string providerNId, string userNId, CancellationToken cancellationToken);

    /// <summary>列出平台 SSO Client(含端点)。</summary>
    Task<IReadOnlyList<SsoClientSummary>> ListClientsAsync(string tenantNId, CancellationToken cancellationToken);

    /// <summary>查询平台 SSO Client 详情。</summary>
    Task<SsoClientSummary> GetClientAsync(string tenantNId, string clientNId, CancellationToken cancellationToken);

    /// <summary>创建平台 SSO Client。</summary>
    Task<SsoClientSummary> CreateClientAsync(string tenantNId, string actorUserNId, CreateSsoClientRequest request, CancellationToken cancellationToken);

    /// <summary>更新平台 SSO Client。</summary>
    Task<SsoClientSummary> UpdateClientAsync(string tenantNId, string actorUserNId, string clientNId, UpdateSsoClientRequest request, CancellationToken cancellationToken);

    /// <summary>启用/停用平台 SSO Client。</summary>
    Task<SsoClientSummary> SetClientEnabledAsync(string tenantNId, string actorUserNId, string clientNId, SetSsoClientEnabledRequest request, CancellationToken cancellationToken);

    /// <summary>登记 Client 端点。</summary>
    Task<SsoClientSummary> AddClientEndpointAsync(string tenantNId, string actorUserNId, string clientNId, AddSsoEndpointRequest request, CancellationToken cancellationToken);

    /// <summary>启用/停用 Client 端点。</summary>
    Task<SsoClientSummary> SetClientEndpointEnabledAsync(string tenantNId, string actorUserNId, string clientNId, string endpointNId, SetSsoEndpointEnabledRequest request, CancellationToken cancellationToken);

    /// <summary>移除 Client 端点(幂等)。</summary>
    Task<SsoClientSummary> RemoveClientEndpointAsync(string tenantNId, string actorUserNId, string clientNId, string endpointNId, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken);
}
