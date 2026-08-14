using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>数据库编排领域通用文本/数值校验,供聚合根与子实体复用(对齐 Identity Role/User 的私有 helper 语义)。</summary>
internal static class DatabaseOrchestrationGuard
{
    /// <summary>修剪非空文本并校验长度。</summary>
    public static string RequireTrimmedNonEmpty(string? value, string emptyMessage, int maxLength, string tooLongMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(emptyMessage);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(tooLongMessage);
        }

        return trimmed;
    }

    /// <summary>业务标识(NId 字符串形式)校验,长度上限对齐 <see cref="Identities.NId.MaxLength"/>。</summary>
    public static string RequireNId(string? value, string emptyMessage) =>
        RequireTrimmedNonEmpty(
            value,
            emptyMessage,
            Identities.NId.MaxLength,
            $"业务标识长度不能超过 {Identities.NId.MaxLength} 个字符。");

    /// <summary>SHA-256 十六进制校验和校验(64 个小写 hex)。</summary>
    public static string RequireSha256Hex(string? value, string emptyMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(emptyMessage);
        }

        var trimmed = value.Trim();
        if (trimmed.Length != 64)
        {
            throw new ValidationException("校验和必须为 64 位十六进制字符。");
        }

        foreach (var c in trimmed)
        {
            if (!Uri.IsHexDigit(c))
            {
                throw new ValidationException("校验和必须为 64 位十六进制字符。");
            }
        }

        return trimmed.ToLowerInvariant();
    }

    /// <summary>可空文本:空则返回 null,否则修剪并校验长度。</summary>
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

    /// <summary>正整数校验。</summary>
    public static int RequirePositive(int value, string message)
    {
        if (value <= 0)
        {
            throw new ValidationException(message);
        }

        return value;
    }

    /// <summary>非负整数校验。</summary>
    public static int RequireNonNegative(int value, string message)
    {
        if (value < 0)
        {
            throw new ValidationException(message);
        }

        return value;
    }
}
