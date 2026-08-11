using System.Text.RegularExpressions;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SharedKernel.ValueObjects;

namespace IndustrialPlatform.Identity.Domain.Identities;

/// <summary>
/// 业务标识 NId 值对象:不透明字符串,大小写不敏感,带格式校验与规范化。
/// 格式:1-128 个字符,以字母或数字开头,可含字母、数字、下划线、点与连字符。
/// 相等性基于规范化值,<c>abc-01</c> 与 <c>ABC-01</c> 视为相同。
/// </summary>
public sealed partial class NId : ValueObject
{
    /// <summary>最大长度。</summary>
    public const int MaxLength = 128;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex FormatPattern();

    /// <summary>修剪后的原始大小写值,用于持久化。</summary>
    public string Value { get; }

    /// <summary>修剪并转为大写的规范化值,用于唯一性约束与比较。</summary>
    public string Normalized { get; }

    private NId(string value, string normalized)
    {
        Value = value;
        Normalized = normalized;
    }

    /// <summary>
    /// 创建并校验 NId。空值、超长或格式非法抛出 <see cref="ValidationException"/>。
    /// </summary>
    /// <param name="value">原始标识,前后空白将被修剪。</param>
    /// <returns>校验通过后的 NId 值对象。</returns>
    public static NId Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException("标识不能为空。");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            throw new ValidationException($"标识长度不能超过 {MaxLength} 个字符。");
        }

        if (!FormatPattern().IsMatch(trimmed))
        {
            throw new ValidationException("标识只能包含字母、数字、下划线、点与连字符,且必须以字母或数字开头。");
        }

        return new NId(trimmed, trimmed.ToUpperInvariant());
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <inheritdoc />
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Normalized;
    }
}
