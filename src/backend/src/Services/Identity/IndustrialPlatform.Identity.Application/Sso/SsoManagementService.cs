using System.Diagnostics;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Contracts.Sso;
using IndustrialPlatform.Identity.Domain.Sso;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.Identity.Application.Sso;

/// <summary>
/// SSO 管理用例实现(§26.7/§26.8)。写操作按乐观并发更新并写操作审计;
/// 密钥引用更新不接收明文、审计与摘要绝不包含密钥、DB 主键或外部主体标识。
/// </summary>
public sealed partial class SsoManagementService : ISsoManagementService
{
    private readonly ISsoStore _store;
    private readonly IExternalIdentityProviderFactory _providerFactory;
    private readonly IManagementStore _managementStore;
    private readonly IOperationAuditSink _auditSink;
    private readonly ILogger<SsoManagementService> _logger;

    public SsoManagementService(
        ISsoStore store,
        IExternalIdentityProviderFactory providerFactory,
        IManagementStore managementStore,
        IOperationAuditSink auditSink,
        ILogger<SsoManagementService> logger)
    {
        _store = store;
        _providerFactory = providerFactory;
        _managementStore = managementStore;
        _auditSink = auditSink;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderSummary>> ListProvidersAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var providers = await _store.ListProvidersAsync(tenantNId, cancellationToken);
        return providers.Select(ToProviderSummary).ToList();
    }

    /// <inheritdoc/>
    public async Task<ProviderSummary> GetProviderAsync(string tenantNId, string providerNId, CancellationToken cancellationToken)
    {
        var provider = await RequireProviderAsync(tenantNId, providerNId, cancellationToken);
        return ToProviderSummary(provider);
    }

    /// <inheritdoc/>
    public async Task<ProviderSummary> CreateProviderAsync(
        string tenantNId,
        string actorUserNId,
        CreateSsoProviderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = IdentitySsoProvider.Create(
            tenantNId,
            SsoRandom.NId("SSOP-"),
            RequireTrimmed(request.Name, "Provider 名称不能为空。"),
            ParseProtocol(request.Protocol),
            RequireTrimmed(request.AuthorityOrMetadataUrl, "Issuer/元数据地址不能为空。"),
            RequireTrimmed(request.ClientIdOrEntityId, "ClientId/EntityId 不能为空。"),
            request.SecretOrCertificateReference,
            RequireTrimmed(request.CallbackPath, "回调路径不能为空。"),
            request.AutoRedirect,
            ParseProvisioningMode(request.ProvisioningMode),
            ParseLogoutMode(request.LogoutMode),
            request.AllowedEmailDomains ?? [],
            request.JitDefaultRoleNIds ?? []);

        await _store.AddProviderAsync(provider, cancellationToken);

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.SsoProviderCreate,
                OperationObjectType.SsoProvider,
                provider.NId,
                null,
                BuildProviderSummaryText(provider),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToProviderSummary(provider);
    }

    /// <inheritdoc/>
    public async Task<ProviderSummary> UpdateProviderAsync(
        string tenantNId,
        string actorUserNId,
        string providerNId,
        UpdateSsoProviderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = await RequireProviderAsync(tenantNId, providerNId, cancellationToken);
        var before = BuildProviderSummaryText(provider);

        provider.UpdateConfiguration(
            RequireTrimmed(request.Name, "Provider 名称不能为空。"),
            ParseProtocol(request.Protocol),
            RequireTrimmed(request.AuthorityOrMetadataUrl, "Issuer/元数据地址不能为空。"),
            RequireTrimmed(request.ClientIdOrEntityId, "ClientId/EntityId 不能为空。"),
            RequireTrimmed(request.CallbackPath, "回调路径不能为空。"),
            request.AutoRedirect,
            ParseProvisioningMode(request.ProvisioningMode),
            ParseLogoutMode(request.LogoutMode),
            request.AllowedEmailDomains ?? [],
            request.JitDefaultRoleNIds ?? []);

        await ExecuteWriteAsync(() => _store.UpdateProviderAsync(provider, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken));

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.SsoProviderUpdate,
                OperationObjectType.SsoProvider,
                provider.NId,
                before,
                BuildProviderSummaryText(provider),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToProviderSummary(provider);
    }

