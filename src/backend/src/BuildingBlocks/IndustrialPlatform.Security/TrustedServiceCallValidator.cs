using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace IndustrialPlatform.Security;

/// <summary>验证 PF05 固定内部路径使用的短时 Collaboration 服务断言。</summary>
public sealed class TrustedServiceCallValidator
{
    public const string HeaderName = "X-Industrial-Service-Assertion";
    public const int MaxBodyBytes = 4 * 1024 * 1024;
    private static readonly string[] RequiredClaimTypes =
    ["jti", "iat", "nbf", "exp", "tenant_id", "actor_user_n_id", "actor_session_n_id", "actor_security_version", "request_n_id"];
    private readonly IConfiguration _configuration;

    public TrustedServiceCallValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsValid(HttpRequest request, string audience, string action)
    {
        if (!TryReadRequestValues(request, out var assertion, out var bodyHash))
        {
            return false;
        }

        return TryValidate(request, audience, action, assertion, bodyHash, out _);
    }

    /// <summary>
    /// Validates and atomically consumes the assertion nonce. The receiver must use this
    /// method for an internal write or read; <see cref="IsValid"/> is retained only for
    /// compatibility with existing diagnostics that can prove the request has no body.
    /// </summary>
    public async Task<TrustedServiceCallValidationResult> ValidateAsync(
        HttpRequest request,
        string audience,
        string action,
        ITrustedServiceCallNonceStore nonceStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(nonceStore);
        _ = _configuration;

        if (!request.Headers.TryGetValue(HeaderName, out var values) || values.Count != 1)
        {
            return TrustedServiceCallValidationResult.Invalid;
        }

        byte[] body;
        try
        {
            request.EnableBuffering();
            if (request.Body.CanSeek)
                request.Body.Position = 0;
            await using var copy = new MemoryStream();
            await request.Body.CopyToAsync(copy, cancellationToken);
            if (copy.Length > MaxBodyBytes)
            {
                request.Body.Position = 0;
                return TrustedServiceCallValidationResult.Invalid;
            }

            body = copy.ToArray();
            request.Body.Position = 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return TrustedServiceCallValidationResult.Invalid;
        }

        if (!TryValidate(request, audience, action, values[0] ?? string.Empty, Hash(body), out var call, bodyHashOverride: Hash(body)))
        {
            return TrustedServiceCallValidationResult.Invalid;
        }

        try
        {
            var expiresOn = call!.ExpiresOn;
            var accepted = await nonceStore.TryRegisterAsync(
                call.Issuer,
                call.Jti,
                expiresOn,
                cancellationToken);
            return accepted
                ? new TrustedServiceCallValidationResult(TrustedServiceCallValidationStatus.Valid, call)
                : TrustedServiceCallValidationResult.Replayed;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return TrustedServiceCallValidationResult.Unavailable;
        }
    }

    private static bool RequiredClaimsPresent(System.Security.Claims.ClaimsPrincipal principal) =>
        RequiredClaimTypes.All(type => !string.IsNullOrWhiteSpace(principal.FindFirst(type)?.Value));

    private static bool HasShortLifetime(JwtSecurityToken token) =>
        token.IssuedAt != DateTime.MinValue && token.ValidTo > token.IssuedAt && token.ValidTo - token.IssuedAt <= TimeSpan.FromSeconds(30);

    private static bool HasAllowedTenant(IConfigurationSection caller, string? tenantNId) =>
        !string.IsNullOrWhiteSpace(tenantNId)
        && caller.GetSection("AllowedTenantNIds").GetChildren().Select(item => item.Value).Contains(tenantNId, StringComparer.Ordinal);

    private static bool HasClaim(System.Security.Claims.ClaimsPrincipal principal, string type, string value) =>
        string.Equals(principal.FindFirst(type)?.Value, value, StringComparison.Ordinal);

