using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration.Identities;

namespace IndustrialPlatform.SystemData.Domain;

/// <summary>
/// 组织、岗位与任职领域通用文本/数值校验与业务标识规范化(对齐 DatabaseOrchestrationGuard 语义)。
/// 业务标识复用 <see cref="NId"/> 的格式规则,返回原始值与大写规范化值供持久化。
/// </summary>
internal static class SystemDataDomainGuard
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

    /// <summary>业务标识(NId 字符串形式)校验,返回 (原始值, 大写规范化值);空值抛领域专属消息。</summary>
    public static NIdPair RequireNId(string? value, string emptyMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(emptyMessage);
        }

        var nid = NId.Create(value);
        return new NIdPair(nid.Value, nid.Normalized);
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

/// <summary>业务标识对:原始值 + 大写规范化值(唯一性约束与比较使用规范化值)。</summary>
internal readonly record struct NIdPair(string Value, string Normalized);
