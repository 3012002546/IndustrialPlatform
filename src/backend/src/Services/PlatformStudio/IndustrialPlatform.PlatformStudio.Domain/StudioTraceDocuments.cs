using System.Text.Json.Nodes;

namespace IndustrialPlatform.PlatformStudio.Domain;

public static partial class StudioDocuments
{
    private static void ValidateTrace(JsonObject doc, List<StudioDiagnostic> errors)
    {
        Allowed(doc, ["schemaVersion", "parameters", "scope", "start", "objects", "relations", "layout", "limits", "presentation"], "", errors);
        void Error(string path, string message) => errors.Add(new("TRACE_MAPPING_INVALID", path, message));
        if (doc.ContainsKey("presentation"))
        {
            if (doc["presentation"] is not JsonObject presentation) Error("presentation", "运行展示配置须为对象。");
            else
            {
                Allowed(presentation, ["layoutDirection"], "presentation", errors);
                if (StudioJson.Text(presentation["layoutDirection"]) is not ("LeftToRight" or "TopToBottom")) Error("presentation.layoutDirection", "布局方向须为横向或竖向。");
            }
        }
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in Objects(doc["parameters"], "parameters", errors))
        {
            var name = StudioJson.RequiredString(parameter, "name");
            if (!parameterNames.Add(name)) Error("parameters." + name, "参数名称重复。");
            if (StudioJson.Text(parameter["type"]) is not ("String" or "Int32" or "Int64" or "Decimal" or "Boolean" or "Date" or "DateTime" or "Guid" or "Array")) Error("parameters." + name + ".type", "参数类型未登记。");
            if (StudioJson.Text(parameter["type"]) == "Array" && StudioJson.Text(parameter["itemType"]) is not ("String" or "Int32" or "Int64" or "Decimal" or "Boolean" or "Date" or "DateTime" or "Guid")) Error("parameters." + name + ".itemType", "数组须声明标量类型。");
            if (StudioJson.Text(parameter["source"]) is not (null or "Caller" or "TrustedContext")) Error("parameters." + name + ".source", "参数来源未登记。");
            if (StudioJson.Text(parameter["source"]) == "TrustedContext" && StudioJson.Text(parameter["contextKey"]) is not ("tenant" or "user" or "authorizedFactoryIds")) Error("parameters." + name + ".contextKey", "可信上下文未登记。");
        }
        void Binding(JsonNode? node, string path) => ValidateBinding(node, path, [], errors);
        void Identity(JsonNode? node, string path)
        {
            foreach (var key in new[] { "sourceSystem", "factory", "businessKey" }) Binding(node?[key], path + "." + key);
        }
        void Maps(JsonNode? node, string path)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var map in Objects(node, path, errors))
            {
                var name = StudioJson.Text(map["target"]);
                if (string.IsNullOrWhiteSpace(name) || !names.Add(name)) Error(path, "目标参数缺失或重复。");
                Binding(map["source"], path + ".source");
            }
        }
        void Path(JsonNode? node, string path)
        {
            if (node is not JsonArray a || a.Count > 32 || a.Any(x => StudioJson.Text(x) is not { Length: > 0 and <= 128 } s || s is "__proto__" or "constructor" or "prototype")) Error(path, "须声明安全字段路径数组。");
        }
        var objects = Objects(doc["objects"], "objects", errors);
        var relations = Objects(doc["relations"], "relations", errors);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var relationIds = new HashSet<string>(StringComparer.Ordinal);
        if (objects.Length == 0 || objects.Length > 128 || relations.Length > 256) Error("objects", "对象数量须为1～128，关系最多256。");
        foreach (var obj in objects)
        {
            var id = StudioJson.RequiredString(obj, "id", 128);
            if (!ids.Add(id)) Error("objects." + id, "对象ID重复。");
            Identity(obj["identity"], "objects." + id + ".identity");
            if (StudioJson.Text(obj["identity"]?["grain"]) is not ("Batch" or "Package" or "Serial" or "WorkOrder" or "Operation" or "Custom")) Error("objects." + id + ".identity.grain", "粒度未登记。");
            if (string.IsNullOrWhiteSpace(StudioJson.Text(obj["name"]))) Error("objects." + id + ".name", "对象须有名称。");
            foreach (var field in Objects(obj["summary"], "summary", errors)) Binding(field["value"], "objects." + id + ".summary.value");
            var detail = obj["detail"] as JsonObject;
            if (detail is null || string.IsNullOrWhiteSpace(StudioJson.Text(detail["datasetRevisionNId"]))) Error("objects." + id + ".detail", "须声明明细数据集固定修订。");
            if (detail is null) continue;
            Maps(detail["parameterMap"], "objects." + id + ".detail.parameterMap");
            foreach (var field in Objects(detail["fields"], "fields", errors)) Binding(field["value"], "objects." + id + ".detail.fields.value");
            if (detail.ContainsKey("sections"))
            {
                var path = "objects." + id + ".detail.sections";
                if (detail["sections"] is not JsonArray sections || sections.Count > 16) Error(path, "明细分组须为数组，最多16组。");
                var sectionIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var section in Objects(detail["sections"], path, errors))
                {
                    var sectionId = StudioJson.Text(section["id"]);
                    var sectionPath = path + "." + sectionId;
                    Allowed(section, ["id", "label", "kind", "datasetRevisionNId", "parameterMap", "collectionPath", "fields", "columns", "rowKey", "pageSize"], sectionPath, errors);
                    if (string.IsNullOrWhiteSpace(sectionId) || sectionId.Length > 128 || !sectionIds.Add(sectionId)) Error(sectionPath + ".id", "分组ID缺失或重复。");
                    if (string.IsNullOrWhiteSpace(StudioJson.Text(section["label"]))) Error(sectionPath + ".label", "分组须有名称。");
                    var kind = StudioJson.Text(section["kind"]);
                    if (kind is not ("Fields" or "Table")) Error(sectionPath + ".kind", "分组显示方式未登记。");
                    if (string.IsNullOrWhiteSpace(StudioJson.Text(section["datasetRevisionNId"]))) Error(sectionPath + ".datasetRevisionNId", "分组须声明已发布数据集固定修订。");
                    Maps(section["parameterMap"], sectionPath + ".parameterMap");
                    var associations = (section["parameterMap"] as JsonArray ?? []).OfType<JsonObject>().Where(m => StudioJson.Text(m["source"]?["from"]) is "Node" or "Row").ToArray();
                    if (associations.Length == 0) Error(sectionPath + ".parameterMap", "分组须通过节点或主明细字段绑定关联参数。");
                    if (associations.Any(m => StudioJson.Text(m["source"]?["nullPolicy"]) != "Error")) Error(sectionPath + ".parameterMap", "节点关联参数须使用报错空值策略，不能退化为无条件查询。");
                    Path(section["collectionPath"], sectionPath + ".collectionPath");
                    if (section["pageSize"] is not JsonValue pageSize || !pageSize.TryGetValue<int>(out var size) || size is < 1 or > 200) Error(sectionPath + ".pageSize", "分组分页大小须为1～200。");
                    var fields = Objects(section["fields"], sectionPath + ".fields", errors);
                    if (kind == "Fields" && fields.Length == 0 || fields.Length > 128) Error(sectionPath + ".fields", "字段分组须配置1～128个字段。");
                    foreach (var field in fields)
                    {
                        if (string.IsNullOrWhiteSpace(StudioJson.Text(field["label"]))) Error(sectionPath + ".fields.label", "字段须有名称。");
                        Binding(field["value"], sectionPath + ".fields.value");
                    }
                    var columns = Objects(section["columns"], sectionPath + ".columns", errors);
                    if (kind == "Table" && columns.Length == 0 || columns.Length > 128) Error(sectionPath + ".columns", "表格分组须配置1～128列。");
                    var columnIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var column in columns)
                    {
                        var fieldId = StudioJson.Text(column["fieldId"]);
                        if (string.IsNullOrWhiteSpace(fieldId) || !columnIds.Add(fieldId)) Error(sectionPath + ".columns.fieldId", "列字段缺失或重复。");
                        if (string.IsNullOrWhiteSpace(StudioJson.Text(column["label"]))) Error(sectionPath + ".columns.label", "列须有名称。");
                    }
                    if (kind == "Table" || section["rowKey"] is not null) Binding(section["rowKey"], sectionPath + ".rowKey");
                    if (kind == "Table" && StudioJson.Text(section["rowKey"]?["from"]) != "Row") Error(sectionPath + ".rowKey", "表格行标识须绑定本分组的结果行字段。");
                }
            }
            var links = new HashSet<string>();
            foreach (var link in Objects(detail["links"], "links", errors))
            {
                var path = "objects." + id + ".detail.links";
                if (!links.Add(StudioJson.RequiredString(link, "id"))) Error(path, "链接ID重复。");
                Binding(link["value"], path + ".value"); Maps(link["parameterMap"], path + ".parameterMap");
                if (StudioJson.Text(link["openMode"]) != "Modal") Error(path + ".openMode", "详情必须在当前页模态框打开。");
                var kind = StudioJson.Text(link["kind"]);
                if (kind is not ("File" or "External")) Error(path + ".kind", "链接类型未登记。");
                if (kind == "External" && (string.IsNullOrWhiteSpace(StudioJson.Text(link["allowedOriginKey"])) || link["fileSource"] is not null)) Error(path, "外链须登记来源，且不可包含文件来源。");
                if (kind == "File" && link["fileSource"] is JsonObject file)
                {
                    if (StudioJson.Text(file["mode"]) is not ("Owned" or "Mes")) Error(path + ".fileSource.mode", "文件来源未登记。");
                    if (StudioJson.Text(file["mode"]) == "Mes" && string.IsNullOrWhiteSpace(StudioJson.Text(file["sourceKey"]))) Error(path + ".fileSource.sourceKey", "MES文件须登记来源键。");
                    if (file["version"] is not null) Binding(file["version"], path + ".fileSource.version");
                }
            }
        }
        if (doc["start"] is not JsonObject start) Error("start", "须声明授权起点。");
        else
        {
            if (start["objectTypeIds"] is not JsonArray types || types.Count == 0 || types.Any(t => !ids.Contains(StudioJson.Text(t) ?? ""))) Error("start.objectTypeIds", "起点对象类型不存在。");
            if (string.IsNullOrWhiteSpace(StudioJson.Text(start["datasetRevisionNId"]))) Error("start.datasetRevisionNId", "须声明起点固定修订。");
            Path(start["collectionPath"], "start.collectionPath"); Maps(start["parameterMap"], "start.parameterMap");
            Binding(start["rowKey"], "start.rowKey"); Binding(start["objectType"], "start.objectType"); Identity(start["identityMap"], "start.identityMap");
            var searchNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var search in Objects(start["searchFields"], "start.searchFields", errors))
            {
                var name = StudioJson.RequiredString(search, "parameterName");
                if (!parameterNames.Contains(name) || !searchNames.Add(name)) Error("start.searchFields." + name, "搜索参数未声明或重复。");
                var control = StudioJson.Text(search["control"]);
                if (control is not ("Text" or "Select" or "DateRange" or "BusinessSelector")) Error("start.searchFields." + name + ".control", "搜索控件未登记。");
                if (control is "Select" or "BusinessSelector" && (string.IsNullOrWhiteSpace(StudioJson.Text(search["optionsDatasetRevisionNId"])) || string.IsNullOrWhiteSpace(StudioJson.Text(search["valueFieldId"])) || string.IsNullOrWhiteSpace(StudioJson.Text(search["labelFieldId"])))) Error("start.searchFields." + name, "选择控件须声明授权选项来源与字段。");
            }
        }
        foreach (var relation in relations)
        {
            var id = StudioJson.RequiredString(relation, "id"); var path = "relations." + id;
            if (!relationIds.Add(id)) Error(path, "关系ID重复。");
            if (!ids.Contains(StudioJson.Text(relation["fromTypeId"]) ?? "") || !ids.Contains(StudioJson.Text(relation["toTypeId"]) ?? "")) Error(path, "关系对象类型不存在。");
            if (StudioJson.Text(relation["kind"]) is not ("Genealogy" or "Context")) Error(path + ".kind", "关系类型未登记。");
            if (StudioJson.Text(relation["semantic"]) is not ("Consume" or "Produce" or "Split" or "Merge" or "Ship" or "Use" or "Inspect" or "Custom")) Error(path + ".semantic", "关系语义未登记。");
            foreach (var direction in new[] { "forward", "reverse" })
            {
                if (relation[direction] is not JsonObject traversal) { Error(path + "." + direction, "正反映射须分别声明。"); continue; }
                if (string.IsNullOrWhiteSpace(StudioJson.Text(traversal["datasetRevisionNId"]))) Error(path + "." + direction, "须声明固定修订。");
                Maps(traversal["inputMap"], path + "." + direction + ".inputMap");
                var result = traversal["result"];
                if (StudioJson.Text(result?["targetObjectTypeId"]) != StudioJson.Text(relation[direction == "forward" ? "toTypeId" : "fromTypeId"])) Error(path + "." + direction + ".result.targetObjectTypeId", "映射目标与事实方向不符。");
                Path(result?["collectionPath"], path + "." + direction + ".result.collectionPath"); Identity(result?["targetIdentity"], path + "." + direction + ".result.targetIdentity");
                foreach (var field in Objects(result?["summaryMap"], path + ".summaryMap", errors)) Binding(field["value"] ?? field["source"], path + ".summaryMap.value");
            }
            Binding(relation["identity"], path + ".identity"); Binding(relation["label"], path + ".label");
            foreach (var key in new[] { "recordKey", "occurredOn" }) Binding(relation["evidence"]?[key], path + ".evidence." + key);
            foreach (var key in new[] { "quantity", "unit" }) if (relation["evidence"]?[key] is { } binding) Binding(binding, path + ".evidence." + key);
        }
        if (doc["layout"] is not JsonObject layout || layout["nodes"] is not JsonArray || layout["edges"] is not JsonArray) throw new StudioException("STUDIO_VALIDATION", "layout.nodes/edges须为数组。", path: "layout");
        foreach (var kind in new[] { "nodes", "edges" })
        {
            var seen = new HashSet<string>();
            var index = 0;
            foreach (var entry in layout[kind]!.AsArray())
            {
                if (entry is not JsonObject item) throw new StudioException("STUDIO_VALIDATION", "布局项须为对象。", path: "layout." + kind);
                var path = $"layout.{kind}[{index++}]"; var key = kind == "nodes" ? "objectTypeId" : "relationId";
                var id = StudioJson.RequiredString(item, key);
                if (!seen.Add(id) || !(kind == "nodes" ? ids : relationIds).Contains(id)) Error(path + "." + key, "布局ID重复或引用不存在。");
                void Number(JsonNode? value, string p, double min, double max) { if (!TryNumber(value, out var n) || n < min || n > max) throw new StudioException("STUDIO_VALIDATION", "布局几何超出范围。", path: p); }
                if (kind == "nodes")
                {
                    foreach (var k in new[] { "x", "y" }) Number(item[k], path + "." + k, -1000000, 1000000);
                    foreach (var k in new[] { "width", "height" }) Number(item[k], path + "." + k, 1, 10000);
                }
                else
                {
                    foreach (var (port, expected) in new[] { ("sourcePort", "out"), ("targetPort", "in") })
                    {
                        if (!item.ContainsKey(port)) throw new StudioException("STUDIO_VALIDATION", "端口字段缺失。", path: path + "." + port);
                        if (StudioJson.Text(item[port]) != expected) Error(path + "." + port, "端口断开或未登记，修复后才能发布。");
                    }
                    if (item["vertices"] is not JsonArray vertices) throw new StudioException("STUDIO_VALIDATION", "折点须为数组。", path: path + ".vertices");
                    foreach (var vertex in vertices) foreach (var k in new[] { "x", "y" }) Number(vertex?[k], path + ".vertices." + k, -1000000, 1000000);
                }
            }
        }
        foreach (var (key, maximum) in new[] { ("maxDepth", 12), ("maxNodes", 1000), ("maxEdges", 3000), ("timeoutSeconds", 30) })
            if (doc["limits"]?[key] is not JsonValue v || !v.TryGetValue<int>(out var n) || n < 1 || n > maximum) Error("limits." + key, "预算超出保护范围。");
        if (doc["scope"]?["factoryIds"] is not JsonArray || doc["scope"]?["industryTags"] is not JsonArray) Error("scope", "须声明工厂与行业范围数组。");
    }
}
