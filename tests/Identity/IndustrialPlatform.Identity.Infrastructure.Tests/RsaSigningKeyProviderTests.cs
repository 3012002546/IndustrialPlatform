using System.Security.Cryptography;
using IndustrialPlatform.Identity.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// RSA 签名密钥提供者测试:空配置临时密钥兜底、配置 PEM 生效、非法 PEM 启动失败(fail-closed)。
/// </summary>
public sealed class RsaSigningKeyProviderTests
{
    private static IOptions<JwtOptions> OptionsFor(string? signingKey) =>
        Options.Create(new JwtOptions { SigningKey = signingKey });

    [Fact]
    public void EmptySigningKey_GeneratesEphemeralKey()
    {
        using var provider = new RsaSigningKeyProvider(OptionsFor(null), NullLogger<RsaSigningKeyProvider>.Instance);

        Assert.NotNull(provider.PrivateKey);
        Assert.NotNull(provider.PublicKey.ExportParameters(false).Modulus);
        Assert.Equal("identity-default", provider.KeyId);
    }

    [Fact]
    public void ConfiguredPem_UsesProvidedKey()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();

        using var provider = new RsaSigningKeyProvider(OptionsFor(pem), NullLogger<RsaSigningKeyProvider>.Instance);

        var expected = Convert.ToBase64String(rsa.ExportParameters(false).Modulus!);
        var actual = Convert.ToBase64String(provider.PublicKey.ExportParameters(false).Modulus!);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InvalidPem_ThrowsInvalidOperation_FailClosed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new RsaSigningKeyProvider(OptionsFor("not-a-pem"), NullLogger<RsaSigningKeyProvider>.Instance));
    }
}
