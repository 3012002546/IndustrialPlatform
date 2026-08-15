using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.Identity.Application.Bootstrap;

/// <summary>
/// bootstrap 安全助手:一次性引用哈希(SHA-256)与随机引用生成。
/// 明文密码与引用本身绝不入库,只保存哈希。
/// </summary>
public static class BootstrapHashing
{
    /// <summary>计算引用 SHA-256 十六进制哈希。</summary>
    public static string HashReference(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference))).ToLowerInvariant();
    }

    /// <summary>生成一次性随机引用(base64url,32 字节熵)。</summary>
    public static string NewReference() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
