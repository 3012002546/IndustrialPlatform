using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>配置规范化与强类型边界；版本和decimal从不通过double转换。</summary>
public static class StudioJson
{
    public static string Canonicalize(JsonNode? node) => Sort(node)?.ToJsonString() ?? "null";

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string RequiredString(JsonObject node, string key, int maxLength = 128)
    {
        var value = Text(node[key]);
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new StudioException("STUDIO_VALIDATION", $"{key}不能为空且不能超过{maxLength}个字符。", path: key);
        return value;
    }

    public static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static long Version(string? value, string path = "optimisticVersion")
    {
        if (value is null || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 0)
            throw new StudioException("TYPE_MISMATCH", "版本必须是非负十进制整数字符串。", path: path);
        return version;
    }

    public static long ScaledDecimal(string? value, string path)
    {
        if (value is null || !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            || number < -999999999999.999999m || number > 999999999999.999999m || number * 1000000m != decimal.Truncate(number * 1000000m))
            throw new StudioException("TYPE_MISMATCH", "数量须为最多6位小数的十进制字符串且在允许范围内。", path: path);
        return checked((long)(number * 1000000m));
    }

    public static JsonNode? ReadPath(JsonNode? root, string path)
    {
        if (path.Length > 512 || path.Split('.').Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "__proto__" or "prototype" or "constructor"))
            throw new StudioException("PATH_INVALID", "字段路径包含非法片段。", path: path);
        var current = root;
        foreach (var segment in path.Split('.'))
        {
            if (current is JsonObject item)
            {
                if (!item.TryGetPropertyValue(segment, out current)) return null;
            }
            else if (current is JsonArray array && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < array.Count)
            {
                current = array[index];
            }
            else return null;
        }
        return current?.DeepClone();
    }

    public static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => KeyValuePair.Create(pair.Key, Sort(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
        _ => node?.DeepClone(),
    };
}
