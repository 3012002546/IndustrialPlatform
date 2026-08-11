using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.Identity.Infrastructure.Security;

/// <summary>内部哈希工具(IP/User-Agent/Refresh Token 统一入口,小写 hex)。</summary>
internal static class Hashing
{
    /// <summary>计算 SHA-256 十六进制摘要(小写)。</summary>
    /// <param name="value">原文,不得包含明文密码。</param>
    /// <returns>64 位小写 hex 摘要。</returns>
    public static string Sha256Hex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
