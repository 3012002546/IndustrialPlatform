using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Globalization;
using Microsoft.IdentityModel.Tokens;

namespace IndustrialPlatform.Security;

/// <summary>
/// Issues the signed binding used by Collaboration preparation and Identity step-up.
/// A browser never supplies the authoritative action, scope or request hash: these
/// values are signed by the Collaboration service and revalidated by Identity.
/// </summary>
public static class StepUpBinding
{
    public const string Type = "industrial-step-up-binding+jwt";
    public const string Algorithm = SecurityAlgorithms.RsaSha256;

    public static string Create(
        RSA privateKey,
        string keyId,
        string issuer,
        string audience,
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        string action,
        string requestNId,
        string scopeChecksum,
        string requestHash,
        DateTimeOffset issuedOn,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        Require(issuer, nameof(issuer));
        Require(audience, nameof(audience));
        Require(tenantNId, nameof(tenantNId));
        Require(actorUserNId, nameof(actorUserNId));
        Require(actorSessionNId, nameof(actorSessionNId));
        Require(actorSecurityVersion, nameof(actorSecurityVersion));
        Require(action, nameof(action));
        Require(requestNId, nameof(requestNId));
        Require(scopeChecksum, nameof(scopeChecksum));
        Require(requestHash, nameof(requestHash));
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(lifetime));

        var issuedUtc = issuedOn.ToUniversalTime();
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, "collaboration"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(JwtRegisteredClaimNames.Iat, issuedUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
                new Claim("tenant_id", tenantNId.Trim()),
                new Claim("actor_user_n_id", actorUserNId.Trim()),
                new Claim("actor_session_n_id", actorSessionNId.Trim()),
                new Claim("actor_security_version", actorSecurityVersion.Trim()),
                new Claim("request_n_id", requestNId.Trim()),
                new Claim("action", action.Trim().ToLowerInvariant()),
                new Claim("scope_checksum", scopeChecksum.Trim().ToLowerInvariant()),
                new Claim("request_hash", requestHash.Trim().ToLowerInvariant()),
            ],
            issuedUtc.UtcDateTime,
            issuedUtc.Add(lifetime).UtcDateTime,
            new SigningCredentials(new RsaSecurityKey(privateKey)
            {
                KeyId = keyId,
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
            }, Algorithm));
        token.Header[JwtHeaderParameterNames.Typ] = Type;
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static ClaimsPrincipal Validate(
        string binding,
        RSA publicKey,
        string keyId,
        string issuer,
        string audience,
        DateTimeOffset now,
        out JwtSecurityToken token)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        token = handler.ReadJwtToken(binding);
        if (!string.Equals(token.Header.Typ, Type, StringComparison.Ordinal)
            || !string.Equals(token.Header.Alg, Algorithm, StringComparison.Ordinal)
            || !string.Equals(token.Header.Kid, keyId, StringComparison.Ordinal))
            throw new SecurityTokenException("step-up binding header invalid");

        var principal = handler.ValidateToken(binding, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(publicKey)
            {
                KeyId = keyId,
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
            },
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(5),
        }, out _);
        if (token.ValidTo - token.IssuedAt > TimeSpan.FromMinutes(2)
            || token.ValidTo <= now.UtcDateTime
            || !string.Equals(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), "collaboration", StringComparison.Ordinal))
            throw new SecurityTokenException("step-up binding lifetime or subject invalid");
        return principal;
    }

    private static void Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("值不能为空。", parameterName);
    }
}
