using System.Text.Json.Nodes;

namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>Derives template entry schemas from pinned datasets, so subtree mappings are checked before publishing.</summary>
public static class StudioReportContracts
{
    public static JsonObject? Path(JsonObject? schema, JsonArray? path)
    {
        var current = schema;
        foreach (var segment in path ?? [])
        {
            var key = StudioJson.Text(segment);
            current = StudioJson.Text(current?["type"]) == "Array" && int.TryParse(key, out _) ? current?["item"] as JsonObject : (current?["children"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(f => StudioJson.Text(f["name"]) == key);
            if (current is null) return null;
        }
        return current;
    }
    public static Dictionary<string, JsonObject> BandSchemas(JsonObject document, IReadOnlyDictionary<string, JsonObject> datasets, List<StudioDiagnostic> errors)
    {
        var instances = (document["instances"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(i => StudioJson.RequiredString(i, "instanceId"));
        var bands = (document["bands"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(b => StudioJson.RequiredString(b, "id"));
        var schemas = new Dictionary<string, JsonObject>(); var visiting = new HashSet<string>();
        JsonObject? Resolve(string id)
        {
            if (schemas.TryGetValue(id, out var existing)) return existing;
            if (!bands.TryGetValue(id, out var band) || !visiting.Add(id)) return null;
            JsonObject? schema = null;
            if (band["data"] is JsonObject collection)
            {
                var mode = StudioJson.Text(collection["mode"]);
                if (mode == "Dataset") datasets.TryGetValue(StudioJson.Text(collection["datasetRevisionNId"]) ?? "", out schema);
                if (mode == "ParentPath" && StudioJson.Text(band["parentBandId"]) is { } parent) schema = Resolve(parent);
                if (mode == "RootPath" && instances.TryGetValue(StudioJson.Text(collection["instanceId"]) ?? "", out var instance))
                {
                    if (StudioJson.Text(instance["sourceType"]) == "Constant") schema = Infer(instance["value"]);
                    else datasets.TryGetValue(StudioJson.Text(instance["revisionNId"]) ?? "", out schema);
                }
                schema = Path(schema, collection["path"] as JsonArray);
                if (StudioJson.Text(schema?["type"]) != "Array" || schema?["item"] is not JsonObject item) errors.Add(new("PATH_INVALID", "bands." + id + ".data", "固定Schema中的带数据路径不是数组。"));
                else schemas[id] = schema = item;
            }
            visiting.Remove(id); return schema;
        }
        foreach (var id in bands.Keys) _ = Resolve(id);
        return schemas;
    }
    public static JsonObject Infer(JsonNode? value)
    {
        if (value is JsonArray rows) return new() { ["type"] = "Array", ["item"] = rows.Count == 0 ? new JsonObject { ["type"] = "Object", ["children"] = new JsonArray() } : Infer(rows[0]) };
        if (value is JsonObject obj) return new() { ["type"] = "Object", ["children"] = new JsonArray(obj.Select(p => { var schema = Infer(p.Value); schema["name"] = p.Key; return (JsonNode)schema; }).ToArray()) };
        return new() { ["type"] = value is JsonValue v && v.TryGetValue<bool>(out _) ? "Boolean" : value is JsonValue n && n.TryGetValue<double>(out _) ? "Decimal" : "String", ["nullable"] = value is null };
    }
    public static JsonArray EntryBands(JsonObject document, IReadOnlyDictionary<string, JsonObject> datasets, List<StudioDiagnostic> diagnostics)
    {
        var schemas = BandSchemas(document, datasets, diagnostics);
        var bands = (document["bands"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
        return new JsonArray(bands.Where(b => StudioJson.Text(b["kind"]) == "Detail").Select(b => (JsonNode)new JsonObject { ["id"] = b["id"]?.DeepClone(), ["name"] = b["name"]?.DeepClone(), ["parentBandId"] = b["parentBandId"]?.DeepClone(), ["rowSchema"] = schemas.GetValueOrDefault(StudioJson.RequiredString(b, "id"))?.DeepClone(), ["requiredAncestorBands"] = new JsonArray(RequiredAncestors(document, StudioJson.RequiredString(b, "id")).Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()) }).ToArray());
    }
    public static IReadOnlySet<string> RequiredAncestors(JsonObject document, string entry)
    {
        var bands = (document["bands"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(b => StudioJson.RequiredString(b, "id"));
        var ancestors = new List<string>(); var current = entry;
        while (bands.TryGetValue(current, out var b) && StudioJson.Text(b["parentBandId"]) is { Length: > 0 } parent && !ancestors.Contains(parent)) { ancestors.Add(parent); current = parent; }
        var required = new HashSet<string>(StringComparer.Ordinal);
        void Visit(JsonNode? node)
        {
            if (node is JsonArray arr) { foreach (var item in arr) Visit(item); return; }
            if (node is not JsonObject obj) return;
            var from = StudioJson.Text(obj["from"]);
            if (from == "Constant") return;
            if (StudioJson.Text(obj["contextBandId"]) is { } band && ancestors.Contains(band)) required.Add(band);
            if (from == "Root" && ancestors.Count > 0) required.Add(ancestors[^1]);
            if (from == "ParentRow" && ancestors.Count > 0) required.Add(ancestors[0]);
            foreach (var pair in obj) Visit(pair.Value);
        }
        bool Descendant(string id) { var seen = new HashSet<string>(); while (seen.Add(id) && bands.TryGetValue(id, out var b)) { if (id == entry) return true; if (StudioJson.Text(b["parentBandId"]) is not { } p) return false; id = p; } return false; }
        foreach (var band in bands.Values.Where(b => Descendant(StudioJson.RequiredString(b, "id")))) Visit(band["controls"]);
        return required;
    }
    public static void ValidateBindings(JsonObject document, IReadOnlyDictionary<string, JsonObject> datasets, List<StudioDiagnostic> errors)
    {
        var schemas = BandSchemas(document, datasets, errors);
        var instances = (document["instances"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(i => StudioJson.RequiredString(i, "instanceId"));
        var bands = (document["bands"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(b => StudioJson.RequiredString(b, "id"));
        foreach (var (id, band) in bands)
        {
            var ancestors = new List<string>(); var cursor = id;
            JsonObject? collectionParent = null;
            while (bands.TryGetValue(cursor, out var current) && StudioJson.Text(current["parentBandId"]) is { Length: > 0 } parent && !ancestors.Contains(parent)) { ancestors.Add(parent); cursor = parent; }
            JsonObject? BindingSchema(JsonObject binding, JsonObject? rowSchema)
            {
                var from = StudioJson.Text(binding["from"]); JsonObject? schema = null;
                if (from == "Constant") return Infer(binding["value"]);
                if (binding["contextBandId"] is { } context) schemas.TryGetValue(StudioJson.Text(context) ?? "", out schema);
                else if (from == "Row") schema = rowSchema;
                else if (from == "ParentRow" && collectionParent is not null) schema = collectionParent;
                else if (from == "ParentRow" && ancestors.Count > 0) schemas.TryGetValue(ancestors[0], out schema);
                else if (from == "Root") schemas.TryGetValue(ancestors.Count > 0 ? ancestors[^1] : id, out schema);
                else if (from == "Instance" && instances.TryGetValue(StudioJson.Text(binding["instanceId"]) ?? "", out var instance)) { if (StudioJson.Text(instance["sourceType"]) == "Constant") schema = Infer(instance["value"]); else datasets.TryGetValue(StudioJson.Text(instance["revisionNId"]) ?? "", out schema); }
                if (from is "Context" or "PageParam") return new JsonObject { ["type"] = "String" }; // parameter types are checked by DatasetValues before execution.
                return Path(schema, binding["path"] as JsonArray);
            }
            void Check(JsonNode? node, string path, JsonObject? rowSchema)
            {
                if (node is JsonArray array) { for (var i = 0; i < array.Count; i++) Check(array[i], path + "." + i, rowSchema); return; }
                if (node is not JsonObject obj) return;
                if (StudioJson.Text(obj["from"]) is { } from)
                {
                    if (from is "Form" or "Node") errors.Add(new("PATH_INVALID", path, "报告不提供该上下文。"));
                    if (from != "Constant" && BindingSchema(obj, rowSchema) is null) errors.Add(new("PATH_INVALID", path, "绑定字段不在固定报告行Schema中。"));
                    return;
                }
                foreach (var pair in obj) Check(pair.Value, path + "." + pair.Key, rowSchema);
            }
            foreach (var control in (band["controls"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var path = "bands." + id + ".controls." + StudioJson.RequiredString(control, "id");
                var rowSchema = schemas.GetValueOrDefault(id);
                collectionParent = null;
                foreach (var pair in control.Where(p => p.Key is not ("table" or "aggregate"))) Check(pair.Value, path + "." + pair.Key, rowSchema);
                if (control["aggregate"] is JsonObject aggregate && aggregate["field"] is { } field) Check(field, path + ".aggregate.field", schemas.GetValueOrDefault(StudioJson.Text(aggregate["scopeBandId"]) ?? ""));
                if (control["table"] is JsonObject table)
                {
                    if (StudioJson.Text(table["rowMode"]) == "Collection" && control["value"] is JsonObject binding)
                    {
                        var collectionSchema = BindingSchema(binding, rowSchema);
                        collectionParent = rowSchema;
                        if (StudioJson.Text(collectionSchema?["type"]) != "Array") errors.Add(new("TYPE_MISMATCH", path + ".value", "表格集合绑定必须是数组Schema。"));
                        rowSchema = collectionSchema?["item"] as JsonObject;
                    }
                    Check(table["columns"], path + ".table.columns", rowSchema);
                }
            }
        }
    }
}