    private static string CanonicalPath(HttpRequest request)
    {
        var query = request.Query.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.Select(value => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(value!)}"));
        var path = request.Path.Value?.TrimStart('/') ?? string.Empty;
        var queryString = string.Join('&', query);
        return string.IsNullOrEmpty(queryString) ? path : $"{path}?{queryString}";
    }

    private static string Hash(ReadOnlySpan<byte> body) =>
        Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

    private static bool TryReadRequestValues(HttpRequest request, out string assertion, out string bodyHash)
    {
        assertion = string.Empty;
        bodyHash = Hash([]);
        if (!request.Headers.TryGetValue(HeaderName, out var values) || values.Count != 1)
        {
            return false;
        }

        // Synchronous validation is intentionally limited to an empty body. HTTP receivers
        // should call ValidateAsync so a signed body can be read and restored safely.
        if (request.ContentLength is not null and not 0)
        {
            return false;
        }

        assertion = values[0] ?? string.Empty;
        return !string.IsNullOrWhiteSpace(assertion);
    }

    private bool TryValidate(
        HttpRequest request,
        string audience,
        string action,
        string assertion,
        string bodyHash,
        out TrustedCollaborationCall? call,
        string? bodyHashOverride = null)
    {
        call = null;
        var caller = _configuration.GetSection("TrustedServiceCalls:Callers:collaboration");
        var issuer = caller["Issuer"];
        var keyId = caller["KeyId"];
        var publicKeyPath = caller["PublicKeyPath"];
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(publicKeyPath)
            || !File.Exists(publicKeyPath))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(publicKeyPath));
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var token = handler.ReadJwtToken(assertion);
            if (!string.Equals(token.Header.Typ, "industrial-service-call+jwt", StringComparison.Ordinal)
                || !string.Equals(token.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal)
                || !string.Equals(token.Header.Kid, keyId, StringComparison.Ordinal))
            {
                return false;
            }

            var principal = handler.ValidateToken(assertion, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new RsaSecurityKey(rsa)
                {
                    KeyId = keyId,
                    // The RSA instance is request-scoped; never cache a provider that owns it.
                    CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
                },
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromSeconds(5),
            }, out _);

            if (!HasClaim(principal, "sub", "collaboration")
                || !HasClaim(principal, "action", action)
                || !HasClaim(principal, "method", request.Method.ToUpperInvariant())
                || !HasClaim(principal, "path", CanonicalPath(request))
                || !HasClaim(principal, "body_sha256", bodyHashOverride ?? bodyHash)
                || !HasAllowedTenant(caller, principal.FindFirst("tenant_id")?.Value)
                || !RequiredClaimsPresent(principal)
                || !HasShortLifetime(token))
            {
                return false;
            }

            call = new TrustedCollaborationCall(
                principal.FindFirst("tenant_id")!.Value,
                principal.FindFirst("actor_user_n_id")!.Value,
                principal.FindFirst("actor_session_n_id")!.Value,
                principal.FindFirst("actor_security_version")!.Value,
                principal.FindFirst("action")!.Value,
                principal.FindFirst("request_n_id")!.Value,
                issuer,
                principal.FindFirst("jti")!.Value,
                token.ValidTo);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException or SecurityTokenException or FormatException or IOException)
        {
            return false;
        }
    }
}

public enum TrustedServiceCallValidationStatus
{
    Invalid,
    Valid,
    Replayed,
    Unavailable,
}

public sealed record TrustedServiceCallValidationResult(
    TrustedServiceCallValidationStatus Status,
    TrustedCollaborationCall? Call = null)
{
    public static TrustedServiceCallValidationResult Invalid { get; } = new(TrustedServiceCallValidationStatus.Invalid);
    public static TrustedServiceCallValidationResult Replayed { get; } = new(TrustedServiceCallValidationStatus.Replayed);
    public static TrustedServiceCallValidationResult Unavailable { get; } = new(TrustedServiceCallValidationStatus.Unavailable);
}
