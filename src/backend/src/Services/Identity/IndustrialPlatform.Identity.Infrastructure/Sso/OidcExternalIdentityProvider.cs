using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialPlatform.Identity.Application.Sso;
using IndustrialPlatform.Identity.Domain.Sso;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IndustrialPlatform.Identity.Infrastructure.Sso;

/// <summary>
/// OpenID Connect 适配器(§26.2):授权码 + PKCE 流程。发现文档按 Authority 缓存,
/// id_token 经 JsonWebTokenHandler 校验签名/issuer/audience/lifetime/nonce(零明文 Secret 落日志)。
/// </summary>
public sealed class OidcExternalIdentityProvider : IExternalIdentityProvider
{
    private static readonly Action<ILogger, Exception?> DiscoveryFetchFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(DiscoveryFetchFailed)),
            "OIDC 发现文档获取失败,Provider 不可用(fail-closed)。");

    private static readonly Action<ILogger, Exception?> TokenExchangeFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2, nameof(TokenExchangeFailed)),
            "OIDC 令牌交换失败,回调校验失败。");

    private static readonly Action<ILogger, Exception?> IdTokenValidationFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(3, nameof(IdTokenValidationFailed)),
            "OIDC id_token 校验失败,回调校验失败。");

    private readonly ConcurrentDictionary<string, OidcDiscovery> _discoveryCache = new(StringComparer.Ordinal);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISsoSecretResolver _secretResolver;
    private readonly ILogger<OidcExternalIdentityProvider> _logger;

    /// <summary>初始化 OIDC 适配器。</summary>
    public OidcExternalIdentityProvider(
        IHttpClientFactory httpClientFactory,
        ISsoSecretResolver secretResolver,
        ILogger<OidcExternalIdentityProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(secretResolver);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _secretResolver = secretResolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public SsoProtocol Protocol => SsoProtocol.Oidc;

    /// <inheritdoc/>
    public async Task<ExternalAuthorizeResult> BuildAuthorizeUriAsync(ExternalAuthorizeContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var discovery = await GetDiscoveryAsync(context.Provider, cancellationToken);

        var uri = QueryHelpers.AddQueryString(discovery.AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = context.Provider.ClientIdOrEntityId,
            ["redirect_uri"] = context.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid profile email",
            ["state"] = context.State,
            ["nonce"] = context.Nonce,
            ["code_challenge"] = context.CodeChallenge,
            ["code_challenge_method"] = "S256",
            ["prompt"] = context.Prompt,
        });
        return new ExternalAuthorizeResult(uri);
    }

    /// <inheritdoc/>
    public async Task<ExternalLoginResult> HandleCallbackAsync(ExternalCallbackContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 1) state 防 CSRF:必须与一次性消费取回的一致。
        if (!context.Parameters.TryGetValue("state", out var state) || !string.Equals(state, context.ExpectedState, StringComparison.Ordinal))
        {
            throw new SsoCallbackValidationException();
        }

        // 2) 授权码交换:携带 code_verifier 与可选 client_secret。
        if (!context.Parameters.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            throw new SsoCallbackValidationException();
        }

        var discovery = await GetDiscoveryAsync(context.Provider, cancellationToken);
        var secret = _secretResolver.Resolve(context.Provider.SecretOrCertificateReference);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code!,
            ["redirect_uri"] = context.RedirectUri,
            ["client_id"] = context.Provider.ClientIdOrEntityId,
            ["code_verifier"] = context.CodeVerifier ?? string.Empty,
        };
        if (secret is not null)
        {
            form["client_secret"] = secret;
        }

        string idToken;
        try
        {
            using var client = _httpClientFactory.CreateClient();
            using var response = await client.PostAsync(discovery.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                TokenExchangeFailed(_logger, null);
                throw new SsoCallbackValidationException();
            }

            using var doc = JsonDocument.Parse(payload);
            idToken = doc.RootElement.GetProperty("id_token").GetString()
                ?? throw new SsoCallbackValidationException();
        }
        catch (SsoException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TokenExchangeFailed(_logger, ex);
            throw new SsoCallbackValidationException();
        }

        // 3) 校验 id_token:签名(JWKS)/issuer/audience/lifetime/nonce。
        var jwt = await ValidateIdTokenAsync(context.Provider, discovery, idToken, context.ExpectedNonce, cancellationToken);
        var externalSubject = Claim(jwt, "sub");
        if (string.IsNullOrWhiteSpace(externalSubject))
        {
            throw new SsoCallbackValidationException();
        }

        return new ExternalLoginResult(
            externalSubject!,
            FirstNonEmpty(Claim(jwt, "name"), Claim(jwt, "preferred_username")),
            Claim(jwt, "email"));
    }

    /// <inheritdoc/>
    public async Task<ExternalLogoutResult> BuildLogoutUriAsync(ExternalLogoutContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var discovery = await GetDiscoveryAsync(context.Provider, cancellationToken);
        if (string.IsNullOrWhiteSpace(discovery.EndSessionEndpoint))
        {
            return new ExternalLogoutResult(null);
        }

        var uri = QueryHelpers.AddQueryString(discovery.EndSessionEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = context.Provider.ClientIdOrEntityId,
            ["post_logout_redirect_uri"] = context.PostLogoutRedirectUri,
            ["id_token_hint"] = context.IdTokenHint,
        });
        return new ExternalLogoutResult(uri);
    }

    /// <inheritdoc/>
    public async Task<ExternalConnectionTestResult> TestConnectionAsync(ExternalConnectionTestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var discovery = await GetDiscoveryAsync(context.Provider, cancellationToken, forceRefresh: true);
            if (string.IsNullOrWhiteSpace(discovery.AuthorizationEndpoint) || string.IsNullOrWhiteSpace(discovery.TokenEndpoint))
            {
                return new ExternalConnectionTestResult(false, "发现文档缺少授权/令牌端点。");
            }

            return new ExternalConnectionTestResult(true, "发现文档获取成功,授权/令牌端点可用。");
        }
        catch (SsoException)
        {
            return new ExternalConnectionTestResult(false, "发现文档获取失败,请检查 Issuer 地址与网络连通性。");
        }
    }

    private async Task<JsonWebToken> ValidateIdTokenAsync(
        IdentitySsoProvider provider,
        OidcDiscovery discovery,
        string idToken,
        string? expectedNonce,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            var jwksJson = await client.GetStringAsync(discovery.JwksUri, cancellationToken);
            var jwks = new JsonWebKeySet(jwksJson);
            var handler = new JsonWebTokenHandler();
            var result = await handler.ValidateTokenAsync(idToken, new TokenValidationParameters
            {
                ValidIssuer = discovery.Issuer,
                ValidAudience = provider.ClientIdOrEntityId,
                IssuerSigningKeys = jwks.Keys,
                ValidateIssuerSigningKey = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ValidateTokenReplay = false,
                ClockSkew = TimeSpan.FromSeconds(90),
            });

            var jwt = result.SecurityToken as JsonWebToken;
            if (!result.IsValid || jwt is null)
            {
                IdTokenValidationFailed(_logger, null);
                throw new SsoCallbackValidationException();
            }

            // 4) nonce 防重放:必须与预存一致。
            var nonce = Claim(jwt, "nonce");
            if (!string.IsNullOrEmpty(expectedNonce) && !string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
            {
                IdTokenValidationFailed(_logger, null);
                throw new SsoCallbackValidationException();
            }

            return jwt;
        }
        catch (SsoException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            IdTokenValidationFailed(_logger, ex);
            throw new SsoCallbackValidationException();
        }
    }

    private async Task<OidcDiscovery> GetDiscoveryAsync(
        IdentitySsoProvider provider,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        var authority = provider.AuthorityOrMetadataUrl;
        if (!forceRefresh && _discoveryCache.TryGetValue(authority, out var cached))
        {
            return cached;
        }

        var wellKnown = authority.TrimEnd('/') + "/.well-known/openid-configuration";
        try
        {
            using var client = _httpClientFactory.CreateClient();
            var payload = await client.GetFromJsonAsync<JsonElement>(wellKnown, cancellationToken);
            var discovery = new OidcDiscovery(
                GetString(payload, "issuer"),
                GetString(payload, "authorization_endpoint") ?? string.Empty,
                GetString(payload, "token_endpoint") ?? string.Empty,
                GetString(payload, "end_session_endpoint"),
                GetString(payload, "jwks_uri") ?? string.Empty);

            if (string.IsNullOrWhiteSpace(discovery.Issuer)
                || string.IsNullOrWhiteSpace(discovery.AuthorizationEndpoint)
                || string.IsNullOrWhiteSpace(discovery.TokenEndpoint)
                || string.IsNullOrWhiteSpace(discovery.JwksUri))
            {
                throw new SsoCallbackValidationException();
            }

            _discoveryCache[authority] = discovery;
            return discovery;
        }
        catch (SsoException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DiscoveryFetchFailed(_logger, ex);
            throw new SsoProviderUnavailableException();
        }
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Claim(JsonWebToken token, string name)
        => token.TryGetPayloadValue(name, out string? value) ? value : null;

    private static string? FirstNonEmpty(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first : second;

    /// <summary>OIDC 发现文档投影(只保留本用例所需端点)。</summary>
    private sealed record OidcDiscovery(
        string? Issuer,
        string AuthorizationEndpoint,
        string TokenEndpoint,
        string? EndSessionEndpoint,
        string JwksUri);
}
