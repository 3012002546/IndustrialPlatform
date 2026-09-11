using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace IndustrialPlatform.Security;

public interface ITrustedServiceCallSigner
{
    string Sign(
        HttpMethod method,
        string pathAndQuery,
        ReadOnlyMemory<byte> body,
        string audience,
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        string action,
        string requestNId);
}

/// <summary>Signs the short-lived assertion used at PF05 internal service boundaries.</summary>
public sealed class TrustedServiceCallSigner : ITrustedServiceCallSigner, IDisposable
{
    private readonly RSA? _privateKey;
    private readonly string _issuer;
    private readonly string _keyId;

    public TrustedServiceCallSigner(IConfiguration configuration)
    {
        var section = configuration.GetSection("Collaboration:ServiceIdentity");
        _issuer = section["Issuer"] ?? "collaboration";
        _keyId = section["KeyId"] ?? throw new InvalidOperationException("Collaboration:ServiceIdentity:KeyId 未配置。");
        var path = section["PrivateKeyPath"];
        var pem = section["PrivateKey"];
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(pem))
            return;

        _privateKey = RSA.Create();
        try
        {
            _privateKey.ImportFromPem(string.IsNullOrWhiteSpace(path) ? pem! : File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or CryptographicException or ArgumentException)
        {
            _privateKey.Dispose();
            throw new InvalidOperationException("Collaboration 服务断言私钥无效。", exception);
        }
    }

    public string Sign(
        HttpMethod method,
        string pathAndQuery,
        ReadOnlyMemory<byte> body,
        string audience,
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        string action,
        string requestNId)
    {
        if (_privateKey is null)
            throw new InvalidOperationException("Collaboration 服务断言私钥未配置。");
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            [JwtRegisteredClaimNames.Sub] = "collaboration",
            ["action"] = action,
            ["method"] = method.Method.ToUpperInvariant(),
            ["path"] = pathAndQuery.TrimStart('/'),
            ["body_sha256"] = Convert.ToHexString(SHA256.HashData(body.Span)).ToLowerInvariant(),
            ["tenant_id"] = tenantNId,
            ["actor_user_n_id"] = actorUserNId,
            ["actor_session_n_id"] = actorSessionNId,
            ["actor_security_version"] = actorSecurityVersion,
            ["request_n_id"] = requestNId,
        };
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: audience,
            claims: claims
                .Select(item => new Claim(item.Key, item.Value.ToString()!))
                .Append(new Claim(
                    JwtRegisteredClaimNames.Iat,
                    new DateTimeOffset(now).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64)),
            notBefore: now,
            expires: now.AddSeconds(30),
            signingCredentials: new SigningCredentials(new RsaSecurityKey(_privateKey) { KeyId = _keyId }, SecurityAlgorithms.RsaSha256));
        token.Header[JwtHeaderParameterNames.Typ] = "industrial-service-call+jwt";
        token.Header[JwtHeaderParameterNames.Kid] = _keyId;
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose() => _privateKey?.Dispose();
}
