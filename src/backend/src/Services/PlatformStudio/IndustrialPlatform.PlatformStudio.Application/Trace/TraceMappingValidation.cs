using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Trace;

/// <summary>发布前核对固定Dataset参数、可读字段与集合路径；保存草稿仍允许待修复诊断。</summary>
internal static class TraceMappingValidation
{
    public static void Validate(JsonObject document, Dictionary<string, JsonObject> datasets, List<StudioDiagnostic> diagnostics)
    {
        void Error(string path, string message) => diagnostics.Add(new("TRACE_MAPPING_INVALID", path, message));
        JsonObject? Dataset(JsonNode? revision) => StudioJson.Text(revision) is { } id ? datasets.GetValueOrDefault(id) : null;
        JsonObject? SchemaAt(JsonNode? schema, JsonArray? path)
        {
            var current = schema as JsonObject;
            if (path is null) return null;
            foreach (var segment in path)
            {
                if (StudioJson.Text(current?["type"]) != "Object") return null;
                current = (current?["children"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(f => StudioJson.Text(f["name"]) == StudioJson.Text(segment));
            }
            return current;
        }
        void CheckBindings(JsonNode? value, JsonObject? row, JsonObject dataset, string path)
        {
            if (value is JsonObject obj)
            {
                if (StudioJson.Text(obj["from"]) is { } from)
                {
                    if (from == "Row")
                    {
                        var field = SchemaAt(row, obj["path"] as JsonArray);
                        var readable = (dataset["readableFieldIds"] as JsonArray ?? []).Select(StudioJson.Text).ToHashSet(StringComparer.Ordinal);
                        if (field is null || StudioJson.Text(field["type"]) is "Object" or "Array" || !readable.Contains(StudioJson.Text(field["id"]))) Error(path, "Row绑定须指向来源合同的可读标量字段。");
                    }
                    else if (from == "PageParam")
                    {
                        var name = StudioJson.Text((obj["path"] as JsonArray)?.FirstOrDefault());
                        if (!(document["parameters"] as JsonArray ?? []).OfType<JsonObject>().Any(x => StudioJson.Text(x["name"]) == name)) Error(path, "页面参数未声明。");
                    }
                    else if (from is not ("Node" or "Context" or "Constant")) Error(path, "追溯不提供该绑定上下文。");
                    return;
                }
                foreach (var field in obj) CheckBindings(field.Value, row, dataset, path + "." + field.Key);
            }
            else if (value is JsonArray array)
                for (var i = 0; i < array.Count; i++) CheckBindings(array[i], row, dataset, path + "[" + i + "]");
        }
        void Parameters(JsonNode? maps, JsonObject dataset, string path)
        {
            var definitions = (dataset["parameters"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
            var targets = (maps as JsonArray ?? []).OfType<JsonObject>().Select(x => StudioJson.Text(x["target"])).ToHashSet(StringComparer.Ordinal);
            foreach (var target in targets) if (!definitions.Any(x => StudioJson.Text(x["name"]) == target)) Error(path, "映射参数不在Dataset合同声明中。");
            var paging = dataset["pagination"]?["input"] as JsonObject;
            var pagingNames = paging?.Select(x => StudioJson.Text(x.Value)).ToHashSet(StringComparer.Ordinal) ?? [];
            foreach (var definition in definitions)
            {
                var name = StudioJson.Text(definition["name"]);
                if (definition["required"]?.GetValue<bool>() == true && definition["defaultValue"] is null && StudioJson.Text(definition["source"]) != "TrustedContext" && !pagingNames.Contains(name) && !targets.Contains(name)) Error(path, "缺少Dataset必需参数映射：" + name);
            }
        }
        JsonObject? RowSchema(JsonObject dataset, JsonArray? path, string errorPath)
        {
            var collection = SchemaAt(dataset["result"], path);
            if (StudioJson.Text(collection?["type"]) != "Array" || StudioJson.Text(collection?["item"]?["type"]) != "Object") { Error(errorPath, "集合路径须指向对象数组。"); return null; }
            return collection!["item"]!.AsObject();
        }
        void SectionParameterTypes(JsonNode? maps, JsonObject? detailRow, JsonObject dataset, string path)
        {
            var definitions = (dataset["parameters"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
            var pagingNames = (dataset["pagination"]?["input"] as JsonObject)?.Select(x => StudioJson.Text(x.Value)).ToHashSet(StringComparer.Ordinal) ?? [];
            foreach (var map in (maps as JsonArray ?? []).OfType<JsonObject>())
            {
                var target = StudioJson.Text(map["target"]);
                var definition = definitions.SingleOrDefault(d => StudioJson.Text(d["name"]) == target);
                if (definition is null || map["source"] is not JsonObject source) continue;
                var bindingPath = path + "." + target;
                if (StudioJson.Text(definition["source"]) == "TrustedContext" || pagingNames.Contains(target))
                { Error(bindingPath, "分组不能映射由服务端填充的可信上下文或分页参数。"); continue; }
                var segments = (source["path"] as JsonArray ?? []).Select(StudioJson.Text).ToArray();
                JsonObject? sourceSchema = StudioJson.Text(source["from"]) switch
                {
                    "Row" => SchemaAt(detailRow, source["path"] as JsonArray),
                    "Node" when segments is ["key" or "objectTypeId" or "grain"] or ["identity", "tenantNId" or "sourceSystem" or "factory" or "businessKey"] => new() { ["type"] = "String" },
                    "PageParam" when segments.Length == 1 => (document["parameters"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(d => StudioJson.Text(d["name"]) == segments[0]),
                    "Context" when segments is ["tenant" or "user"] => new() { ["type"] = "String" },
                    "Context" when segments is ["authorizedFactoryIds"] => new() { ["type"] = "Array", ["itemType"] = "String" },
                    _ => null,
                };
                if (StudioJson.Text(source["from"]) == "Constant")
                {
                    try
                    {
                        if (source["value"] is { } value) DatasetValues.CheckScalar(StudioJson.RequiredString(definition, "type"), value, bindingPath, definition);
                        else if (definition["required"]?.GetValue<bool>() == true || definition["nullable"]?.GetValue<bool>() == false) Error(bindingPath, "必填参数不能绑定空常量。");
                    }
                    catch (StudioException) { Error(bindingPath, "常量类型与数据集参数类型不符。"); }
                }
                else if (sourceSchema is null || StudioJson.Text(sourceSchema["type"]) != StudioJson.Text(definition["type"]) || StudioJson.Text(definition["type"]) == "Array" && StudioJson.Text(sourceSchema["itemType"]) != StudioJson.Text(definition["itemType"]))
                    Error(bindingPath, "绑定来源路径或类型与数据集参数合同不符。");
            }
        }
        var start = document["start"] as JsonObject;
        if (start is not null && Dataset(start["datasetRevisionNId"]) is { } startDataset)
        {
            var row = RowSchema(startDataset, start["collectionPath"] as JsonArray, "start.collectionPath");
            CheckBindings(start, row, startDataset, "start"); Parameters(start["parameterMap"], startDataset, "start.parameterMap");
            foreach (var column in (start["columns"] as JsonArray ?? []).OfType<JsonObject>())
                if (!(row?["children"] as JsonArray ?? []).OfType<JsonObject>().Any(x => StudioJson.Text(x["id"]) == StudioJson.Text(column["fieldId"]))) Error("start.columns", "起点列不在集合字段合同中。");
        }
        foreach (var obj in (document["objects"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (Dataset(obj["detail"]?["datasetRevisionNId"]) is not { } dataset) continue;
            var schema = dataset["result"] as JsonObject;
            var row = StudioJson.Text(schema?["type"]) == "Array" ? schema?["item"] as JsonObject : (schema?["children"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(x => StudioJson.Text(x["name"]) == "rows" && StudioJson.Text(x["type"]) == "Array")?["item"] as JsonObject ?? schema;
            var objectPath = "objects." + StudioJson.Text(obj["id"]);
            // 分组输出的Row属于其自己的数据集；关联参数的Row才属于节点主明细。
            foreach (var member in obj.Where(member => member.Key != "detail")) CheckBindings(member.Value, row, dataset, objectPath + "." + member.Key);
            foreach (var member in obj["detail"]!.AsObject().Where(member => member.Key != "sections")) CheckBindings(member.Value, row, dataset, objectPath + ".detail." + member.Key);
            Parameters(obj["detail"]?["parameterMap"], dataset, objectPath + ".detail.parameterMap");
            foreach (var section in (obj["detail"]?["sections"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Dataset(section["datasetRevisionNId"]) is not { } sectionDataset) continue;
                var sectionPath = objectPath + ".detail.sections." + StudioJson.Text(section["id"]);
                CheckBindings(section["parameterMap"], row, dataset, sectionPath + ".parameterMap");
                Parameters(section["parameterMap"], sectionDataset, sectionPath + ".parameterMap");
                SectionParameterTypes(section["parameterMap"], row, sectionDataset, sectionPath + ".parameterMap");
                var sectionSchema = SchemaAt(sectionDataset["result"], section["collectionPath"] as JsonArray);
                var sectionRow = StudioJson.Text(sectionSchema?["type"]) == "Array" ? sectionSchema?["item"] as JsonObject : sectionSchema;
                var kind = StudioJson.Text(section["kind"]);
                if (StudioJson.Text(sectionRow?["type"]) != "Object" || kind == "Table" && StudioJson.Text(sectionSchema?["type"]) != "Array") Error(sectionPath + ".collectionPath", "字段分组须指向对象或对象数组，表格分组须指向对象数组。");
                if (kind == "Table")
                {
                    if (StudioJson.Text(sectionDataset["pagination"]?["mode"]) != "Offset" || string.IsNullOrWhiteSpace(StudioJson.Text(sectionDataset["pagination"]?["result"]?["totalCountPath"]))) Error(sectionPath + ".datasetRevisionNId", "表格分组须使用声明页码分页及真实总数的数据集。");
                    var maxSize = sectionDataset["pagination"]?["maxSize"]?.GetValue<int>() ?? 200;
                    if (section["pageSize"] is JsonValue pageSize && pageSize.TryGetValue<int>(out var size) && size > maxSize) Error(sectionPath + ".pageSize", "分组分页大小超过数据集允许上限。");
                }
                CheckBindings(section["fields"], sectionRow, sectionDataset, sectionPath + ".fields");
                if (section["rowKey"] is not null) CheckBindings(section["rowKey"], sectionRow, sectionDataset, sectionPath + ".rowKey");
                var readable = (sectionDataset["readableFieldIds"] as JsonArray ?? []).Select(StudioJson.Text).ToHashSet(StringComparer.Ordinal);
                foreach (var column in (section["columns"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    var fieldId = StudioJson.Text(column["fieldId"]);
                    var field = (sectionRow?["children"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(f => StudioJson.Text(f["id"]) == fieldId);
                    if (field is null || StudioJson.Text(field["type"]) is "Array" or "Object" || !readable.Contains(fieldId)) Error(sectionPath + ".columns", "分组列须引用该集合的可读标量字段。");
                }
            }
        }
        foreach (var relation in (document["relations"] as JsonArray ?? []).OfType<JsonObject>())
        foreach (var direction in new[] { "forward", "reverse" })
        {
            if (relation[direction] is not JsonObject traversal || Dataset(traversal["datasetRevisionNId"]) is not { } dataset) continue;
            var path = "relations." + StudioJson.Text(relation["id"]) + "." + direction;
            var row = RowSchema(dataset, traversal["result"]?["collectionPath"] as JsonArray, path + ".result.collectionPath");
            CheckBindings(traversal, row, dataset, path);
            foreach (var field in new[] { "identity", "evidence", "label" }) CheckBindings(relation[field], row, dataset, path + "." + field);
            Parameters(traversal["inputMap"], dataset, path + ".inputMap");
        }
    }
}
