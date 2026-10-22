using System.Globalization;
using System.Text.Json.Nodes;

namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>无脚本绑定解释器；上下文必须由调用端明确提供，空值不自动变零。</summary>
public static class StudioBinding
{
    public static JsonNode? Resolve(JsonObject binding, IReadOnlyDictionary<string, JsonNode?> contexts)
    {
        var from = StudioJson.RequiredString(binding, "from", 32);
        JsonNode? value;
        if (from == "Constant")
        {
            if (!binding.ContainsKey("value")) throw Error("value", "常量必须提供value。");
            value = binding["value"]?.DeepClone();
        }
        else
        {
            if (!contexts.TryGetValue(from == "Instance" ? "Instance:" + StudioJson.RequiredString(binding, "instanceId") : from, out value))
                throw Error("from", "当前操作不提供该绑定上下文。");
            if (binding["path"] is not JsonArray segments || segments.Count > 32) throw Error("path", "必须配置字段路径数组。");
            foreach (var segment in segments)
            {
                var key = StudioJson.Text(segment);
                if (string.IsNullOrEmpty(key) || key is "__proto__" or "constructor" or "prototype" || key.Length > 128) throw Error("path", "字段路径无效。");
                if (value is JsonObject obj) value = obj[key];
                else if (value is JsonArray array && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < array.Count) value = array[index];
                else value = null;
            }
            value = value?.DeepClone();
        }
        if (value is null)
        {
            return StudioJson.Text(binding["nullPolicy"]) switch
            {
                "Null" => null,
                "Default" when binding.ContainsKey("defaultValue") => binding["defaultValue"]?.DeepClone(),
                _ => throw new StudioException("PARAM_REQUIRED", "必需的绑定值为空。", path: "binding.path"),
            };
        }
        if (binding["transform"] is not JsonObject transform) return value;
        var args = transform["args"] as JsonObject ?? new JsonObject();
        var scalar = value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) ? text : value.ToJsonString();
        switch (StudioJson.RequiredString(transform, "kind", 32))
        {
            case "ToString": return JsonValue.Create(scalar);
            case "ToDecimal":
                var scaled = StudioJson.ScaledDecimal(scalar, "binding.transform");
                var scale = args["scale"]?.GetValue<int>() ?? 6;
                if (scale is < 0 or > 6 || scaled % (long)Math.Pow(10, 6 - scale) != 0) throw Error("transform.args.scale", "转换将损失精度。");
                return JsonValue.Create((scaled / 1000000m).ToString(CultureInfo.InvariantCulture));
            case "DateFormat":
                if (!System.Text.RegularExpressions.Regex.IsMatch(scalar, @"(Z|[+-]\d{2}:\d{2})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || !DateTimeOffset.TryParse(scalar, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)) throw Error("transform", "时间格式必须含时区。");
                var pattern = StudioJson.Text(args["pattern"]);
                if (pattern is not ("yyyy-MM-dd" or "yyyy-MM-dd HH:mm:ss")) throw Error("transform.args.pattern", "日期格式不在白名单。");
                var zoneKey = StudioJson.Text(args["timeZone"]) ?? "UTC";
                if (zoneKey != "UTC" && !TimeZoneInfo.TryConvertIanaIdToWindowsId(zoneKey, out _)) throw Error("transform.args.timeZone", "必须使用有效IANA时区。");
                try { return JsonValue.Create(TimeZoneInfo.ConvertTime(time, TimeZoneInfo.FindSystemTimeZoneById(zoneKey)).ToString(pattern, CultureInfo.InvariantCulture)); }
                catch (TimeZoneNotFoundException) { throw Error("transform.args.timeZone", "该时区不可用。"); }
            case "EnumMap":
                if (args["entries"] is not JsonArray entries || entries.Any(entry => entry is not JsonObject)) throw Error("transform.args.entries", "枚举映射项必须为对象。");
                foreach (var entry in entries.Cast<JsonObject>())
                    if (JsonNode.DeepEquals(entry["from"], value)) return entry["to"]?.DeepClone();
                return StudioJson.Text(args["onMissing"]) switch
                {
                    "Original" => value,
                    "Default" when args.ContainsKey("defaultValue") => args["defaultValue"]?.DeepClone(),
                    _ => throw Error("transform", "枚举映射中没有对应项。"),
                };
            default: throw Error("transform.kind", "不允许的绑定转换。");
        }
    }

    public static bool Evaluate(JsonObject condition, IReadOnlyDictionary<string, JsonNode?> contexts, int depth = 0)
    {
        if (depth > 32) throw Error("condition", "条件嵌套超过保护预算。");
        var op = StudioJson.RequiredString(condition, "op", 16);
        if (op is "And" or "Or")
        {
            if (condition["items"] is not JsonArray items || items.Count == 0 || items.Any(item => item is not JsonObject)) throw Error("condition.items", "组合条件不能为空。");
            return op == "And" ? items.OfType<JsonObject>().All(item => Evaluate(item, contexts, depth + 1)) : items.OfType<JsonObject>().Any(item => Evaluate(item, contexts, depth + 1));
        }
        var left = Resolve(condition["left"] as JsonObject ?? throw Error("condition.left", "缺少左值。"), contexts);
        if (op == "IsNull") return left is null;
        if (op == "HasRows") return left is JsonArray { Count: > 0 };
        var right = Resolve(condition["right"] as JsonObject ?? throw Error("condition.right", "缺少右值。"), contexts);
        if (op == "Eq") return JsonNode.DeepEquals(left, right);
        if (op == "Ne") return !JsonNode.DeepEquals(left, right);
        if (op == "In") return right is JsonArray array && array.Any(value => JsonNode.DeepEquals(left, value));
        if (left is null || right is null) return false;
        var l = StudioJson.Text(left) ?? left.ToJsonString();
        var r = StudioJson.Text(right) ?? right.ToJsonString();
        var comparison = decimal.TryParse(l, NumberStyles.Number, CultureInfo.InvariantCulture, out var ln) && decimal.TryParse(r, NumberStyles.Number, CultureInfo.InvariantCulture, out var rn) ? ln.CompareTo(rn) : string.CompareOrdinal(l, r);
        return op switch { "Gt" => comparison > 0, "Ge" => comparison >= 0, "Lt" => comparison < 0, "Le" => comparison <= 0, _ => throw Error("condition.op", "不允许的比较操作。") };
    }

    private static StudioException Error(string path, string message) => new("TYPE_MISMATCH", message, path: path);
}