    /// <inheritdoc/>
    public async Task<ProviderSummary> UpdateProviderSecretAsync(
        string tenantNId,
        string actorUserNId,
        string providerNId,
        UpdateSsoProviderSecretRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = await RequireProviderAsync(tenantNId, providerNId, cancellationToken);

        provider.ChangeSecretReference(request.SecretOrCertificateReference);
        await ExecuteWriteAsync(() => _store.UpdateProviderAsync(provider, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken));

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.SsoProviderUpdateSecret,
                OperationObjectType.SsoProvider,
                provider.NId,
                null,
                null,
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToProviderSummary(provider);
    }

    /// <inheritdoc/>
    public async Task<ProviderSummary> SetProviderEnabledAsync(
        string tenantNId,
        string actorUserNId,
        string providerNId,
        SetSsoProviderEnabledRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = await RequireProviderAsync(tenantNId, providerNId, cancellationToken);
        var before = BuildProviderSummaryText(provider);

        provider.SetEnabled(request.Enabled);
        await ExecuteWriteAsync(() => _store.UpdateProviderAsync(provider, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken));

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                request.Enabled ? OperationAction.SsoProviderEnable : OperationAction.SsoProviderDisable,
                OperationObjectType.SsoProvider,
                provider.NId,
                before,
                BuildProviderSummaryText(provider),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToProviderSummary(provider);
    }

