using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.Identity.Application.Sso;

/// <summary>
/// OAuth state 记录(§26.5):随机不透明 state 只作为 Redis 键,5 分钟一次性消费。
/// 绝不允许写入 Cookie、日志或返回给浏览器。CodeVerifier 明文只在服务端内存与 Redis 流转,
/// <see cref="CodeChallenge"/> 即其 S256 摘要(回调时重算比对,防篡改)。
/// </summary>
public sealed record SsoOAuthState(
    string ProviderNId,
    string TenantNId,
    string Nonce,
    string CodeChallenge,
    string CodeChallengeMethod,
    string CodeVerifier,
    string RedirectUri,
    string? ReturnUrl,
    DateTimeOffset IssuedAt);

/// <summary>一次性登录票据(§26.5):回调/复用用例签发,交换用例消费,Redis 60 秒。</summary>
public sealed record SsoLoginTicket(
    string ProviderNId,
    string TenantNId,
    string UserNId,
    string? ReturnUrl,
    string BrowserSessionHandle,
    DateTimeOffset IssuedAt);

/// <summary>SSO 随机值生成助手:base64url 编码,32 字节随机源,不可预测。</summary>
internal static class SsoRandom
{
    /// <summary>生成 base64url 随机令牌(state/ticket/handle)。</summary>
    public static string Base64UrlToken(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>计算 base64url 的 SHA-256 摘要(PKCE S256 code_challenge / 处理器摘要)。</summary>
    public static string Sha256Base64Url(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>生成业务标识(字母前缀 + 12 字节十六进制小写),符合 NId 格式。</summary>
    public static string NId(string prefix) => prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
}
