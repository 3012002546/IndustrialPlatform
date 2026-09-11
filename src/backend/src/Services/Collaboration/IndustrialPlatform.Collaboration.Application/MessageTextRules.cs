using System.Globalization;
using System.Text;

namespace IndustrialPlatform.Collaboration.Application;

public static class MessageTextRules
{
    public static string? NormalizeAndValidate(string? value)
    {
        if (value is null)
            return null;

        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsLowSurrogate(value[index]) && index > 0 && char.IsHighSurrogate(value[index - 1]))
                continue;
            if (char.IsSurrogate(value[index])
                && (!char.IsHighSurrogate(value[index])
                    || index + 1 >= value.Length
                    || !char.IsLowSurrogate(value[index + 1])))
                throw new ArgumentException("消息正文包含无效的 Unicode 字符。", nameof(value));
        }

        var normalized = value.Normalize(NormalizationForm.FormC)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var scalarCount = 0;
        foreach (var rune in normalized.EnumerateRunes())
        {
            scalarCount++;
            if (scalarCount > 4000)
                throw new ArgumentException("消息正文不能超过 4000 个 Unicode 标量值。", nameof(value));

            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.Control && rune.Value is not ('\n' or '\t'))
                throw new ArgumentException("消息正文包含不允许的控制字符。", nameof(value));
            if (category == UnicodeCategory.Format && IsBidiControl(rune.Value))
                throw new ArgumentException("消息正文包含不允许的双向文本控制字符。", nameof(value));
        }

        return normalized.Trim();
    }

    private static bool IsBidiControl(int value) => value is
        0x061C or 0x200E or 0x200F or
        0x202A or 0x202B or 0x202C or 0x202D or 0x202E or
        0x2066 or 0x2067 or 0x2068 or 0x2069;
}
