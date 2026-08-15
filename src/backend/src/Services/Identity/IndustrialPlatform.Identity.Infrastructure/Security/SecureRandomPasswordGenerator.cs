using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Domain.Passwords;

namespace IndustrialPlatform.Identity.Infrastructure.Security;

/// <summary>
/// 密码学安全随机临时密码生成器(§29A.4):使用 <see cref="RandomNumberGenerator"/>,
/// 长度不少于 <paramref name="minLength"/>(bootstrap 固定 20)且满足 <see cref="PasswordPolicy"/>,
/// 每次调用独立随机,不读取任何固定密码配置。
/// </summary>
public static class SecureRandomPasswordGenerator
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string Special = PasswordPolicy.SpecialCharacters;
    private const string All = Upper + Lower + Digits + Special;

    /// <summary>
    /// 生成随机临时密码:保证至少包含大小写字母、数字与特殊字符,
    /// 总长度不少于 <paramref name="minLength"/> 且不超过 <see cref="PasswordPolicy.MaxLength"/>。
    /// </summary>
    /// <param name="minLength">最短长度,默认 20(bootstrap 要求不少于 20)。</param>
    /// <param name="loginName">登录名,用于策略校验(禁止与登录名相同)。</param>
    /// <param name="nId">业务标识,用于策略校验(禁止与标识相同)。</param>
    /// <returns>满足策略的随机临时密码明文。</returns>
    public static string Generate(int minLength = BootstrapSeedCatalog.BootstrapPasswordMinLength, string? loginName = null, string? nId = null)
    {
        var length = Math.Clamp(minLength, 4, PasswordPolicy.MaxLength);

        // 先各取一类保证字符集覆盖,再补齐随机字符,最后 Fisher-Yates 洗牌。
        var builder = new StringBuilder(length);
        builder.Append(NextChar(Upper));
        builder.Append(NextChar(Lower));
        builder.Append(NextChar(Digits));
        builder.Append(NextChar(Special));
        for (var i = 4; i < length; i++)
        {
            builder.Append(NextChar(All));
        }

        var password = Shuffle(builder.ToString());
        PasswordPolicy.Validate(password, loginName, nId);
        return password;
    }

    private static char NextChar(string charset)
    {
        var bytes = RandomNumberGenerator.GetBytes(4);
        var index = (int)(BitConverter.ToUInt32(bytes, 0) % (uint)charset.Length);
        return charset[index];
    }

    private static string Shuffle(string value)
    {
        var chars = value.ToCharArray();
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var bytes = RandomNumberGenerator.GetBytes(4);
            var j = (int)(BitConverter.ToUInt32(bytes, 0) % (uint)(i + 1));
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
