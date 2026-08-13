using System.Diagnostics;
using System.Security.Cryptography;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Contracts.Authentication;
using IndustrialPlatform.Identity.Contracts.Sso;
using ContractsEvents = IndustrialPlatform.Identity.Contracts.Events;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Domain.Sso;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Identity.Application.Sso;

/// <summary>
/// SSO 认证用例实现(§26)。编排 SSO 存储、票据存储、外部 IdP 适配器与认证端口;
/// state/票据/句柄/PKCE verifier 等敏感值仅在服务端内存与 Redis 流转,绝不进入日志或消息。
/// </summary>
public sealed partial class SsoService : ISsoService
{
    private static readonly SsoBeginResponse NotReused = new(
        Reused: false,
        Ticket: null,
        ReturnUrl: null,
        NeedsSelection: false,
        ProviderNId: null,
        AuthorizeUri: null,
        Providers: null);

    private readonly ISsoStore _store;
    private readonly ISsoTicketStore _ticketStore;
    private readonly IExternalIdentityProviderFactory _providerFactory;
    private readonly IAuthenticationStore _authStore;
    private readonly IManagementStore _managementStore;
    private readonly IAccessTokenFactory _tokenFactory;
    private readonly IRefreshSessionStore _refreshStore;
    private readonly ISessionRevocationStore _sessionRevocation;
    private readonly ILoginAuditSink _auditSink;
    private readonly IPasswordHasher _hasher;
    private readonly IOptions<SsoOptions> _ssoOptions;
    private readonly IOptions<AuthenticationOptions> _authOptions;
    private readonly ILogger<SsoService> _logger;

