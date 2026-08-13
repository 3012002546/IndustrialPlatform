using System.Security.Cryptography;

namespace IndustrialPlatform.Identity.Infrastructure.Security;

/// <summary>基础设施层随机令牌助手:base64url 随机令牌与 NId 生成(与应用层 SsoRandom 同规则,层内专用)。</summary>
internal static class SecureTokens
{
    /// <summary>生成 base64url 随机令牌(浏览器会话句柄等)。</summary>
    public static string Base64UrlToken(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>生成业务标识(字母前缀 + 12 字节十六进制小写),符合 NId 格式。</summary>
    public static string NId(string prefix) => prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
}
