using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>Studio发布文档白名单、台账字段约束和升级兼容性；不生成客户物理表。</summary>
public static class StudioDocuments
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

    private static void Allowed(JsonObject value, IReadOnlyList<string> allowed, string path, List<StudioDiagnostic> errors)
    {
        foreach (var property in value)
            if (!allowed.Contains(property.Key, StringComparer.Ordinal)) errors.Add(new("STUDIO_VALIDATION", path + "." + property.Key, "未登记的配置属性。"));
    }
    private static StudioException Invalid(string path, string message) => new("TYPE_MISMATCH", message, path: path);
}
