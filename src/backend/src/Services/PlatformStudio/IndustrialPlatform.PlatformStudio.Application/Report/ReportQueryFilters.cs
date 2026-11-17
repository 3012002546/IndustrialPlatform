using System.Globalization;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Report;

/// <summary>Published field definitions authorize filters; query and export share the same pre-pagination predicate.</summary>
public static class ReportQueryFilters
{
    public static JsonArray Resolve(JsonObject document, JsonArray? requested)
    {
        if (requested is null || requested.Count == 0) return new();
        if (requested.Count > 100 || requested.ToJsonString().Length > 32768 || StudioJson.Text(document["mode"]) != "Query") throw Invalid();
        var definitions = (document["query"]?["filters"] as JsonArray ?? []).OfType<JsonObject>().Where(f => f.ContainsKey("fieldId")).ToArray();
        var result = new JsonArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in requested)
        {
            if (item is not JsonObject input || input.Any(p => p.Key is not ("fieldId" or "value" or "min" or "max"))) throw Invalid();
            var id = StudioJson.RequiredString(input, "fieldId");
            var definition = definitions.SingleOrDefault(f => StudioJson.Text(f["fieldId"]) == id);
            if (definition is null || !seen.Add(id)) throw new StudioException("STUDIO_FORBIDDEN", "筛选字段未配置或重复。", 403, "filters");
            var type = StudioJson.Text(definition["dataType"]) ?? "String";
            var filter = new JsonObject { ["fieldId"] = id };
            if (type is "String" or "Boolean")
            {
                if (input.ContainsKey("min") || input.ContainsKey("max")) throw Invalid();
                if (input["value"] is null) continue;
                if (type == "String")
                {
                    if (input["value"] is not JsonValue text || !text.TryGetValue<string>(out var value) || value.Length > 1000) throw Invalid();
                    if (value.Length == 0) continue;
                }
                else if (input["value"] is not JsonValue boolean || !boolean.TryGetValue<bool>(out _)) throw Invalid();
                filter["value"] = input["value"]!.DeepClone();
            }
            else
            {
                if (input.ContainsKey("value")) throw Invalid();
                foreach (var bound in new[] { "min", "max" })
                    if (input[bound] is { } value)
                    {
                        if (!TryComparable(value, type, out _)) throw Invalid();
                        filter[bound] = value.DeepClone();
                    }
                if (filter.Count == 1) continue;
                if (filter["min"] is { } min && filter["max"] is { } max && Compare(min, max, type) > 0) throw new StudioException("STUDIO_VALIDATION", "筛选起始值不能大于结束值。", 422, "filters");
            }
            result.Add(filter);
        }
        return result;
    }

    public static JsonArray Compile(JsonObject source, JsonArray? requested)
    {
        var filters = Resolve(source, requested);
        var document = source.DeepClone().AsObject();
        if (filters.Count == 0) return filters;
        var query = document["query"]!.AsObject();
        var band = document["bands"]!.AsArray().OfType<JsonObject>().Single(b => StudioJson.Text(b["id"]) == StudioJson.Text(query["resultBandId"]));
        foreach (var filter in filters.OfType<JsonObject>())
        {
            var id = StudioJson.Text(filter["fieldId"]);
            var column = query["columns"]!.AsArray().OfType<JsonObject>().Single(c => StudioJson.Text(c["columnId"]) == id);
            var table = band["controls"]!.AsArray().OfType<JsonObject>().Single(c => StudioJson.Text(c["id"]) == StudioJson.Text(column["controlId"]))["table"]!.AsObject();
            var binding = ReportTableTemplates.Binding(table, table["columns"]!.AsArray().OfType<JsonObject>().Single(c => StudioJson.Text(c["id"]) == id));
            if (StudioJson.Text(binding["from"]) != "Row") throw Invalid();
            filter["binding"] = binding.DeepClone();
            filter["dataType"] = query["filters"]!.AsArray().OfType<JsonObject>().Single(f => StudioJson.Text(f["fieldId"]) == id)["dataType"]?.DeepClone() ?? JsonValue.Create("String");
        }
        return filters;
    }

    public static bool Matches(JsonArray? filters, IReadOnlyDictionary<string, JsonNode?> context)
    {
        foreach (var filter in (filters ?? []).OfType<JsonObject>())
        {
            var value = ReportDataResolver.Resolve(filter["binding"]!.AsObject(), context);
            if (value is null) return false;
            var type = StudioJson.Text(filter["dataType"]) ?? "String";
            if (type == "String")
            {
                if (!(StudioJson.Text(value) ?? value.ToJsonString()).Contains(StudioJson.Text(filter["value"])!, StringComparison.OrdinalIgnoreCase)) return false;
            }
            else if (type == "Boolean") { if (!JsonNode.DeepEquals(value, filter["value"])) return false; }
            else
            {
                if (!TryComparable(value, type, out _)) return false;
                if (filter["min"] is { } min && Compare(value, min, type) < 0 || filter["max"] is { } max && Compare(value, max, type) > 0) return false;
            }
        }
        return true;
    }

    private static int Compare(JsonNode left, JsonNode right, string type)
    {
        TryComparable(left, type, out var a); TryComparable(right, type, out var b); return a.CompareTo(b);
    }
    private static bool TryComparable(JsonNode value, string type, out decimal result)
    {
        result = 0;
        var text = StudioJson.Text(value) ?? value.ToJsonString();
        if (type is "Date" or "DateTime")
        {
            // Calendar-date filters include the whole selected end day, including timestamp values.
            if (text.Length < 10 || !DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return false;
            result = day.DayNumber; return true;
        }
        return type is "Int32" or "Int64" or "Decimal" && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out result);
    }
    private static StudioException Invalid() => new("STUDIO_VALIDATION", "筛选值或字段类型无效。", 422, "filters");
}