    public SsoService(
        ISsoStore store,
        ISsoTicketStore ticketStore,
        IExternalIdentityProviderFactory providerFactory,
        IAuthenticationStore authStore,
        IManagementStore managementStore,
        IAccessTokenFactory tokenFactory,
        IRefreshSessionStore refreshStore,
        ISessionRevocationStore sessionRevocation,
        ILoginAuditSink auditSink,
        IPasswordHasher hasher,
        IOptions<SsoOptions> ssoOptions,
        IOptions<AuthenticationOptions> authOptions,
        ILogger<SsoService> logger)
    {
        _store = store;
        _ticketStore = ticketStore;
        _providerFactory = providerFactory;
        _authStore = authStore;
        _managementStore = managementStore;
        _tokenFactory = tokenFactory;
        _refreshStore = refreshStore;
        _sessionRevocation = sessionRevocation;
        _auditSink = auditSink;
        _hasher = hasher;
        _ssoOptions = ssoOptions;
        _authOptions = authOptions;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SsoDiscoveryProvider>> DiscoveryAsync(string? connection, CancellationToken cancellationToken)
    {
        var tenantNId = _authOptions.Value.DefaultTenantNId;
        var providers = await _store.ListEnabledProvidersAsync(tenantNId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(connection))
        {
            var keyword = connection.Trim();
            providers = providers
                .Where(p => p.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return providers
            .Select(p => new SsoDiscoveryProvider(p.NId, p.Name, p.Protocol.ToString(), p.AutoRedirect))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<SsoBeginResponse> BeginAuthorizeAsync(
        string tenantNId,
        string? clientId,
        string? returnUrl,
        string? providerNId,
        string requestBaseUrl,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var client = await ResolveClientAsync(tenantNId, clientId, cancellationToken);
        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl, client);

        IdentitySsoProvider? provider;
        if (string.IsNullOrWhiteSpace(providerNId))
        {
            var enabled = await _store.ListEnabledProvidersAsync(tenantNId, cancellationToken);
            if (enabled.Count == 1 && enabled[0].AutoRedirect)
            {
                provider = enabled[0];
            }
            else
            {
                return new SsoBeginResponse(
                    Reused: false,
                    Ticket: null,
                    ReturnUrl: normalizedReturnUrl,
                    NeedsSelection: true,
                    ProviderNId: null,
                    AuthorizeUri: null,
                    Providers: enabled
                        .Select(p => new SsoDiscoveryProvider(p.NId, p.Name, p.Protocol.ToString(), p.AutoRedirect))
                        .ToList());
            }
        }
        else
        {
            provider = await RequireEnabledProviderAsync(tenantNId, providerNId!, cancellationToken);
        }

        // 预存 OAuth state:随机 state 只作 Redis 键;nonce/verifier 由 IdP 回调时回传验证。
        var adapter = _providerFactory.GetFor(provider.Protocol);
        var state = SsoRandom.Base64UrlToken(32);
        var nonce = SsoRandom.Base64UrlToken(32);
        var codeVerifier = SsoRandom.Base64UrlToken(48);
        var codeChallenge = SsoRandom.Sha256Base64Url(codeVerifier);
        var redirectUri = BuildCallbackUri(requestBaseUrl, provider.CallbackPath);

        await _ticketStore.StoreOAuthStateAsync(
            state,
            new SsoOAuthState(
                provider.NId,
                tenantNId,
                nonce,
                codeChallenge,
                "S256",
                codeVerifier,
                redirectUri,
                normalizedReturnUrl,
                now),
            _ssoOptions.Value.OAuthStateLifetime,
            cancellationToken);

        var result = await adapter.BuildAuthorizeUriAsync(
            new ExternalAuthorizeContext(provider, state, nonce, codeChallenge, redirectUri),
            cancellationToken);

        return new SsoBeginResponse(
            Reused: false,
            Ticket: null,
            ReturnUrl: normalizedReturnUrl,
            NeedsSelection: false,
            ProviderNId: provider.NId,
            AuthorizeUri: result.RedirectUri,
            Providers: null);
    }

    /// <inheritdoc/>
    public async Task<SsoBeginResponse> ReuseBrowserSessionAsync(
        string sessionHandle,
        string tenantNId,
        string? clientId,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var client = await ResolveClientAsync(tenantNId, clientId, cancellationToken);
        var normalizedReturnUrl = NormalizeReturnUrl(returnUrl, client);
        var now = DateTimeOffset.UtcNow;

        var session = await _store.FindBrowserSessionByHandleAsync(sessionHandle, cancellationToken);
        if (session is null || !string.Equals(session.TenantNId, tenantNId, StringComparison.Ordinal))
        {
            return NotReused;
        }

        // 过期会话顺手按 Expired 撤销,释放资源。
        if (session.RevokedOn is null && !session.IsValid(now))
        {
            var revokeOptimistic = session.OptimisticVersion;
            var revokeConcurrency = session.ConcurrencyVersion;
            session.Revoke(SsoBrowserSessionRevokeReason.Expired, now);
            await _store.UpdateBrowserSessionAsync(session, revokeOptimistic, revokeConcurrency, cancellationToken);
        }

        if (!session.IsValid(now))
        {
            return NotReused;
        }

        var account = await _authStore.FindByUserIdAsync(session.UserId, cancellationToken);
        var user = account?.User;
        if (user is null || user.IsDeleted || user.Status == UserStatus.Disabled || user.AuthVersion != session.AuthVersion)
        {
            return NotReused;
        }

        var expectedOptimistic = session.OptimisticVersion;
        var expectedConcurrency = session.ConcurrencyVersion;
        session.TouchActivity(now);
        await _store.UpdateBrowserSessionAsync(session, expectedOptimistic, expectedConcurrency, cancellationToken);

        // 复用会话直接签发一次性票据,无需再走 IdP。
        var ticket = SsoRandom.Base64UrlToken(32);
        await _ticketStore.StoreLoginTicketAsync(
            ticket,
            new SsoLoginTicket(session.ProviderNId, tenantNId, user.NId, normalizedReturnUrl, sessionHandle, now),
            _ssoOptions.Value.LoginTicketLifetime,
            cancellationToken);

        return new SsoBeginResponse(
            Reused: true,
            Ticket: ticket,
            ReturnUrl: normalizedReturnUrl,
            NeedsSelection: false,
            ProviderNId: session.ProviderNId,
            AuthorizeUri: null,
            Providers: null);
    }

    /// <inheritdoc/>
    public async Task<SsoCallbackResult> HandleOidcCallbackAsync(
        string providerNId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var tenantNId = _authOptions.Value.DefaultTenantNId;
        var provider = await RequireEnabledProviderAsync(tenantNId, providerNId, cancellationToken);
        if (provider.Protocol != SsoProtocol.Oidc)
        {
            throw new SsoCallbackValidationException();
        }

        if (!parameters.TryGetValue("state", out var state) || string.IsNullOrWhiteSpace(state)
            || !parameters.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            throw new SsoStateInvalidException();
        }

        var stored = await _ticketStore.ConsumeOAuthStateAsync(state!, cancellationToken);
        if (stored is null || !string.Equals(stored.ProviderNId, provider.NId, StringComparison.Ordinal))
        {
            throw new SsoStateInvalidException();
        }

        // PKCE 防篡改:回调 state 已一次性消费,再校验 verifier 摘要与预存 challenge 一致。
        var recomputedChallenge = SsoRandom.Sha256Base64Url(stored.CodeVerifier);
        if (!string.Equals(recomputedChallenge, stored.CodeChallenge, StringComparison.Ordinal))
        {
            throw new SsoCallbackValidationException();
        }

        var adapter = _providerFactory.GetFor(provider.Protocol);
        ExternalLoginResult external;
        try
        {
            external = await adapter.HandleCallbackAsync(
                new ExternalCallbackContext(provider, parameters, state!, stored.Nonce, stored.CodeVerifier, stored.RedirectUri),
                cancellationToken);
        }
        catch (SsoException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProviderUnavailable(_logger, ex);
            throw new SsoProviderUnavailableException();
        }

        return await ResolveLoginAsync(provider, stored.ReturnUrl, external, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SsoCallbackResult> HandleSamlCallbackAsync(
        string providerNId,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var tenantNId = _authOptions.Value.DefaultTenantNId;
        var provider = await RequireEnabledProviderAsync(tenantNId, providerNId, cancellationToken);
        if (provider.Protocol != SsoProtocol.Saml2)
        {
            throw new SsoCallbackValidationException();
        }

        if (!parameters.TryGetValue("SAMLResponse", out var samlResponse) || string.IsNullOrWhiteSpace(samlResponse))
        {
            throw new SsoCallbackValidationException();
        }

        // SAML 以 RelayState 携带 state(随机不透明值),一次性消费。
        if (!parameters.TryGetValue("RelayState", out var relayState) || string.IsNullOrWhiteSpace(relayState))
        {
            throw new SsoStateInvalidException();
        }

        var stored = await _ticketStore.ConsumeOAuthStateAsync(relayState!, cancellationToken);
        if (stored is null || !string.Equals(stored.ProviderNId, provider.NId, StringComparison.Ordinal))
        {
            throw new SsoStateInvalidException();
        }

        var adapter = _providerFactory.GetFor(provider.Protocol);
        ExternalLoginResult external;
        try
        {
            external = await adapter.HandleCallbackAsync(
                new ExternalCallbackContext(provider, parameters, relayState!, null, stored.CodeVerifier, stored.RedirectUri),
                cancellationToken);
        }
        catch (SsoException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProviderUnavailable(_logger, ex);
            throw new SsoProviderUnavailableException();
        }

        return await ResolveLoginAsync(provider, stored.ReturnUrl, external, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SsoExchangeResponse> ExchangeTicketAsync(
        string ticket,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            throw new SsoTicketInvalidException();
        }

        var stored = await _ticketStore.ConsumeLoginTicketAsync(ticket!, cancellationToken);
        if (stored is null)
        {
            throw new SsoTicketInvalidException();
        }

        var now = DateTimeOffset.UtcNow;
        var account = await _authStore.FindByNIdAsync(stored.UserNId, cancellationToken);
        var user = account?.User;
        if (user is null || user.IsDeleted || user.Status == UserStatus.Disabled)
        {
            throw new SsoTicketInvalidException();
        }

        var session = await _store.FindBrowserSessionByHandleAsync(stored.BrowserSessionHandle, cancellationToken);
        if (session is null || !string.Equals(session.TenantNId, stored.TenantNId, StringComparison.Ordinal) || !session.IsValid(now))
        {
            throw new SsoTicketInvalidException();
        }

        // 安全版本漂移:撤销浏览器会话并拒绝(旧 Access Token 由 sid/ver 失效)。
        if (session.AuthVersion != user.AuthVersion)
        {
            var revokeOptimistic = session.OptimisticVersion;
            var revokeConcurrency = session.ConcurrencyVersion;
            session.Revoke(SsoBrowserSessionRevokeReason.Security, now);
            await _store.UpdateBrowserSessionAsync(session, revokeOptimistic, revokeConcurrency, cancellationToken);
            throw new SsoTicketInvalidException();
        }

        var expectedOptimistic = session.OptimisticVersion;
        var expectedConcurrency = session.ConcurrencyVersion;
        session.TouchActivity(now);
        await _store.UpdateBrowserSessionAsync(session, expectedOptimistic, expectedConcurrency, cancellationToken);

        var authSession = await IssueSessionAsync(account!, user, clientIp, userAgent, now, cancellationToken);
        return new SsoExchangeResponse(authSession, stored.ReturnUrl, stored.BrowserSessionHandle);
    }

    /// <inheritdoc/>
    public async Task<SsoLogoutResponse> LogoutAsync(
        string? sessionHandle,
        string? refreshToken,
        string? clientId,
        string? postLogoutRedirectUri,
        string? accessTokenSessionNId,
        TimeSpan sidRevocationTtl,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // 1) 撤销刷新会话族(幂等,不暴露刷新令牌是否存在)。
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var session = await _refreshStore.FindByRawTokenAsync(refreshToken!, cancellationToken);
            if (session is not null)
            {
                await _refreshStore.RevokeFamilyAsync(session.FamilyNId, "logout", cancellationToken);
            }
        }

        // 2) sid 撤销直到 Access Token 到期(Redis 尽力而为,数据库撤销为权威)。
        if (!string.IsNullOrWhiteSpace(accessTokenSessionNId))
        {
            await _sessionRevocation.RevokeAsync(accessTokenSessionNId!, sidRevocationTtl, cancellationToken);
        }

        // 3) 撤销浏览器 SSO 会话。
        IdentitySsoBrowserSession? browserSession = null;
        if (!string.IsNullOrWhiteSpace(sessionHandle))
        {
            var found = await _store.FindBrowserSessionByHandleAsync(sessionHandle!, cancellationToken);
            if (found is not null && found.RevokedOn is null)
            {
                var expectedOptimistic = found.OptimisticVersion;
                var expectedConcurrency = found.ConcurrencyVersion;
                found.Revoke(SsoBrowserSessionRevokeReason.Logout, now);
                await _store.UpdateBrowserSessionAsync(found, expectedOptimistic, expectedConcurrency, cancellationToken);
                browserSession = found;
            }
        }

        // 4) 联邦登出:仅当浏览器会话由客户 IdP 建立且配置 Federated 时跳转 IdP。
        if (browserSession is not null)
        {
            var provider = await _store.FindProviderByNIdAsync(browserSession.TenantNId, browserSession.ProviderNId, cancellationToken);
            if (provider is not null && !provider.IsDeleted && provider.Enabled && provider.LogoutMode == SsoLogoutMode.Federated)
            {
                var client = await ResolveClientAsync(browserSession.TenantNId, clientId, cancellationToken);
                if (client is not null)
                {
                    var normalizedPostLogout = NormalizePostLogoutRedirectUri(postLogoutRedirectUri, client);
                    var adapter = _providerFactory.GetFor(provider.Protocol);
                    var logout = await adapter.BuildLogoutUriAsync(
                        new ExternalLogoutContext(provider, null, normalizedPostLogout),
                        cancellationToken);
                    return new SsoLogoutResponse(logout.RedirectUri, logout.RedirectUri is not null);
                }
            }
        }

        return new SsoLogoutResponse(null, false);
    }

    /// <summary>解析外部身份并按绑定状态/供给策略登录,签发一次性票据。</summary>
    private async Task<SsoCallbackResult> ResolveLoginAsync(
        IdentitySsoProvider provider,
        string? returnUrl,
        ExternalLoginResult external,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var tenantNId = provider.TenantNId;

        // 1) 已绑定外部账号:校验用户状态、更新外部资料并记录登录。
        var existing = await _store.FindExternalAccountAsync(provider.Id, external.ExternalSubject, cancellationToken);
        if (existing is not null)
        {
            var account = await _authStore.FindByUserIdAsync(existing.UserId, cancellationToken);
            var user = account?.User;
            if (user is null || user.IsDeleted || user.Status == UserStatus.Disabled)
            {
                throw new SsoUserUnavailableException();
            }

            existing.UpdateProfile(external.ExternalName, external.ExternalEmail);
            existing.RecordLogin(now);
            var accountOptimistic = existing.OptimisticVersion;
            var accountConcurrency = existing.ConcurrencyVersion;
            await _store.UpdateExternalAccountAsync(existing, accountOptimistic, accountConcurrency, cancellationToken);

            return await CompleteLoginAsync(provider, account!, user, returnUrl, now, cancellationToken);
        }

        // 2) 未绑定:按供给策略处理。
        if (provider.ProvisioningMode != SsoProvisioningMode.JustInTime)
        {
            throw new SsoAccountNotLinkedException();
        }

        if (!provider.IsEmailAllowed(external.ExternalEmail))
        {
            throw new SsoJitNotAllowedException();
        }

        var email = external.ExternalEmail!.Trim();
        var matched = await _authStore.FindByNormalizedLoginNameAsync(tenantNId, email.ToUpperInvariant(), cancellationToken);

        // 2a) 邮箱已存在且可用:直接建立外部账号映射。
        if (matched is not null && !matched.User.IsDeleted && matched.User.Status != UserStatus.Disabled)
        {
            var linked = IdentityExternalAccount.Create(
                GenerateAccountNId(),
                provider.Id,
                provider.IsDeleted,
                external.ExternalSubject,
                matched.User.Id,
                matched.User.IsDeleted,
                external.ExternalName,
                external.ExternalEmail);
            linked.RecordLogin(now);
            await _store.AddExternalAccountAsync(linked, cancellationToken);

            return await CompleteLoginAsync(provider, matched, matched.User, returnUrl, now, cancellationToken);
        }

        // 2b) 邮箱不存在:JIT 创建本地用户(分配默认角色)+ 映射 + Outbox 同事务。
        var created = CreateJitUser(provider, external);
        var roles = await _managementStore.GetRolesByNIdsAsync(provider.JitDefaultRoleNIds, cancellationToken);
        var defaultRoles = roles.Where(r => r.TenantNId == tenantNId && !r.IsDeleted).ToList();
        foreach (var role in defaultRoles)
        {
            created.AssignRole(role);
        }

        var jitAccount = IdentityExternalAccount.Create(
            GenerateAccountNId(),
            provider.Id,
            provider.IsDeleted,
            external.ExternalSubject,
            created.Id,
            created.IsDeleted,
            external.ExternalName,
            external.ExternalEmail);
        jitAccount.RecordLogin(now);

        await _store.AddJitUserAsync(created, jitAccount, BuildOutboxEnvelopes(created), cancellationToken);
        created.ClearDomainEvents();

        var createdAccount = new AuthenticatedUser(
            created,
            defaultRoles.Select(r => r.Id).ToList(),
            defaultRoles.Select(r => r.NId).ToList());

        return await CompleteLoginAsync(provider, createdAccount, created, returnUrl, now, cancellationToken);
    }

    /// <summary>记录本地用户登录成功、建立浏览器会话并签发一次性票据。</summary>
    private async Task<SsoCallbackResult> CompleteLoginAsync(
        IdentitySsoProvider provider,
        AuthenticatedUser account,
        User user,
        string? returnUrl,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // 记录本地用户登录成功(双版本并发)。
        var expectedOptimistic = user.OptimisticVersion;
        var expectedConcurrency = user.ConcurrencyVersion;
        user.RecordLoginSuccess(now);
        await _authStore.UpdateUserAsync(user, expectedOptimistic, expectedConcurrency, cancellationToken);

        // 建立浏览器 SSO 会话,句柄进 HttpOnly Cookie(服务器只保存哈希)。
        var created = await _store.CreateBrowserSessionAsync(
            provider.TenantNId,
            provider.NId,
            user.Id,
            user.IsDeleted,
            user.AuthVersion,
            cancellationToken);

        // 签发一次性票据,由前端跳转携带并交换。
        var ticket = SsoRandom.Base64UrlToken(32);
        await _ticketStore.StoreLoginTicketAsync(
            ticket,
            new SsoLoginTicket(provider.NId, provider.TenantNId, user.NId, returnUrl, created.SessionHandle, now),
            _ssoOptions.Value.LoginTicketLifetime,
            cancellationToken);

        return new SsoCallbackResult(ticket, returnUrl, created.SessionHandle);
    }

    /// <summary>签发完整认证会话:刷新会话落库、签发 Access Token 并写登录审计。</summary>
    private async Task<AuthSession> IssueSessionAsync(
        AuthenticatedUser account,
        User user,
        string? clientIp,
        string? userAgent,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sessionNId = GenerateSessionNId();
        var rawRefreshToken = GenerateRefreshToken();
        try
        {
            await _refreshStore.AddAsync(
                new NewRefreshSession(
                    user.TenantNId,
                    sessionNId,
                    sessionNId,
                    user.Id,
                    rawRefreshToken,
                    now.AddDays(_authOptions.Value.RefreshTokenLifetimeDays),
                    clientIp,
                    userAgent,
                    now),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRefreshSessionPersistFailed(_logger, ex);
            throw new SecurityStoreUnavailableException();
        }

        var accessToken = _tokenFactory.Create(new AccessTokenDescriptor(
            user.NId,
            user.LoginName,
            user.TenantNId,
            account.RoleNIds,
            sessionNId,
            user.AuthVersion,
            now));

        var authUser = await BuildAuthUserAsync(account, cancellationToken);

        await TryWriteAuditAsync(
            new LoginAuditEntry(user.TenantNId, user.NId, user.LoginName, true, null, clientIp, userAgent, TraceId, now),
            cancellationToken);

        return new AuthSession(accessToken.Token, rawRefreshToken, accessToken.ExpiresAt, authUser);
    }

    private async Task<AuthUser> BuildAuthUserAsync(AuthenticatedUser account, CancellationToken cancellationToken)
    {
        var permissions = await _authStore.GetPermissionsForRolesAsync(account.RoleIds, cancellationToken);
        var permissionNIds = permissions
            .Select(p => p.NId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return new AuthUser(
            account.User.NId,
            account.User.LoginName,
            account.User.Name,
            account.User.TenantNId,
            account.RoleNIds,
            permissionNIds);
    }

    /// <summary>JIT 创建本地用户:随机不可用密码哈希(用户仅能经 SSO 登录),Email 即登录名。</summary>
    private User CreateJitUser(IdentitySsoProvider provider, ExternalLoginResult external)
    {
        var email = external.ExternalEmail!.Trim();
        var name = string.IsNullOrWhiteSpace(external.ExternalName) ? email : external.ExternalName!.Trim();
        var passwordHash = _hasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        return User.Create(provider.TenantNId, GenerateUserNId(), email, name, email, phone: null, passwordHash);
    }

    private async Task TryWriteAuditAsync(LoginAuditEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await _auditSink.WriteAsync(entry, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditWriteFailed(_logger, ex);
        }
    }

    private async Task<IdentitySsoClient?> ResolveClientAsync(string tenantNId, string? clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        var client = await _store.FindClientByOAuthClientIdAsync(tenantNId, clientId!, cancellationToken);
        if (client is null || client.IsDeleted || !client.Enabled)
        {
            throw new SsoClientNotFoundException();
        }

        return client;
    }

    private async Task<IdentitySsoProvider> RequireEnabledProviderAsync(string tenantNId, string providerNId, CancellationToken cancellationToken)
    {
        var provider = await _store.FindProviderByNIdAsync(tenantNId, providerNId, cancellationToken);
        if (provider is null || provider.IsDeleted)
        {
            throw new SsoProviderNotFoundException();
        }

        if (!provider.Enabled)
        {
            throw new SsoProviderDisabledException();
        }

        return provider;
    }

    /// <summary>returnUrl 校验:站内相对地址(非 // 协议相对)恒允许;绝对地址须精确匹配已注册 Redirect 端点。</summary>
    private static string NormalizeReturnUrl(string? returnUrl, IdentitySsoClient? client)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        var trimmed = returnUrl.Trim();
        if (trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (client is not null && client.HasEndpoint(SsoClientEndpointType.Redirect, trimmed))
        {
            return trimmed;
        }

        throw new SsoReturnUrlInvalidException();
    }

    /// <summary>postLogoutRedirectUri 校验:站内相对地址恒允许;绝对地址须精确匹配 PostLogoutRedirect 端点。</summary>
    private static string NormalizePostLogoutRedirectUri(string? uri, IdentitySsoClient client)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new SsoLogoutInvalidException();
        }

        var trimmed = uri.Trim();
        if (trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (client.HasEndpoint(SsoClientEndpointType.PostLogoutRedirect, trimmed))
        {
            return trimmed;
        }

        throw new SsoLogoutInvalidException();
    }

    private static string BuildCallbackUri(string requestBaseUrl, string callbackPath)
        => requestBaseUrl.TrimEnd('/') + (callbackPath.StartsWith('/') ? callbackPath : "/" + callbackPath);

    /// <summary>把聚合上尚未派发的领域事件映射为 Outbox 信封(§20,版本化事件)。</summary>
    private static OutboxEnvelope[] BuildOutboxEnvelopes(User user)
    {
        if (user.DomainEvents.Count == 0)
        {
            return [];
        }

        return user.DomainEvents
            .Select(MapToEnvelope)
            .Where(e => e is not null)
            .Select(e => e!)
            .ToArray();
    }

    private static OutboxEnvelope? MapToEnvelope(IDomainEvent domainEvent) => domainEvent switch
    {
        UserCreatedEvent e => CreateEnvelope(new ContractsEvents.UserCreatedEvent(e.TenantNId, e.UserNId, e.AuthVersion)),
        UserRolesChangedEvent e => CreateEnvelope(new ContractsEvents.UserRolesChangedEvent(e.TenantNId, e.UserNId, e.RoleNId)),
        _ => null,
    };

    private static OutboxEnvelope CreateEnvelope<TEvent>(TEvent integrationEvent)
        where TEvent : ContractsEvents.IdentityIntegrationEvent
        => new(
            integrationEvent.EventId,
            integrationEvent.EventTypeName,
            integrationEvent.EventVersion,
            ContractsEvents.IntegrationEventJson.Serialize(integrationEvent),
            integrationEvent.CreatedTime);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Error, Message = "刷新会话持久化失败,登录终止。")]
    private static partial void LogRefreshSessionPersistFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Error, Message = "登录审计写入失败,已忽略。")]
    private static partial void LogAuditWriteFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "IdP 调用失败(不可达或协议错误),映射为安全存储不可用。")]
    private static partial void LogProviderUnavailable(ILogger logger, Exception ex);

    private static string? TraceId => Activity.Current?.TraceId.ToString();

    private static string GenerateSessionNId() => SsoRandom.NId("SES-");

    private static string GenerateUserNId() => SsoRandom.NId("USR-");

    private static string GenerateAccountNId() => SsoRandom.NId("SSOA-");

    private static string GenerateRefreshToken() => SsoRandom.Base64UrlToken(32);
}