    /// <inheritdoc/>
    public async Task<ProviderTestResult> TestProviderAsync(string tenantNId, string providerNId, CancellationToken cancellationToken)
    {
        var provider = await RequireProviderAsync(tenantNId, providerNId, cancellationToken);
        try
        {
            var result = await _providerFactory
                .GetFor(provider.Protocol)
                .TestConnectionAsync(new ExternalConnectionTestContext(provider), cancellationToken);
            return new ProviderTestResult(result.Reachable, result.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTestConnectionFailed(_logger, ex);
            return new ProviderTestResult(false, "连接失败,请检查地址与配置。");
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExternalAccountSummary>> ListAccountsAsync(string tenantNId, string providerNId, CancellationToken cancellationToken)
    {
        var provider = await RequireProviderAsync(tenantNId, providerNId, cancellationToken);
        var accounts = await _store.ListExternalAccountsAsync(provider.Id, provider.NId, cancellationToken);
        return accounts
            .Select(a => new ExternalAccountSummary(
                a.NId,
                a.ProviderNId,
                a.UserNId,
                a.UserLoginName,
                a.UserName,
                a.ExternalName,
                a.ExternalEmail,
                a.LastLoginOn,
                a.OptimisticVersion,
                a.ConcurrencyVersion))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<ExternalAccountSummary> BindAccountAsync(
        string tenantNId,
        string actorUserNId,
        string providerNId,
        BindSsoAccountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = await RequireProviderAsync(tenantNId, providerNId, cancellationToken);

        var userNId = request.UserNId?.Trim();
        var subject = request.ExternalSubject?.Trim();
        if (string.IsNullOrWhiteSpace(userNId))
        {
            throw new ValidationException("平台用户不能为空。");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ValidationException("外部主体标识不能为空。");
        }

        var user = await _managementStore.GetUserAsync(userNId!, cancellationToken);
        if (user is null || !string.Equals(user.TenantNId, tenantNId, StringComparison.Ordinal))
        {
            throw new ResourceNotFoundException();
        }

        if (user.Status == UserStatus.Disabled)
        {
            throw new BusinessRuleViolationException("只能绑定启用的平台用户。");
        }

        var existingBySubject = await _store.FindExternalAccountAsync(provider.Id, subject!, cancellationToken);
        if (existingBySubject is not null)
        {
            throw new SsoAccountLinkConflictException();
        }

        var existingByUser = await _store.FindExternalAccountByUserIdAsync(provider.Id, user.Id, cancellationToken);
        if (existingByUser is not null)
        {
            throw new SsoAccountLinkConflictException();
        }

        var account = IdentityExternalAccount.Create(
            SsoRandom.NId("SSOA-"),
            provider.Id,
            provider.IsDeleted,
            subject!,
            user.Id,
            false,
            request.ExternalName,
            request.ExternalEmail);
        await _store.AddExternalAccountAsync(account, cancellationToken);

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.SsoAccountBind,
                OperationObjectType.SsoAccount,
                account.NId,
                null,
                BuildAccountSummaryText(account, user.NId),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return new ExternalAccountSummary(
            account.NId,
            provider.NId,
            user.NId,
            user.LoginName,
            user.Name,
            account.ExternalName,
            account.ExternalEmail,
            null,
            account.OptimisticVersion,
            account.ConcurrencyVersion);
    }

    /// <inheritdoc/>
    public async Task UnbindAccountAsync(
        string tenantNId,
        string actorUserNId,
        string providerNId,
        string userNId,
        CancellationToken cancellationToken)
    {
        var provider = await RequireProviderAsync(tenantNId, providerNId, cancellationToken);

        var user = await _managementStore.GetUserAsync(userNId, cancellationToken);
        if (user is null || !string.Equals(user.TenantNId, tenantNId, StringComparison.Ordinal))
        {
            throw new ResourceNotFoundException();
        }

        var account = await _store.FindExternalAccountByUserIdAsync(provider.Id, user.Id, cancellationToken);
        if (account is null)
        {
            return;
        }

        var expectedOptimistic = account.OptimisticVersion;
        var expectedConcurrency = account.ConcurrencyVersion;
        await ExecuteWriteAsync(() =>
        {
            account.Unbind();
            return _store.UpdateExternalAccountAsync(account, expectedOptimistic, expectedConcurrency, cancellationToken);
        });

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.SsoAccountUnbind,
                OperationObjectType.SsoAccount,
                account.NId,
                null,
                null,
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SsoClientSummary>> ListClientsAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var clients = await _store.ListClientsAsync(tenantNId, cancellationToken);
        return clients.Select(ToClientSummary).ToList();
    }

    /// <inheritdoc/>
    public async Task<SsoClientSummary> GetClientAsync(string tenantNId, string clientNId, CancellationToken cancellationToken)
    {
        var client = await RequireClientAsync(tenantNId, clientNId, cancellationToken);
        return ToClientSummary(client);
    }

    /// <inheritdoc/>
    public async Task<SsoClientSummary> CreateClientAsync(
        string tenantNId,
        string actorUserNId,
        CreateSsoClientRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = IdentitySsoClient.Create(
            tenantNId,
            SsoRandom.NId("SSOC-"),
            RequireTrimmed(request.Name, "Client 名称不能为空。"),
            RequireTrimmed(request.OAuthClientId, "OAuth ClientId 不能为空。"));

        await _store.AddClientAsync(client, cancellationToken);

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.SsoClientCreate,
                OperationObjectType.SsoClient,
                client.NId,
                null,
                BuildClientSummaryText(client),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToClientSummary(client);
    }

    /// <inheritdoc/>
    public async Task<SsoClientSummary> UpdateClientAsync(
        string tenantNId,
        string actorUserNId,
        string clientNId,
        UpdateSsoClientRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = await RequireClientAsync(tenantNId, clientNId, cancellationToken);
        var before = BuildClientSummaryText(client);

        client.ChangeProfile(
            RequireTrimmed(request.Name, "Client 名称不能为空。"),
            RequireTrimmed(request.OAuthClientId, "OAuth ClientId 不能为空。"));
        await ExecuteWriteAsync(() => _store.UpdateClientAsync(client, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken));

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.SsoClientUpdate,
                OperationObjectType.SsoClient,
                client.NId,
                before,
                BuildClientSummaryText(client),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToClientSummary(client);
    }

    /// <inheritdoc/>
    public async Task<SsoClientSummary> SetClientEnabledAsync(
        string tenantNId,
        string actorUserNId,
        string clientNId,
        SetSsoClientEnabledRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = await RequireClientAsync(tenantNId, clientNId, cancellationToken);
        var before = BuildClientSummaryText(client);

        client.SetEnabled(request.Enabled);
        await ExecuteWriteAsync(() => _store.UpdateClientAsync(client, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken));

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                request.Enabled ? OperationAction.SsoClientEnable : OperationAction.SsoClientDisable,
                OperationObjectType.SsoClient,
                client.NId,
                before,
                BuildClientSummaryText(client),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToClientSummary(client);
    }

    /// <inheritdoc/>
    public async Task<SsoClientSummary> AddClientEndpointAsync(
        string tenantNId,
        string actorUserNId,
        string clientNId,
        AddSsoEndpointRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = await RequireClientAsync(tenantNId, clientNId, cancellationToken);
        var before = BuildClientSummaryText(client);

        var endpointNId = request.NId?.Trim();
        var uri = request.Uri?.Trim();
        if (string.IsNullOrWhiteSpace(endpointNId))
        {
            throw new ValidationException("端点业务标识不能为空。");
        }

        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ValidationException("端点 URI 不能为空。");
        }

        await ExecuteWriteAsync(() =>
        {
            client.AddEndpoint(endpointNId!, ParseEndpointType(request.Type), uri!);
            return _store.UpdateClientAsync(client, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        });

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.SsoClientEndpointAdd,
                OperationObjectType.SsoClient,
                client.NId,
                before,
                BuildClientSummaryText(client),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToClientSummary(client);
    }

    /// <inheritdoc/>
    public async Task<SsoClientSummary> SetClientEndpointEnabledAsync(
        string tenantNId,
        string actorUserNId,
        string clientNId,
        string endpointNId,
        SetSsoEndpointEnabledRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = await RequireClientAsync(tenantNId, clientNId, cancellationToken);
        if (!client.Endpoints.Any(e => !e.IsDeleted && e.NId == endpointNId))
        {
            throw new ResourceNotFoundException();
        }

        var before = BuildClientSummaryText(client);

        await ExecuteWriteAsync(() =>
        {
            client.SetEndpointEnabled(endpointNId, request.Enabled);
            return _store.UpdateClientAsync(client, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        });

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.SsoClientEndpointUpdate,
                OperationObjectType.SsoClient,
                client.NId,
                before,
                BuildClientSummaryText(client),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToClientSummary(client);
    }

    /// <inheritdoc/>
    public async Task<SsoClientSummary> RemoveClientEndpointAsync(
        string tenantNId,
        string actorUserNId,
        string clientNId,
        string endpointNId,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        var client = await RequireClientAsync(tenantNId, clientNId, cancellationToken);
        if (!client.Endpoints.Any(e => !e.IsDeleted && e.NId == endpointNId))
        {
            return ToClientSummary(client);
        }

        var before = BuildClientSummaryText(client);

        await ExecuteWriteAsync(() =>
        {
            client.RemoveEndpoint(endpointNId);
            return _store.UpdateClientAsync(client, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken);
        });

        await TryWriteOperationAuditAsync(
            new OperationAuditEntry(
                tenantNId,
                actorUserNId,
                OperationAction.SsoClientEndpointRemove,
                OperationObjectType.SsoClient,
                client.NId,
                before,
                BuildClientSummaryText(client),
                TraceId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToClientSummary(client);
    }

    private async Task<IdentitySsoProvider> RequireProviderAsync(string tenantNId, string providerNId, CancellationToken cancellationToken)
    {
        var provider = await _store.FindProviderByNIdAsync(tenantNId, providerNId, cancellationToken);
        if (provider is null || provider.IsDeleted || !string.Equals(provider.TenantNId, tenantNId, StringComparison.Ordinal))
        {
            throw new SsoProviderNotFoundException();
        }

        return provider;
    }

    private async Task<IdentitySsoClient> RequireClientAsync(string tenantNId, string clientNId, CancellationToken cancellationToken)
    {
        var client = await _store.FindClientByNIdAsync(tenantNId, clientNId, cancellationToken);
        if (client is null || client.IsDeleted || !string.Equals(client.TenantNId, tenantNId, StringComparison.Ordinal))
        {
            throw new SsoClientNotFoundException();
        }

        return client;
    }

    private static ProviderSummary ToProviderSummary(IdentitySsoProvider p) => new(
        p.NId,
        p.Name,
        p.Protocol.ToString(),
        p.AuthorityOrMetadataUrl,
        p.ClientIdOrEntityId,
        p.SecretOrCertificateReference is not null,
        p.CallbackPath,
        p.Enabled,
        p.AutoRedirect,
        p.ProvisioningMode.ToString(),
        p.LogoutMode.ToString(),
        p.AllowedEmailDomains.ToList(),
        p.JitDefaultRoleNIds.ToList(),
        p.CreatedOn,
        p.LastUpdatedOn,
        p.OptimisticVersion,
        p.ConcurrencyVersion);

    private static SsoClientSummary ToClientSummary(IdentitySsoClient c) => new(
        c.NId,
        c.Name,
        c.OAuthClientId,
        c.Enabled,
        c.Endpoints
            .Where(e => !e.IsDeleted)
            .Select(e => new SsoEndpointSummary(e.NId, e.EndpointType.ToString(), e.Uri, e.Enabled))
            .ToList(),
        c.CreatedOn,
        c.LastUpdatedOn,
        c.OptimisticVersion,
        c.ConcurrencyVersion);

    /// <summary>审计摘要:仅非敏感配置字段;绝不包含密钥引用键名、DB 主键或外部主体标识。</summary>
    private static string BuildProviderSummaryText(IdentitySsoProvider p) =>
        $"name={p.Name},protocol={p.Protocol},authority={p.AuthorityOrMetadataUrl},clientId={p.ClientIdOrEntityId},callbackPath={p.CallbackPath},enabled={p.Enabled},provisioning={p.ProvisioningMode},logout={p.LogoutMode},allowedDomains={string.Join(",", p.AllowedEmailDomains)},jitRoles={string.Join(",", p.JitDefaultRoleNIds)}";

    private static string BuildClientSummaryText(IdentitySsoClient c) =>
        $"name={c.Name},oauthClientId={c.OAuthClientId},enabled={c.Enabled},endpoints={string.Join(",", c.Endpoints.Where(e => !e.IsDeleted).Select(e => $"{e.NId}:{e.EndpointType}"))}";

    private static string BuildAccountSummaryText(IdentityExternalAccount account, string userNId) =>
        $"accountNId={account.NId},userNId={userNId}";

    private static SsoProtocol ParseProtocol(string? protocol)
    {
        if (!Enum.TryParse<SsoProtocol>(protocol, ignoreCase: true, out var value))
        {
            throw new ValidationException("协议无效(仅支持 Oidc/Saml2)。");
        }

        return value;
    }

    private static SsoProvisioningMode ParseProvisioningMode(string? mode)
    {
        if (!Enum.TryParse<SsoProvisioningMode>(mode, ignoreCase: true, out var value))
        {
            throw new ValidationException("账号供给模式无效(仅支持 ExistingOnly/JustInTime)。");
        }

        return value;
    }

    private static SsoLogoutMode ParseLogoutMode(string? mode)
    {
        if (!Enum.TryParse<SsoLogoutMode>(mode, ignoreCase: true, out var value))
        {
            throw new ValidationException("注销模式无效(仅支持 LocalOnly/Federated)。");
        }

        return value;
    }

    private static SsoClientEndpointType ParseEndpointType(string? type)
    {
        if (!Enum.TryParse<SsoClientEndpointType>(type, ignoreCase: true, out var value))
        {
            throw new ValidationException("端点类型无效(仅支持 Redirect/PostLogoutRedirect/Origin)。");
        }

        return value;
    }

    private static string RequireTrimmed(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(message);
        }

        return value.Trim();
    }

    /// <summary>写操作统一异常映射:并发冲突 409、领域业务规则 400。</summary>
    private static async Task ExecuteWriteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
        catch (BusinessException ex)
        {
            throw new BusinessRuleViolationException(ex.Message);
        }
    }

    private async Task TryWriteOperationAuditAsync(OperationAuditEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await _auditSink.WriteAsync(entry, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOperationAuditWriteFailed(_logger, ex);
        }
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Error, Message = "操作审计写入失败,已忽略。")]
    private static partial void LogOperationAuditWriteFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning, Message = "IdP 连接测试失败,返回降级结果。")]
    private static partial void LogTestConnectionFailed(ILogger logger, Exception ex);

    private static string? TraceId => Activity.Current?.TraceId.ToString();
}
