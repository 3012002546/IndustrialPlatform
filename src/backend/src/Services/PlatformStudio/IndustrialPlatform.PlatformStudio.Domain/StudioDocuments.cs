using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>Studio发布文档白名单、台账字段约束和升级兼容性；不生成客户物理表。</summary>
public static partial class StudioDocuments
{
    public static readonly string[] ComponentTypes = ["Container", "Grid", "Text", "FormField", "Table", "Tree", "Tabs", "File", "Chart", "RegisteredPage", "RegisteredComponent"];
    private static readonly string[] LedgerTypes = ["String", "Text", "Decimal", "Int32", "Boolean", "Date", "DateTime", "Enum", "FileReference"];

    public static IReadOnlyList<StudioDiagnostic> Validate(string kind, JsonObject document)
    {
        try { return ValidateCore(kind, document); }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException)
        {
            throw new StudioException("STUDIO_VALIDATION", "文档字段类型或结构无效。", path: "document");
        }
    }

    private static List<StudioDiagnostic> ValidateCore(string kind, JsonObject document)
    {
        if (!StudioKinds.Modules.ContainsKey(kind)) throw new StudioException("STUDIO_VALIDATION", "未知资源类型。", path: "kind");
        if (document["schemaVersion"]?.GetValue<int>() != 1 || Encoding.UTF8.GetByteCount(document.ToJsonString()) > 2048 * 1024)
            throw new StudioException("STUDIO_VALIDATION", "配置结构版本必须为1且内容不超过2MiB。", path: "document");
        var errors = new List<StudioDiagnostic>();
        if (kind == "LedgerModel") ValidateModel(document, errors);
        if (kind == "LowCodePage") ValidatePage(document, errors);
        if (kind == "Dataset") ValidateDataset(document, errors);
        if (kind == "Dashboard") ValidateDashboard(document, errors);
        if (kind == "Playlist") ValidatePlaylist(document, errors);
        if (kind is "Report" or "ReportBundle") ValidateReport(kind, document, errors);
        return errors;
    }

    public static JsonObject ValidateValues(JsonObject model, JsonObject input, bool applyDefaults)
    {
        var output = new JsonObject();
        var fields = (model["fields"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(field => StudioJson.RequiredString(field, "id", 64), StringComparer.Ordinal);
        foreach (var key in input.Select(pair => pair.Key))
            if (!fields.TryGetValue(key, out var field) || field["retired"]?.GetValue<bool>() == true)
                throw new StudioException("STUDIO_VALIDATION", "不能写入未声明或已停用字段。", path: "values." + key);
        foreach (var (id, field) in fields)
        {
            if (field["retired"]?.GetValue<bool>() == true) continue;
            var value = input[id]?.DeepClone();
            if (!input.ContainsKey(id) && applyDefaults) value = field["defaultValue"]?.DeepClone();
            ValidateValue(field, value, "values." + id);
            if (StudioJson.Text(field["type"]) == "DateTime" && value is not null) value = DateTimeOffset.Parse(StudioJson.Text(value)!, CultureInfo.InvariantCulture).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            output[id] = value;
        }
        return output;
    }

    public static IReadOnlyList<StudioDiagnostic> Compatibility(JsonObject oldModel, JsonObject newModel, long existingRecords)
    {
        if (existingRecords == 0) return [];
        var errors = new List<StudioDiagnostic>();
        var oldFields = (oldModel["fields"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(field => StudioJson.RequiredString(field, "id", 64), StringComparer.Ordinal);
        var newFields = (newModel["fields"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(field => StudioJson.RequiredString(field, "id", 64), StringComparer.Ordinal);
        foreach (var (id, oldField) in oldFields)
        {
            if (!newFields.TryGetValue(id, out var field)) { errors.Add(new("MODEL_SCHEMA_INCOMPATIBLE", "fields." + id, "已有记录字段必须保留并标为retired。")); continue; }
            foreach (var property in new[] { "type", "scale", "unique", "caseInsensitive", "maxLength", "enumValues", "nullable" })
                if (!JsonNode.DeepEquals(oldField[property], field[property])) errors.Add(new("MODEL_SCHEMA_INCOMPATIBLE", "fields." + id + "." + property, "已有记录不允许隐式改变字段约束或精度。"));
            if (oldField["required"]?.GetValue<bool>() != true && field["required"]?.GetValue<bool>() == true)
                errors.Add(new("MODEL_SCHEMA_INCOMPATIBLE", "fields." + id, "已有记录不能直接增加必填约束。"));
        }
        foreach (var (id, field) in newFields)
            if (!oldFields.ContainsKey(id) && (field["required"]?.GetValue<bool>() == true || field["nullable"]?.GetValue<bool>() == false || field["unique"]?.GetValue<bool>() == true))
                errors.Add(new("MODEL_SCHEMA_INCOMPATIBLE", "fields." + id, "新增字段必须可空，不能自动回填已有业务记录。"));
        return errors;
    }

    public static void ValidateValue(JsonObject field, JsonNode? value, string path)
    {
        var type = StudioJson.RequiredString(field, "type", 32);
        if (value is null)
        {
            if (field["required"]?.GetValue<bool>() == true || field["nullable"]?.GetValue<bool>() == false) throw Invalid(path, "必填字段不能为空。");
            return;
        }
        var text = StudioJson.Text(value);
        switch (type)
        {
            case "String": case "Text": case "Enum":
                if (text is null || field["required"]?.GetValue<bool>() == true && string.IsNullOrWhiteSpace(text)) throw Invalid(path, "字段需要文本值。");
                var max = field["maxLength"]?.GetValue<int>() ?? (type == "Text" ? 65536 : 512);
                if (text.Length > max) throw Invalid(path, "文本超过最大长度。");
                if (type == "Enum" && (field["enumValues"] is not JsonArray values || !values.Any(item => JsonNode.DeepEquals(item, value)))) throw Invalid(path, "值不在允许的枚举中。");
                break;
            case "Decimal":
                var scaled = StudioJson.ScaledDecimal(text, path);
                var scale = field["scale"]?.GetValue<int>() ?? 6;
                if (scale is < 0 or > 6 || scaled % (long)Math.Pow(10, 6 - scale) != 0) throw Invalid(path, "小数精度超过模型约束。");
                break;
            case "Int32":
                if (value is not JsonValue intValue || !intValue.TryGetValue<int>(out _)) throw Invalid(path, "字段需要32位整数。");
                break;
            case "Boolean":
                if (value is not JsonValue boolValue || !boolValue.TryGetValue<bool>(out _)) throw Invalid(path, "字段需要布尔值。");
                break;
            case "Date":
                if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw Invalid(path, "业务日期必须为yyyy-MM-dd。");
                break;
            case "DateTime":
                if (text is null || !Regex.IsMatch(text, @"(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)) throw Invalid(path, "时间必须包含UTC或明确时区。");
                _ = time;
                break;
            case "FileReference":
                if (value is not JsonObject file || string.IsNullOrWhiteSpace(StudioJson.Text(file["fileNId"])) || string.IsNullOrWhiteSpace(StudioJson.Text(file["referenceNId"])) || file.Any(pair => pair.Key is not ("fileNId" or "referenceNId" or "name"))) throw Invalid(path, "附件必须使用文件及引用标识，不能存物理路径或URL。");
                break;
            default: throw Invalid(path, "不支持的字段类型。");
        }
    }

    private static void ValidateModel(JsonObject model, List<StudioDiagnostic> errors)
    {
        Allowed(model, ["schemaVersion", "fields", "numbering", "displayFieldId"], "", errors);
        if (model["fields"] is not JsonArray fields || fields.Count > 200) { errors.Add(new("STUDIO_VALIDATION", "fields", "字段数组必填且最多200项。")); return; }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in Objects(fields, "fields", errors))
        {
            var id = StudioJson.RequiredString(field, "id", 64);
            var name = StudioJson.RequiredString(field, "name", 128);
            if (!ids.Add(id) || !names.Add(name) || name is "__proto__" or "constructor" or "prototype") errors.Add(new("STUDIO_VALIDATION", "fields." + id, "字段ID或名称重复/非法。"));
            if (!LedgerTypes.Contains(StudioJson.Text(field["type"]), StringComparer.Ordinal)) errors.Add(new("TYPE_MISMATCH", "fields." + id + ".type", "未登记的字段类型。"));
            if (field["maxLength"] is { } maxLength && (maxLength.GetValue<int>() < 1 || maxLength.GetValue<int>() > (StudioJson.Text(field["type"]) == "Text" ? 65536 : 512))) errors.Add(new("STUDIO_VALIDATION", "fields." + id + ".maxLength", "字段长度超出允许范围。"));
            if (field["scale"] is { } scale && scale.GetValue<int>() is < 0 or > 6) errors.Add(new("STUDIO_VALIDATION", "fields." + id + ".scale", "小数精度须在0至6之间。"));
            Allowed(field, ["id", "name", "label", "type", "required", "nullable", "defaultValue", "maxLength", "scale", "enumValues", "queryable", "sortable", "unique", "caseInsensitive", "retired"], "fields." + id, errors);
            if (field["type"]?.GetValue<string>() == "Text" && (field["queryable"]?.GetValue<bool>() == true || field["sortable"]?.GetValue<bool>() == true || field["unique"]?.GetValue<bool>() == true)) errors.Add(new("STUDIO_VALIDATION", "fields." + id, "长文本不能建立类型索引。"));
            if (field.ContainsKey("defaultValue"))
                try { ValidateValue(field, field["defaultValue"], "fields." + id + ".defaultValue"); } catch (StudioException exception) { errors.Add(new(exception.Code, exception.Path ?? "fields", exception.Message)); }
        }
        var mode = StudioJson.Text(model["numbering"]?["mode"]);
        if (mode is not ("Manual" or "Rule")) errors.Add(new("STUDIO_VALIDATION", "numbering.mode", "编号模式须显式选择。"));
        if (StudioJson.Text(model["displayFieldId"]) is not { } display || !ids.Contains(display)) errors.Add(new("PATH_INVALID", "displayFieldId", "显示字段必须引用已声明字段。"));
    }

    private static void ValidatePage(JsonObject page, List<StudioDiagnostic> errors)
    {
        Allowed(page, ["schemaVersion", "mode", "parameters", "instances", "layout", "fields", "actions", "codeComponentKey", "ledgerModelRevisionNId"], "", errors);
        if (StudioJson.Text(page["mode"]) is not ("Crud" or "Compose" or "Code")) errors.Add(new("STUDIO_VALIDATION", "mode", "页面模式未登记。"));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Layout(JsonObject node, int depth)
        {
            if (depth > 32 || ids.Count > 500) throw Invalid("layout", "页面布局超过保护预算。");
            var id = StudioJson.RequiredString(node, "id", 64);
            if (!ids.Add(id)) errors.Add(new("STUDIO_VALIDATION", "layout." + id, "组件ID重复。"));
            var type = StudioJson.Text(node["type"]);
            if (!ComponentTypes.Contains(type, StringComparer.Ordinal)) errors.Add(new("STUDIO_VALIDATION", "layout." + id + ".type", "组件不在代码白名单。"));
            Allowed(node, ["id", "type", "registryVersion", "props", "children", "grid"], "layout." + id, errors);
            if (StudioJson.Text(node["registryVersion"]) != "1") errors.Add(new("STUDIO_VALIDATION", "layout." + id + ".registryVersion", "组件版本未登记。"));
            var props = node["props"] as JsonObject ?? new JsonObject();
            var allowed = type switch
            {
                "Container" or "Grid" => new[] { "columns", "gapPx" }, "Text" => ["text", "styleId"],
                "FormField" => ["fieldId", "control", "placeholder", "optionsDatasetInstanceId"],
                "Table" => ["instanceId", "rowKeyPath", "collectionPath", "columns", "queryMode", "selection"],
                "Tree" => ["instanceId", "keyPath", "labelPath", "parentPath"], "Tabs" => ["items"], "File" => ["multiple", "maxCount"],
                "RegisteredPage" or "RegisteredComponent" => ["registryKey", "registryVersion", "parameters"],
                "Chart" => ["chartType", "datasetInstanceId", "xField", "series", "options"], _ => Array.Empty<string>(),
            };
            Allowed(props, allowed, "layout." + id + ".props", errors);
            if(type=="Table"&&props.ContainsKey("collectionPath")&&(props["collectionPath"] is not JsonArray path||path.Count>32||path.Any(segment=>StudioJson.Text(segment) is not {Length:>0 and <=128} text||text is "__proto__" or "constructor" or "prototype")))
                errors.Add(new("PATH_INVALID","layout."+id+".props.collectionPath","集合路径必须为受限字段路径数组。"));
            if (type is "RegisteredPage" or "RegisteredComponent" && (StudioJson.Text(props["registryKey"]) != "studio.summary" || StudioJson.Text(props["registryVersion"]) != "1")) errors.Add(new("STUDIO_VALIDATION", "layout." + id + ".props", "扩展组件或版本未登记。"));
            foreach (var child in Objects(node["children"], "layout." + id + ".children", errors)) Layout(child, depth + 1);
        }
        if (page["layout"] is JsonObject layout) Layout(layout, 0); else errors.Add(new("STUDIO_VALIDATION", "layout", "页面必须有布局根节点。"));
        var instanceIds = Objects(page["instances"], "instances", errors).Select(instance => StudioJson.RequiredString(instance, "instanceId", 64)).ToArray();
        if (instanceIds.Distinct(StringComparer.Ordinal).Count() != instanceIds.Length) errors.Add(new("STUDIO_VALIDATION", "instances", "数据实例标识重复。"));
        foreach (var field in Objects(page["fields"], "fields", errors))
        {
            if (!ids.Contains(StudioJson.RequiredString(field, "componentId", 64))) errors.Add(new("PATH_INVALID", "fields.componentId", "字段绑定对应组件不存在。"));
            ValidateBinding(field["source"], "fields.source", instanceIds, errors);
        }
        var actionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in Objects(page["actions"], "actions", errors))
        {
            if (!actionIds.Add(StudioJson.RequiredString(action, "id", 64))) errors.Add(new("STUDIO_VALIDATION", "actions.id", "动作标识重复。"));
            if (StudioJson.Text(action["event"]) is not ("Click" or "RowClick" or "Submit")) errors.Add(new("STUDIO_VALIDATION", "actions.event", "动作事件未登记。"));
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var map in Objects(action["inputMap"], "actions.inputMap", errors))
            {
                if (!targets.Add(StudioJson.RequiredString(map, "target"))) errors.Add(new("STUDIO_VALIDATION", "actions.inputMap.target", "输入目标重复。"));
                ValidateBinding(map["source"], "actions.inputMap.source", instanceIds, errors);
            }
        }
    }

    private static void ValidateDataset(JsonObject document, List<StudioDiagnostic> errors)
    {
        Allowed(document, ["schemaVersion", "sourceNId", "sourceConfigurationVersion", "query", "parameters", "result", "rowPolicyKey", "readableFieldIds", "timeoutSeconds", "maxRows", "maxBytes", "cacheSeconds", "freshness", "pagination"], "", errors);
        if (document["query"] is not JsonObject query) errors.Add(new("STUDIO_VALIDATION", "query", "必须登记查询合同。"));
        else
        {
            var kind = StudioJson.Text(query["kind"]) ?? StudioJson.Text(query["type"]);
            Allowed(query, kind switch { "Api" => ["kind", "type", "operationKey", "method", "parameterMap"], "Sql" => ["kind", "type", "statement", "parameters"], "Procedure" => ["kind", "type", "qualifiedName", "parameters", "resultSets"], _ => [] }, "query", errors);
            if (kind is not ("Api" or "Sql" or "Procedure")) errors.Add(new("STUDIO_VALIDATION", "query.kind", "查询类型未登记。"));
        }
        if (document["readableFieldIds"] is not JsonArray { Count: > 0 }) errors.Add(new("STUDIO_FORBIDDEN", "readableFieldIds", "必须显式登记可读字段。"));
        if (string.IsNullOrWhiteSpace(StudioJson.Text(document["rowPolicyKey"]))) errors.Add(new("STUDIO_FORBIDDEN", "rowPolicyKey", "必须显式登记行权限策略。"));
        if (document["result"] is not JsonObject result) errors.Add(new("STUDIO_VALIDATION", "result", "必须配置完整结果Schema。"));
        else
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            ValidateFieldSchema(result, "result", ids, errors);
            if (StudioJson.Text(result["type"]) is not ("Object" or "Array")) errors.Add(new("TYPE_MISMATCH", "result.type", "结果根必须为Object或Array。"));
            foreach (var readable in document["readableFieldIds"] as JsonArray ?? [])
                if (StudioJson.Text(readable) is not { } fieldId || !ids.Contains(fieldId)) errors.Add(new("PATH_INVALID", "readableFieldIds", "可读字段必须引用已声明字段ID。"));
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in Objects(document["parameters"], "parameters", errors))
        {
            var name = StudioJson.RequiredString(parameter, "name");
            if (!names.Add(name)) errors.Add(new("STUDIO_VALIDATION", "parameters." + name, "参数名重复。"));
            if (StudioJson.Text(parameter["type"]) is not ("String" or "Int32" or "Int64" or "Decimal" or "Boolean" or "Date" or "DateTime" or "Guid" or "Array")) errors.Add(new("TYPE_MISMATCH", "parameters." + name + ".type", "参数类型未登记。"));
            if (StudioJson.Text(parameter["source"]) is not (null or "Caller" or "TrustedContext")) errors.Add(new("STUDIO_VALIDATION", "parameters." + name + ".source", "参数来源未登记。"));
        }
    }

    private static void ValidateDashboard(JsonObject document, List<StudioDiagnostic> errors)
    {
        Allowed(document, ["schemaVersion", "canvas", "parameters", "instances", "components", "navigation", "theme", "samples"], "", errors);
        if (document["canvas"] is not JsonObject canvas)
            errors.Add(new("STUDIO_VALIDATION", "canvas", "看板必须声明画布尺寸。"));
        else
        {
            Allowed(canvas, ["width", "height", "background", "fit"], "canvas", errors);
            var width = canvas["width"]?.GetValue<int>() ?? 0;
            var height = canvas["height"]?.GetValue<int>() ?? 0;
            if (width is < 320 or > 16384) errors.Add(new("STUDIO_VALIDATION", "canvas.width", "画布宽度必须在320至16384之间。"));
            if (height is < 320 or > 16384) errors.Add(new("STUDIO_VALIDATION", "canvas.height", "画布高度必须在320至16384之间。"));
            if (StudioJson.Text(canvas["fit"]) is not ("Contain" or "Cover" or "Actual")) errors.Add(new("STUDIO_VALIDATION", "canvas.fit", "适配模式未登记。"));
        }

        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in Objects(document["parameters"], "parameters", errors))
        {
            var name = StudioJson.RequiredString(parameter, "name", 128);
            if (!parameterNames.Add(name)) errors.Add(new("STUDIO_VALIDATION", "parameters." + name, "参数名重复。"));
            Allowed(parameter, ["name", "label", "type", "required", "nullable", "defaultValue", "enumValues", "maxLength", "min", "max", "source", "contextKey", "itemType"], "parameters." + name, errors);
            if (StudioJson.Text(parameter["type"]) is not ("String" or "Int32" or "Int64" or "Decimal" or "Boolean" or "Date" or "DateTime" or "Guid" or "Array"))
                errors.Add(new("TYPE_MISMATCH", "parameters." + name + ".type", "参数类型未登记。"));
            if (StudioJson.Text(parameter["type"]) == "Array" && StudioJson.Text(parameter["itemType"]) is not ("String" or "Int32" or "Int64" or "Decimal" or "Boolean" or "Date" or "DateTime" or "Guid"))
                errors.Add(new("TYPE_MISMATCH", "parameters." + name + ".itemType", "数组参数必须声明已登记的itemType。"));
            if (StudioJson.Text(parameter["source"]) is not (null or "Caller" or "TrustedContext"))
                errors.Add(new("STUDIO_VALIDATION", "parameters." + name + ".source", "参数来源未登记。"));
            if (StudioJson.Text(parameter["source"]) == "TrustedContext" && StudioJson.Text(parameter["contextKey"]) is not ("tenant" or "user" or "authorizedFactoryIds"))
                errors.Add(new("STUDIO_VALIDATION", "parameters." + name + ".contextKey", "可信上下文键未登记。"));
        }

        var instanceIds = Objects(document["instances"], "instances", errors).Select(instance => StudioJson.RequiredString(instance, "instanceId", 64)).ToHashSet(StringComparer.Ordinal);
        if (instanceIds.Count != (document["instances"] as JsonArray)?.Count) errors.Add(new("STUDIO_VALIDATION", "instances", "数据实例标识重复。"));
        if (instanceIds.Count > 64) errors.Add(new("STUDIO_LIMIT", "instances", "页面数据实例最多64项。"));
        var dependencyReferences = new List<(string Id, JsonArray Dependencies)>();
        foreach (var instance in Objects(document["instances"], "instances", errors))
        {
            var id = StudioJson.RequiredString(instance, "instanceId", 64);
            Allowed(instance, ["instanceId", "sourceType", "value", "scopeComponentId", "datasetNId", "revisionNId", "parameters", "refreshSeconds", "staleAfterSeconds", "dependsOn", "label"], "instances." + id, errors);
            var sourceType = StudioJson.Text(instance["sourceType"]) ?? "Dataset";
            if (sourceType is not ("Dataset" or "Api" or "Constant")) errors.Add(new("STUDIO_VALIDATION", "instances." + id + ".sourceType", "数据来源类型未登记。"));
            if (sourceType == "Constant")
            {
                if (!instance.ContainsKey("value")) errors.Add(new("PARAM_REQUIRED", "instances." + id + ".value", "常量数据实例须声明value。"));
                if ((instance["refreshSeconds"]?.GetValue<int>() ?? 0) != 0) errors.Add(new("STUDIO_VALIDATION", "instances." + id + ".refreshSeconds", "常量数据不需要定时读取。"));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(StudioJson.Text(instance["datasetNId"]))) errors.Add(new("PARAM_REQUIRED", "instances." + id + ".datasetNId", "动态数据实例必须指定Dataset。"));
                if (string.IsNullOrWhiteSpace(StudioJson.Text(instance["revisionNId"]))) errors.Add(new("PARAM_REQUIRED", "instances." + id + ".revisionNId", "数据实例必须固定Dataset修订。"));
            }
            foreach (var mapping in Objects(instance["parameters"], "instances." + id + ".parameters", errors))
            {
                StudioJson.RequiredString(mapping, "target", 128);
                ValidateBinding(mapping["source"], "instances." + id + ".parameters.source", [.. instanceIds], errors);
            }
            if (instance["refreshSeconds"] is { } refresh && (refresh.GetValue<int>() is < 0 or > 86400)) errors.Add(new("STUDIO_VALIDATION", "instances." + id + ".refreshSeconds", "刷新周期超出范围。"));
            if (instance["staleAfterSeconds"] is { } stale && (stale.GetValue<int>() is < 1 or > 86400)) errors.Add(new("STUDIO_VALIDATION", "instances." + id + ".staleAfterSeconds", "过期阈值超出范围。"));
            if (instance["dependsOn"] is JsonArray dependsOn) dependencyReferences.Add((id, dependsOn));
        }
        foreach (var (id, dependencies) in dependencyReferences)
            if (dependencies.Any(item => !instanceIds.Contains(StudioJson.Text(item), StringComparer.Ordinal)))
                errors.Add(new("PATH_INVALID", "instances." + id + ".dependsOn", "依赖实例不存在。"));
        var instanceDefinitions = (document["instances"] as JsonArray ?? []).OfType<JsonObject>().GroupBy(item => StudioJson.RequiredString(item, "instanceId")).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (visited.Contains(id) || !instanceDefinitions.TryGetValue(id, out var definition)) return true;
            if (!visiting.Add(id)) return false;
            var dependencies = (definition["dependsOn"] as JsonArray ?? []).Select(StudioJson.Text)
                .Concat((definition["parameters"] as JsonArray ?? []).OfType<JsonObject>().Where(map => StudioJson.Text(map["source"]?["from"]) == "Instance").Select(map => StudioJson.Text(map["source"]?["instanceId"])));
            foreach (var dependency in dependencies.Where(value => value is not null)) if (!Visit(dependency!)) return false;
            visiting.Remove(id); visited.Add(id); return true;
        }
        if (instanceDefinitions.Keys.Any(id => !Visit(id))) errors.Add(new("STUDIO_VALIDATION", "instances.dependsOn", "数据实例存在循环依赖。"));

        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in Objects(document["components"], "components", errors))
        {
            var id = StudioJson.RequiredString(component, "id", 64);
            if (!componentIds.Add(id)) errors.Add(new("STUDIO_VALIDATION", "components." + id, "组件标识重复。"));
            var type = StudioJson.Text(component["type"]);
            var componentProps = component["props"] as JsonObject ?? new JsonObject();
            var registryKey = type == "Chart"
                ? "Chart." + StudioJson.Text(componentProps["kind"])
                : type == "Table"
                    ? (StudioJson.Text(componentProps["presentation"]) == "List" ? "List.Scroll" : "Table.Scroll")
                    : type;
            if (!DashboardComponentRegistry.TryGet(registryKey, out var registration))
            {
                errors.Add(new("STUDIO_VALIDATION", "components." + id + ".type", "组件不在Dashboard注册表。"));
                continue;
            }
            if (StudioJson.Text(component["registryVersion"]) != registration.Version) errors.Add(new("STUDIO_VALIDATION", "components." + id + ".registryVersion", "组件注册版本未登记。"));
            Allowed(component, ["id", "type", "registryVersion", "name", "x", "y", "width", "height", "rotation", "zIndex", "locked", "visible", "props", "bindings", "events", "children"], "components." + id, errors);
            foreach (var key in new[] { "x", "y", "width", "height", "rotation", "zIndex" })
            {
                if (!TryNumber(component[key], out _)) errors.Add(new("TYPE_MISMATCH", "components." + id + "." + key, "几何属性必须为有限数字。"));
            }
            if (!TryNumber(component["width"], out var width) || !TryNumber(component["height"], out var height) || width <= 0 || height <= 0) errors.Add(new("STUDIO_VALIDATION", "components." + id, "组件宽高必须为正数。"));
            var props = componentProps;
            var propNames = registration.PropsSchema["properties"]?.AsObject().Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in props)
            {
                if (!propNames.Contains(prop.Key) && prop.Key is not "children")
                {
                    errors.Add(new("STUDIO_VALIDATION", "components." + id + ".props." + prop.Key, "组件属性未在注册表声明。"));
                    continue;
                }
                if (registration.PropsSchema["properties"]?.AsObject()[prop.Key] is JsonObject propertySchema)
                    ValidateRegistryProperty(prop.Value, propertySchema, "components." + id + ".props." + prop.Key, errors);
            }
            if (registration.Key is "Table.Scroll" or "List.Scroll" && props["columns"] is JsonArray tableColumns)
            {
                var totalWidth = 0d;
                foreach (var column in tableColumns.OfType<JsonObject>())
                    if (column["widthPercent"] is { } widthNode)
                    {
                        if (!TryNumber(widthNode, out var percent) || percent <= 0 || percent > 100)
                            errors.Add(new("STUDIO_VALIDATION", "components." + id + ".props.columns.widthPercent", "列宽百分比必须大于0且不超过100。"));
                        else totalWidth += percent;
                    }
                if (totalWidth > 100.000001) errors.Add(new("STUDIO_VALIDATION", "components." + id + ".props.columns.widthPercent", "已设置的列宽百分比合计不能超过100。"));
            }
            foreach (var binding in Objects(component["bindings"], "components." + id + ".bindings", errors))
            {
                var property = StudioJson.RequiredString(binding, "property", 128);
                if (!registration.BindableProperties.Contains(property, StringComparer.Ordinal)) errors.Add(new("PATH_INVALID", "components." + id + ".bindings.property", "该属性不可绑定。"));
                ValidateBinding(binding["source"], "components." + id + ".bindings.source", [.. instanceIds], errors);
            }
            foreach (var eventId in (component["events"] as JsonArray ?? []).Select(StudioJson.Text))
                if (eventId is not { Length: > 0 and <= 64 }) errors.Add(new("STUDIO_VALIDATION", "components." + id + ".events", "组件事件标识无效。"));
        }

        var navigationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in Objects(document["navigation"], "navigation", errors))
        {
            var id = StudioJson.RequiredString(rule, "id", 64);
            if (!navigationIds.Add(id)) errors.Add(new("STUDIO_VALIDATION", "navigation." + id, "导航规则标识重复。"));
            Allowed(rule, ["id", "componentId", "event", "target", "parameterMap", "enabledWhen", "openMode"], "navigation." + id, errors);
            var componentId = StudioJson.Text(rule["componentId"]);
            if (componentId is null || !componentIds.Contains(componentId)) errors.Add(new("PATH_INVALID", "navigation." + id + ".componentId", "导航规则组件不存在。"));
            if (StudioJson.Text(rule["event"]) is not ("Click" or "RowClick")) errors.Add(new("STUDIO_VALIDATION", "navigation." + id + ".event", "导航事件未登记。"));
            if (rule["target"] is not JsonObject target) errors.Add(new("PARAM_REQUIRED", "navigation." + id + ".target", "导航必须声明目标。"));
            else
            {
                Allowed(target, ["kind", "resourceNId", "revisionNId", "routeKey"], "navigation." + id + ".target", errors);
                if (StudioJson.Text(target["kind"]) is not ("Dashboard" or "LowCodePage" or "Report" or "BusinessPage")) errors.Add(new("TYPE_MISMATCH", "navigation." + id + ".target.kind", "导航目标类型未登记。"));
                if (string.IsNullOrWhiteSpace(StudioJson.Text(target["resourceNId"]))) errors.Add(new("PARAM_REQUIRED", "navigation." + id + ".target.resourceNId", "导航目标资源不能为空。"));
            }
            if (StudioJson.Text(rule["openMode"]) is not (null or "Current" or "Modal")) errors.Add(new("STUDIO_VALIDATION", "navigation." + id + ".openMode", "打开方式未登记。"));
            var mapTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var map in Objects(rule["parameterMap"], "navigation." + id + ".parameterMap", errors))
            {
                var mapTarget = StudioJson.RequiredString(map, "target", 128);
                if (!mapTargets.Add(mapTarget)) errors.Add(new("STUDIO_VALIDATION", "navigation." + id + ".parameterMap.target", "目标参数不能重复。"));
                ValidateBinding(map["source"], "navigation." + id + ".parameterMap.source", [.. instanceIds], errors);
            }
            if (rule["enabledWhen"] is { } condition) ValidateCondition(condition, "navigation." + id + ".enabledWhen", [.. instanceIds], errors);
        }
    }

    private static void ValidatePlaylist(JsonObject document, List<StudioDiagnostic> errors)
    {
        Allowed(document, ["schemaVersion", "parameters", "items", "loop", "onFailure", "preloadCount"], "", errors);
        foreach (var parameter in Objects(document["parameters"], "parameters", errors))
            if (StudioJson.Text(parameter["name"]) is null) errors.Add(new("PARAM_REQUIRED", "parameters.name", "轮播参数必须有名称。"));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Objects(document["items"], "items", errors))
        {
            var id = StudioJson.RequiredString(item, "id", 64);
            if (!ids.Add(id)) errors.Add(new("STUDIO_VALIDATION", "items." + id, "轮播项标识重复。"));
            Allowed(item, ["id", "dashboardNId", "revisionNId", "parameterMap", "staySeconds", "enabled", "label"], "items." + id, errors);
            if (string.IsNullOrWhiteSpace(StudioJson.Text(item["dashboardNId"]))) errors.Add(new("PARAM_REQUIRED", "items." + id + ".dashboardNId", "轮播项必须指定看板。"));
            if (item["staySeconds"] is not JsonValue seconds || !seconds.TryGetValue<int>(out var stay) || stay is < 3 or > 3600) errors.Add(new("STUDIO_VALIDATION", "items." + id + ".staySeconds", "停留时长必须为3至3600秒。"));
            foreach (var map in Objects(item["parameterMap"], "items." + id + ".parameterMap", errors))
            {
                StudioJson.RequiredString(map, "target", 128);
                ValidateBinding(map["source"], "items." + id + ".parameterMap.source", [], errors);
            }
        }
        if (StudioJson.Text(document["onFailure"]) is not ("Skip" or "Pause")) errors.Add(new("STUDIO_VALIDATION", "onFailure", "失败策略未登记。"));
        if ((document["preloadCount"]?.GetValue<int>() ?? 1) is not 0 and not 1) errors.Add(new("STUDIO_VALIDATION", "preloadCount", "预加载最多只能为1项。"));
    }

    private static bool TryNumber(JsonNode? value, out double result)
    {
        result = 0;
        if (value is not JsonValue jsonValue) return false;
        if (jsonValue.TryGetValue<double>(out result)) return double.IsFinite(result);
        if (jsonValue.TryGetValue<int>(out var integer)) { result = integer; return true; }
        if (jsonValue.TryGetValue<long>(out var longValue)) { result = longValue; return true; }
        if (jsonValue.TryGetValue<decimal>(out var decimalValue)) { result = (double)decimalValue; return double.IsFinite(result); }
        return false;
    }

    private static void ValidateRegistryProperty(JsonNode? value, JsonObject schema, string path, List<StudioDiagnostic> errors)
    {
        var type = StudioJson.Text(schema["type"]);
        var valid = type switch
        {
            "string" => value is JsonValue stringValue && stringValue.TryGetValue<string>(out _),
            "number" => TryNumber(value, out _),
            "boolean" => value is JsonValue boolValue && boolValue.TryGetValue<bool>(out _),
            "array" => value is JsonArray,
            "object" => value is JsonObject,
            _ => true,
        };
        if (!valid)
        {
            errors.Add(new("TYPE_MISMATCH", path, "组件属性类型与注册Schema不一致。"));
            return;
        }
        if (schema["enum"] is JsonArray options && !options.Any(option => JsonNode.DeepEquals(option, value)))
            errors.Add(new("STUDIO_VALIDATION", path, "组件属性值不在注册枚举中。"));
        if (TryNumber(value, out var number))
        {
            if (schema["minimum"] is { } minimum && TryNumber(minimum, out var lower) && number < lower) errors.Add(new("STUDIO_VALIDATION", path, "组件属性低于注册范围。"));
            if (schema["maximum"] is { } maximum && TryNumber(maximum, out var upper) && number > upper) errors.Add(new("STUDIO_VALIDATION", path, "组件属性高于注册范围。"));
        }
        if (value is JsonObject objectValue && schema["properties"] is JsonObject properties)
            foreach (var child in objectValue)
                if (properties[child.Key] is JsonObject childSchema) ValidateRegistryProperty(child.Value, childSchema, path + "." + child.Key, errors);
                else errors.Add(new("STUDIO_VALIDATION", path + "." + child.Key, "组件对象属性未在注册Schema声明。"));
    }

    public static void ValidateFieldSchema(JsonObject field, string path, HashSet<string> ids, List<StudioDiagnostic> errors, int depth = 0)
    {
        if (depth > 32 || ids.Count >= 2000) throw new StudioException("STUDIO_LIMIT", "结果Schema超过保护预算。", 429, path);
        var id = StudioJson.RequiredString(field, "id", 64); var name = StudioJson.RequiredString(field, "name", 128);
        Allowed(field, ["id", "name", "label", "type", "nullable", "children", "item", "format", "sensitive", "maxLength"], path, errors);
        if (!ids.Add(id) || name is "__proto__" or "constructor" or "prototype") errors.Add(new("PATH_INVALID", path, "字段标识重复或名称非法。"));
        var type = StudioJson.Text(field["type"]);
        if (type == "Object")
        {
            if (field["children"] is not JsonArray) errors.Add(new("TYPE_MISMATCH", path + ".children", "Object须声明children数组。"));
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in Objects(field["children"], path + ".children", errors))
            {
                if (!names.Add(StudioJson.RequiredString(child, "name", 128))) errors.Add(new("PATH_INVALID", path + ".children", "同级字段名称重复。"));
                ValidateFieldSchema(child, path + ".children", ids, errors, depth + 1);
            }
            if (field.ContainsKey("item")) errors.Add(new("TYPE_MISMATCH", path + ".item", "Object不能声明item。"));
        }
        else if (type == "Array")
        {
            if (field["item"] is JsonObject item) ValidateFieldSchema(item, path + ".item", ids, errors, depth + 1);
            else errors.Add(new("TYPE_MISMATCH", path + ".item", "Array须声明item结构。"));
            if (field.ContainsKey("children")) errors.Add(new("TYPE_MISMATCH", path + ".children", "Array不能声明children。"));
        }
        else
        {
            if (type is not ("String" or "Int32" or "Int64" or "Decimal" or "Boolean" or "Date" or "DateTime" or "Guid")) errors.Add(new("TYPE_MISMATCH", path + ".type", "字段类型未登记。"));
            if (field.ContainsKey("item") || field.ContainsKey("children")) errors.Add(new("TYPE_MISMATCH", path, "标量不能声明子结构。"));
        }
    }

    private static JsonObject[] Objects(JsonNode? node, string path, List<StudioDiagnostic> errors)
    {
        if (node is null) return [];
        if (node is not JsonArray array) { errors.Add(new("TYPE_MISMATCH", path, "须为对象数组。")); return []; }
        if (array.Any(item => item is not JsonObject)) errors.Add(new("TYPE_MISMATCH", path, "数组每项须为对象。"));
        return array.OfType<JsonObject>().ToArray();
    }

    private static void ValidateBinding(JsonNode? value, string path, string[] instanceIds, List<StudioDiagnostic> errors)
    {
        if (value is not JsonObject binding) { errors.Add(new("TYPE_MISMATCH", path, "必须声明Binding。")); return; }
        var from = StudioJson.Text(binding["from"]);
        if (from is not ("PageParam" or "Row" or "ParentRow" or "Root" or "Instance" or "Form" or "Node" or "Constant" or "Context")) errors.Add(new("PATH_INVALID", path + ".from", "绑定上下文未登记。"));
        if (from == "Constant") { if (!binding.ContainsKey("value")) errors.Add(new("PARAM_REQUIRED", path + ".value", "常量须声明value。")); }
        else if (binding["path"] is not JsonArray segments || segments.Count > 32 || segments.Any(segment => StudioJson.Text(segment) is not { Length: > 0 and <= 128 } key || key is "__proto__" or "constructor" or "prototype")) errors.Add(new("PATH_INVALID", path + ".path", "绑定路径非法。"));
        if (from == "Instance" && !instanceIds.Contains(StudioJson.Text(binding["instanceId"]), StringComparer.Ordinal)) errors.Add(new("PATH_INVALID", path + ".instanceId", "绑定实例不存在。"));
        if (StudioJson.Text(binding["nullPolicy"]) is not ("Error" or "Null" or "Default")) errors.Add(new("STUDIO_VALIDATION", path + ".nullPolicy", "须声明空值策略。"));
    }

    private static void ValidateCondition(JsonNode? value, string path, string[] instanceIds, List<StudioDiagnostic> errors, int depth = 0)
    {
        if (depth > 32) { errors.Add(new("STUDIO_LIMIT", path, "条件嵌套超过保护预算。")); return; }
        if (value is not JsonObject condition) { errors.Add(new("TYPE_MISMATCH", path, "必须声明Condition。")); return; }
        var op = StudioJson.Text(condition["op"]);
        if (op is "And" or "Or")
        {
            var items = Objects(condition["items"], path + ".items", errors);
            if (items.Length == 0) errors.Add(new("TYPE_MISMATCH", path + ".items", "组合条件不能为空。"));
            foreach (var item in items) ValidateCondition(item, path + ".items", instanceIds, errors, depth + 1);
            return;
        }
        if (op is not ("Eq" or "Ne" or "In" or "Gt" or "Ge" or "Lt" or "Le" or "IsNull" or "HasRows")) errors.Add(new("TYPE_MISMATCH", path + ".op", "条件操作未登记。"));
        ValidateBinding(condition["left"], path + ".left", instanceIds, errors);
        if (op is not ("IsNull" or "HasRows")) ValidateBinding(condition["right"], path + ".right", instanceIds, errors);
    }

    private static void Allowed(JsonObject value, IReadOnlyList<string> allowed, string path, List<StudioDiagnostic> errors)
    {
        foreach (var property in value)
            if (!allowed.Contains(property.Key, StringComparer.Ordinal)) errors.Add(new("STUDIO_VALIDATION", path + "." + property.Key, "未登记的配置属性。"));
    }
    private static StudioException Invalid(string path, string message) => new("TYPE_MISMATCH", message, path: path);
}
