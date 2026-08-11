using System.Security.Cryptography;
using System.Text.Json;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// Access Token 签发与 JWKS 测试(§12):RS256 验签、claims/kid/有效期、公钥文档可解析。
/// </summary>
public sealed class AccessTokenFactoryAndJwksTests : IDisposable
{
    private readonly RsaSigningKeyProvider _provider;
    private readonly AccessTokenFactory _factory;
    private readonly JwksProvider _jwks;

    public AccessTokenFactoryAndJwksTests()
    {
        using var rsa = RSA.Create(2048);
        _provider = new RsaSigningKeyProvider(
            Options.Create(new JwtOptions { SigningKey = rsa.ExportPkcs8PrivateKeyPem() }),
            NullLogger<RsaSigningKeyProvider>.Instance);
        _factory = new AccessTokenFactory(_provider, Options.Create(new JwtOptions()));
        _jwks = new JwksProvider(_provider);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task CreateToken_SignsValidJwtWithExpectedClaimsAndLifetime()
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var descriptor = new AccessTokenDescriptor(
            "user.alice",
            "alice",
            "development",
            ["role.operator", "role.admin"],
            "SES-abc123",
            2,
            issuedAt);

        var result = _factory.Create(descriptor);

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal(issuedAt.AddMinutes(30), result.ExpiresAt);

        // RS256 验签:签名、issuer、audience、lifetime
        var validationParameters = new TokenValidationParameters
        {
            IssuerSigningKey = new RsaSecurityKey(_provider.PublicKey),
            ValidIssuer = "industrial-platform-identity",
            ValidAudience = "industrial-platform-api",
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(result.Token, validationParameters);
        Assert.True(validation.IsValid);

        // 结构:Header kid + claims(sub=UserNId/role[]/sid/ver),不含完整 permissions
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(result.Token);
        Assert.True(jwt.TryGetHeaderValue("kid", out string? kid));
        Assert.Equal("identity-default", kid);
        Assert.Equal("user.alice", jwt.Claims.First(c => c.Type == "sub").Value);
        Assert.Equal("alice", jwt.Claims.First(c => c.Type == "user_name").Value);
        Assert.Equal("development", jwt.Claims.First(c => c.Type == "tenant_id").Value);
        Assert.Equal("SES-abc123", jwt.Claims.First(c => c.Type == "sid").Value);
        Assert.Equal("2", jwt.Claims.First(c => c.Type == "ver").Value);
        var roles = jwt.GetPayloadValue<JsonElement>("role").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["role.operator", "role.admin"], roles);
        // exp claim 为秒精度,按秒容差断言
        Assert.Equal(result.ExpiresAt.UtcDateTime, jwt.ValidTo, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Jwks_ExposesPublicKeyParametersAndParsesAsJsonWebKeySet()
    {
        var doc = await _jwks.GetAsync(CancellationToken.None);

        var key = Assert.Single(doc.Keys);
        Assert.Equal("RSA", key.Kty);
        Assert.Equal("identity-default", key.Kid);
        Assert.Equal("sig", key.Use);
        Assert.Equal("RS256", key.Alg);

        var parameters = _provider.PublicKey.ExportParameters(false);
        Assert.Equal(Base64UrlEncoder.Encode(parameters.Modulus!), key.N);
        Assert.Equal(Base64UrlEncoder.Encode(parameters.Exponent!), key.E);

        // camelCase 序列化即 RFC 7517,可被标准 JsonWebKeySet 解析
        var jwks = new JsonWebKeySet(JsonSerializer.Serialize(doc));
        var parsed = Assert.Single(jwks.Keys);
        Assert.Equal(key.Kid, parsed.Kid);
    }
}
