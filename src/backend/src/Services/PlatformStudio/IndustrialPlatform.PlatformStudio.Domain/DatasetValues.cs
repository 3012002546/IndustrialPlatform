using System.Globalization;
using System.Text.Json.Nodes;

namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>数据参数及结果Schema边界；必填错误在外部调用前发现，隐藏字段不返回浏览器。</summary>
public static class DatasetValues
{
    public static JsonObject Parameters(StudioPrincipal principal, JsonObject document, JsonObject supplied)
    {
        var definitions = (document["parameters"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        if (definitions.Length != (document["parameters"] as JsonArray)?.Count && document["parameters"] is not null) throw new StudioException("STUDIO_VALIDATION", "参数项必须为对象。");
        var names = definitions.Select(item => StudioJson.RequiredString(item, "name")).ToHashSet(StringComparer.Ordinal);
        if (names.Count != definitions.Length) throw new StudioException("STUDIO_VALIDATION", "参数名称重复。");
        foreach (var field in supplied)
            if (!names.Contains(field.Key)) throw new StudioException("STUDIO_VALIDATION", "查询参数未在合同声明。", path: "parameters." + field.Key);
        var result = new JsonObject();
        foreach (var definition in definitions)
        {
            var name = StudioJson.RequiredString(definition, "name");
            JsonNode? value;
            if (StudioJson.Text(definition["source"]) == "TrustedContext")
            {
                value = StudioJson.Text(definition["contextKey"]) switch
                {
                    "tenant" => JsonValue.Create(principal.TenantNId), "user" => JsonValue.Create(principal.SubjectNId),
                    "authorizedFactoryIds" => new JsonArray(principal.FactoryNIds.Select(item => (JsonNode)JsonValue.Create(item)!).ToArray()),
                    _ => throw new StudioException("STUDIO_FORBIDDEN", "未登记的可信上下文键。", 403, "parameters." + name),
                };
            }
            else value = supplied.ContainsKey(name) ? supplied[name]?.DeepClone() : definition["defaultValue"]?.DeepClone();
            var required = definition["required"]?.GetValue<bool>() == true;
            if (value is null || required && StudioJson.Text(value) is { } text && string.IsNullOrWhiteSpace(text))
            {
                if (required || definition["nullable"]?.GetValue<bool>() == false) throw new StudioException("PARAM_REQUIRED", "缺少必需查询参数。", path: "parameters." + name);
                result[name] = null; continue;
            }
            CheckScalar(StudioJson.RequiredString(definition, "type", 32), value, "parameters." + name, definition);
            result[name] = value;
        }
        return result;
    }

    public static JsonNode? Project(JsonNode? input, JsonObject field, IReadOnlySet<string> readable, string path = "data", int depth = 0)
    {
        if (depth == 0)
        {
            var errors = new List<StudioDiagnostic>();
            StudioDocuments.ValidateFieldSchema(field, "result", new(StringComparer.Ordinal), errors);
            if (errors.FirstOrDefault() is { } error) throw new StudioException(error.Code, error.Message, path: error.Path);
        }
        if (depth > 32) throw new StudioException("STUDIO_LIMIT", "结果Schema超过保护深度。", 429, path);
        if (input is null)
        {
            if (field["nullable"]?.GetValue<bool>() == true) return null;
            throw new StudioException("TYPE_MISMATCH", "来源字段缺失或出现不允许的空值。", path: path);
        }
        var type = StudioJson.RequiredString(field, "type", 32);
        if (type == "Object")
        {
            if (input is not JsonObject obj) throw new StudioException("TYPE_MISMATCH", "来源对象结构与合同不一致。", path: path);
            var result = new JsonObject();
            foreach (var child in (field["children"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var id = StudioJson.RequiredString(child, "id", 64); var name = StudioJson.RequiredString(child, "name");
                if (name is "__proto__" or "constructor" or "prototype") throw new StudioException("PATH_INVALID", "来源属性名无效。", path: path);
                var nested = StudioJson.Text(child["type"]) is "Object" or "Array";
                if (nested ? HasReadableLeaf(child, readable) : readable.Contains(id)) result[name] = Project(obj[name], child, readable, path + "." + name, depth + 1);
            }
            return result;
        }
        if (type == "Array")
        {
            if (input is not JsonArray array || field["item"] is not JsonObject item) throw new StudioException("TYPE_MISMATCH", "来源集合结构与合同不一致。", path: path);
            if (!HasReadableLeaf(item, readable)) throw new StudioException("STUDIO_FORBIDDEN", "集合没有可读字段。", 403, path);
            return new JsonArray(array.Select((value, index) => Project(value, item, readable, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", depth + 1)).ToArray());
        }
        if (type is "Int64" or "Decimal" && input is JsonValue numeric && numeric.GetValueKind() == System.Text.Json.JsonValueKind.Number) input = JsonValue.Create(numeric.ToJsonString());
        CheckScalar(type, input, path, field);
        return input.DeepClone();
    }

    private static bool HasReadableLeaf(JsonObject field, IReadOnlySet<string> readable) => StudioJson.Text(field["type"]) switch
    {
        "Object" => (field["children"] as JsonArray ?? []).Cast<JsonObject>().Any(child => HasReadableLeaf(child, readable)),
        "Array" => field["item"] is JsonObject item && HasReadableLeaf(item, readable),
        _ => readable.Contains(StudioJson.RequiredString(field, "id", 64)),
    };

    public static void CheckScalar(string type, JsonNode value, string path, JsonObject definition)
    {
        var text = StudioJson.Text(value);
        var valid = type switch
        {
            "String" => text is not null && text.Length <= (definition["maxLength"]?.GetValue<int>() ?? 65536),
            "Int32" => value is JsonValue intValue && intValue.TryGetValue<int>(out _),
            "Int64" => text is not null && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
            "Decimal" => text is not null && ExactDecimal(text),
            "Boolean" => value is JsonValue boolValue && boolValue.TryGetValue<bool>(out _),
            "Guid" => text is not null && Guid.TryParse(text, out _),
            "Date" => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "DateTime" => text is not null && System.Text.RegularExpressions.Regex.IsMatch(text, @"(Z|[+-]\d{2}:\d{2})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            "Array" => value is JsonArray { Count: <= 1000 },
            _ => false,
        };
        if (!valid) throw new StudioException("TYPE_MISMATCH", "值类型与声明合同不一致。", path: path);
        if (value is JsonArray values)
        {
            var itemType = StudioJson.Text(definition["itemType"]) ?? throw new StudioException("TYPE_MISMATCH", "数组参数必须声明itemType。", path: path);
            foreach (var item in values)
            {
                if (item is null) throw new StudioException("TYPE_MISMATCH", "数组参数不接受空元素。", path: path);
                CheckScalar(itemType, item, path, new());
            }
        }
        if (definition["enumValues"] is JsonArray options && !options.Any(item => JsonNode.DeepEquals(item, value))) throw new StudioException("TYPE_MISMATCH", "参数值不在声明枚举中。", path: path);
        if (definition["min"] is { } min && decimal.TryParse(text ?? value.ToJsonString(), CultureInfo.InvariantCulture, out var number) && decimal.TryParse(StudioJson.Text(min) ?? min.ToJsonString(), CultureInfo.InvariantCulture, out var minimum) && number < minimum) throw new StudioException("TYPE_MISMATCH", "参数低于允许范围。", path: path);
        if (definition["max"] is { } max && decimal.TryParse(text ?? value.ToJsonString(), CultureInfo.InvariantCulture, out var numberMax) && decimal.TryParse(StudioJson.Text(max) ?? max.ToJsonString(), CultureInfo.InvariantCulture, out var maximum) && numberMax > maximum) throw new StudioException("TYPE_MISMATCH", "参数高于允许范围。", path: path);
    }
    private static bool ExactDecimal(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return false;
        static string Normalize(string source)
        {
            var negative = source.StartsWith('-');
            var parts = source.TrimStart('+', '-').Split('.');
            var whole = parts[0].TrimStart('0');
            if (whole.Length == 0) whole = "0";
            var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
            return (negative && (whole != "0" || fraction.Length > 0) ? "-" : "") + whole + (fraction.Length > 0 ? "." + fraction : "");
        }
        return Normalize(text) == Normalize(value.ToString("0.############################", CultureInfo.InvariantCulture));
    }
}
