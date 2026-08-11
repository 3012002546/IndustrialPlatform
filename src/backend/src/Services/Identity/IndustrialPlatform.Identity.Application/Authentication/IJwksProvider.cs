using IndustrialPlatform.Identity.Contracts.Authentication;

namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>JWKS 公钥文档端口(§12 公钥端点)。</summary>
public interface IJwksProvider
{
    /// <summary>返回当前签名公钥的 JWKS 文档。</summary>
    Task<JwksDocument> GetAsync(CancellationToken cancellationToken);
}
