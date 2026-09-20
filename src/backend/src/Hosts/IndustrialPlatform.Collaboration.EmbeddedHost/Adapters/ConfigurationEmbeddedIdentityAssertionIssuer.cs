using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Adapters;

/// <summary>使用配置中的签名密钥签发和校验身份断言；真实接入需替换演示密钥。</summary>
public sealed class ConfigurationEmbeddedIdentityAssertionIssuer : IEmbeddedIdentityAssertionIssuer, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, RSA> _privateKeys = new(StringComparer.Ordinal);

    public ConfigurationEmbeddedIdentityAssertionIssuer(IConfiguration configuration) => _configuration = configuration;

    public void Dispose()
    {
        foreach (var key in _privateKeys.Values)
            key.Dispose();
        _privateKeys.Clear();
    }

    public string Issue(EmbeddedSourcePrincipal source, string nonce, DateTimeOffset issuedOn, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromSeconds(60))
            throw new EmbeddedHandshakeException(503, "EMBEDDED_IDENTITY_NOT_READY", "嵌入身份断言有效期配置无效。");
        var settings = ResolveBySource(source.SourceNId);
        var privateKey = _privateKeys.GetOrAdd(settings.SourceNId, _ => LoadKey(settings, requirePrivate: true));
        var issuedUtc = issuedOn.ToUniversalTime();
        var signingKey = new RsaSecurityKey(privateKey) { KeyId = settings.KeyId };
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, source.ExternalSubject.Trim()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(issuedUtc.UtcDateTime).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("external_tenant_n_id", source.ExternalTenantNId.Trim()),
            new("display_name", source.DisplayName.Trim()),
            new("security_version", source.SecurityVersion.Trim()),
            new("source_session_n_id", source.SessionNId.Trim()),
            new("nonce", nonce.Trim()),
        };
        if (!string.IsNullOrWhiteSpace(source.AccountNId))
            claims.Add(new Claim("account_n_id", source.AccountNId.Trim()));
        var token = new JwtSecurityToken(
            settings.Issuer,
            settings.Audience,
            claims,
            issuedUtc.UtcDateTime,
            issuedUtc.Add(lifetime).UtcDateTime,
            new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
        token.Header[JwtHeaderParameterNames.Typ] = "industrial-embedded-identity+jwt";
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public EmbeddedIdentityAssertion Validate(string assertion, DateTimeOffset now)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var token = handler.ReadJwtToken(assertion);
        if (!string.Equals(token.Header.Typ, "industrial-embedded-identity+jwt", StringComparison.Ordinal) || !string.Equals(token.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
            throw new SecurityTokenException("embedded assertion header invalid");
        var settings = ResolveByIssuer(token.Issuer, token.Header.Kid);
        using var publicKey = LoadKey(settings, requirePrivate: false);
        var principal = handler.ValidateToken(assertion, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(publicKey)
            {
                KeyId = settings.KeyId,
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
            },
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(5),
        }, out _);
        var issuedOn = token.IssuedAt;
        var expiresOn = token.ValidTo;
        if (issuedOn > now.UtcDateTime.AddSeconds(5) || expiresOn - issuedOn > TimeSpan.FromSeconds(60) || expiresOn <= now.UtcDateTime)
            throw new SecurityTokenException("embedded assertion lifetime invalid");

        var externalSubject = Required(principal, JwtRegisteredClaimNames.Sub, 256);
        var externalTenant = Required(principal, "external_tenant_n_id", 128);
        var displayName = Required(principal, "display_name", 200);
        var securityVersion = Required(principal, "security_version", 128);
        var sourceSession = Required(principal, "source_session_n_id", 128);
        var nonce = Required(principal, "nonce", 256);
        var jti = Required(principal, JwtRegisteredClaimNames.Jti, 128);
        var account = principal.FindFirst("account_n_id")?.Value;
        if (account is not null && (string.IsNullOrWhiteSpace(account) || account.Length > 128))
            throw new SecurityTokenException("embedded account invalid");
        return new EmbeddedIdentityAssertion(settings.SourceNId, settings.Issuer, externalSubject, externalTenant, displayName, securityVersion, sourceSession, nonce, jti, new DateTimeOffset(expiresOn, TimeSpan.Zero), account);
    }

    private EmbeddedIdentitySourceSettings ResolveBySource(string sourceNId) => ReadSettings(sourceNId, _configuration.GetSection($"EmbeddedCollaboration:Sources:{sourceNId}"));

    private EmbeddedIdentitySourceSettings ResolveByIssuer(string? issuer, string? keyId)
    {
        foreach (var source in _configuration.GetSection("EmbeddedCollaboration:Sources").GetChildren())
        {
            var settings = ReadSettings(source.Key, source);
            if (string.Equals(settings.Issuer, issuer, StringComparison.Ordinal) && string.Equals(settings.KeyId, keyId, StringComparison.Ordinal))
                return settings;
        }
        throw new SecurityTokenException("embedded assertion issuer or kid invalid");
    }

    private static EmbeddedIdentitySourceSettings ReadSettings(string sourceNId, IConfigurationSection section)
    {
        var issuer = Required(section["Issuer"], "issuer");
        var audience = Required(section["Audience"], "audience");
        var keyId = Required(section["KeyId"], "keyId");
        return new EmbeddedIdentitySourceSettings(sourceNId, issuer, audience, keyId, section["PrivateKeyPath"], section["PrivateKey"], section["PublicKeyPath"], section["PublicKey"]);
    }

    private static RSA LoadKey(EmbeddedIdentitySourceSettings settings, bool requirePrivate)
    {
        var pem = requirePrivate
            ? !string.IsNullOrWhiteSpace(settings.PrivateKeyPath) ? File.ReadAllText(settings.PrivateKeyPath) : settings.PrivateKey
            : !string.IsNullOrWhiteSpace(settings.PublicKeyPath) ? File.ReadAllText(settings.PublicKeyPath) : settings.PublicKey;
        if (string.IsNullOrWhiteSpace(pem))
            throw new EmbeddedHandshakeException(503, "EMBEDDED_IDENTITY_NOT_READY", "嵌入身份签名密钥未配置。");
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or IOException)
        {
            rsa.Dispose();
            throw new EmbeddedHandshakeException(503, "EMBEDDED_IDENTITY_NOT_READY", "嵌入身份签名密钥无效。");
        }
    }

    private static string Required(IConfigurationSection section, string key) => Required(section[key], key);

    private static string Required(string? value, string field) => string.IsNullOrWhiteSpace(value) ? throw new EmbeddedHandshakeException(503, "EMBEDDED_IDENTITY_NOT_READY", $"嵌入身份配置缺少 {field}。") : value.Trim();

    private static string Required(ClaimsPrincipal principal, string claimType, int maxLength)
    {
        var value = principal.FindFirstValue(claimType);
        return string.IsNullOrWhiteSpace(value) || value.Length > maxLength ? throw new SecurityTokenException("embedded assertion claim invalid") : value;
    }

    private sealed record EmbeddedIdentitySourceSettings(string SourceNId, string Issuer, string Audience, string KeyId, string? PrivateKeyPath, string? PrivateKey, string? PublicKeyPath, string? PublicKey);
}
