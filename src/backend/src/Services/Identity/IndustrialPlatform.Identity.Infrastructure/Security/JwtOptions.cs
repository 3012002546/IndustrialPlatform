namespace IndustrialPlatform.Identity.Infrastructure.Security;

/// <summary>
/// JWT 签发配置(§12),绑定自配置节 <c>Identity:Jwt</c>。
/// SigningKey 为空时启动生成临时密钥(开发环境);配置了但 PEM 非法 → 启动失败(fail-closed)。
/// </summary>
public sealed class JwtOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Identity:Jwt";

    /// <summary>令牌签发方(iss)。</summary>
    public string Issuer { get; set; } = "industrial-platform-identity";

    /// <summary>令牌受众(aud)。</summary>
    public string Audience { get; set; } = "industrial-platform-api";

    /// <summary>Access Token 有效期(分钟,§12 默认 30)。</summary>
    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary>JWT Header kid(密钥标识)。</summary>
    public string KeyId { get; set; } = "identity-default";

    /// <summary>RSA 私钥 PEM(PKCS#8 或 PKCS#1);为空 → 开发期临时密钥兜底。</summary>
    public string? SigningKey { get; set; }
}
