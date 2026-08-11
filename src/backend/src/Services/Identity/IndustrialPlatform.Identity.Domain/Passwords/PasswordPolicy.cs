using System.Buffers;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Passwords;

/// <summary>
/// 明文密码复杂度策略。由应用层在哈希前调用,领域层不接收明文密码。
/// </summary>
public static class PasswordPolicy
{
    /// <summary>最短长度。</summary>
    public const int MinLength = 12;

    /// <summary>最长长度。</summary>
    public const int MaxLength = 128;

    /// <summary>密码必须包含的特殊字符集合。</summary>
    public const string SpecialCharacters = "!@#$%^&*()-_=+[]{}|;:,.<>?/";

    private static readonly SearchValues<char> SpecialCharacterValues = SearchValues.Create(SpecialCharacters);

    /// <summary>
    /// 校验明文密码是否满足复杂度要求:长度、大小写、数字、特殊字符,
    /// 且不得与登录名或业务标识相同(忽略大小写)。不满足时抛出 <see cref="ValidationException"/>。
    /// </summary>
    /// <param name="password">明文密码。</param>
    /// <param name="loginName">登录名,用于禁止与登录名相同。</param>
    /// <param name="nId">业务标识,用于禁止与标识相同。</param>
    public static void Validate(string? password, string? loginName, string? nId)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ValidationException("密码不能为空。");
        }

        if (password.Length < MinLength)
        {
            throw new ValidationException($"密码长度不能少于 {MinLength} 个字符。");
        }

        if (password.Length > MaxLength)
        {
            throw new ValidationException($"密码长度不能超过 {MaxLength} 个字符。");
        }

        if (!password.Any(char.IsUpper))
        {
            throw new ValidationException("密码必须包含大写字母。");
        }

        if (!password.Any(char.IsLower))
        {
            throw new ValidationException("密码必须包含小写字母。");
        }

        if (!password.Any(char.IsDigit))
        {
            throw new ValidationException("密码必须包含数字。");
        }

        if (password.IndexOfAny(SpecialCharacterValues) < 0)
        {
            throw new ValidationException("密码必须包含特殊字符。");
        }

        if (string.Equals(password, loginName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("密码不能与登录名相同。");
        }

        if (string.Equals(password, nId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("密码不能与业务标识相同。");
        }
    }
}
