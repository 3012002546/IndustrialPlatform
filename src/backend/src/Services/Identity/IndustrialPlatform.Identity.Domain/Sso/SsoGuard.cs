using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Sso;

/// <summary>SSO 领域文本规则辅助:与 Role/User 聚合保持一致的长度/空白校验约定。</summary>
internal static class SsoGuard
{
    public static string RequireTrimmedNonEmpty(string? value, string emptyMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(emptyMessage);
        }

        return value.Trim();
    }

    public static string RequireTrimmedNonEmpty(string? value, string emptyMessage, int maxLength, string tooLongMessage)
    {
        var trimmed = RequireTrimmedNonEmpty(value, emptyMessage);
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(tooLongMessage);
        }

        return trimmed;
    }

    public static string? TrimOrNull(string? value, int maxLength, string tooLongMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(tooLongMessage);
        }

        return trimmed;
    }

    /// <summary>邮箱域规范化:转小写、去头尾点,校验不含空白与通配符之外的字符。</summary>
    public static string NormalizeEmailDomain(string value)
    {
        var trimmed = RequireTrimmedNonEmpty(value, "邮箱域不能为空。");
        var normalized = trimmed.ToLowerInvariant().Trim('.');

        if (normalized.Any(char.IsWhiteSpace))
        {
            throw new ValidationException("邮箱域不能包含空白字符。");
        }

        return normalized;
    }
}
