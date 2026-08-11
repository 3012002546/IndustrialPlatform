using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Contracts.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace IndustrialPlatform.Identity.Infrastructure.Security;

/// <summary>
/// JWKS 公钥文档提供实现(§12):由 RSA 公钥生成 RFC 7517 密钥条目。
/// 只暴露公钥参数(模数/指数),绝不暴露私钥。
/// </summary>
public sealed class JwksProvider : IJwksProvider
{
    private readonly RsaSigningKeyProvider _keys;

    /// <summary>初始化 JWKS 提供者。</summary>
    public JwksProvider(RsaSigningKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _keys = keys;
    }

    /// <inheritdoc/>
    public Task<JwksDocument> GetAsync(CancellationToken cancellationToken)
    {
        var parameters = _keys.PublicKey.ExportParameters(false);
        var key = new JwksKey(
            Kty: "RSA",
            Kid: _keys.KeyId,
            N: Base64UrlEncoder.Encode(parameters.Modulus!),
            E: Base64UrlEncoder.Encode(parameters.Exponent!),
            Use: "sig",
            Alg: "RS256");
        return Task.FromResult(new JwksDocument([key]));
    }
}
