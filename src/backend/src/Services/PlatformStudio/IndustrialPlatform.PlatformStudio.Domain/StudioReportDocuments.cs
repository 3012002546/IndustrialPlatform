using System.Text.Json.Nodes;
using System.Globalization;

namespace IndustrialPlatform.PlatformStudio.Domain;

public static partial class StudioDocuments
{
    private static readonly string[] ReportMarginKeys = ["top", "right", "bottom", "left"];
    public static (double Width, double Height, double BodyWidth, double BodyHeight) ReportPaper(JsonObject paper)
    {
        var size = StudioJson.Text(paper["size"]);
        var (width, height) = size switch { "A4" => (210d, 297d), "A3" => (297d, 420d), "Letter" => (215.9, 279.4), "Custom" => (Number(paper, "widthMm"), Number(paper, "heightMm")), _ => throw Invalid("paper.size", "纸张类型未登记。") };
        var orientation = StudioJson.Text(paper["orientation"]);
        if (orientation is not ("Portrait" or "Landscape")) throw Invalid("paper.orientation", "纸张方向未登记。");
        if (orientation == "Landscape") (width, height) = (height, width);
        var margin = paper["margin"] as JsonObject ?? new();
        var margins = ReportMarginKeys.Select(k => Number(margin, k, 15)).ToArray();
        var header = Number(paper, "headerHeightMm", 15); var footer = Number(paper, "footerHeightMm", 10);
        if (width is < 20 or > 2000 || height is < 20 or > 2000 || margins.Any(n => n < 0) || header < 0 || footer < 0 || margins[1] + margins[3] >= width || margins[0] + margins[2] + header + footer >= height) throw Invalid("paper", "纸张必须有正的正文区域。");
        return (width, height, width - margins[1] - margins[3], height - margins[0] - margins[2] - header - footer);
    }
    private static double Number(JsonObject obj, string key, double fallback = 0) => obj[key] is null ? fallback : obj[key] is JsonValue value && double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : throw Invalid(key, "需要有限数字。");

    private static void ValidateReport(string kind, JsonObject doc, List<StudioDiagnostic> errors)
    {
        Allowed(doc, kind == "Report" ? ["schemaVersion", "mode", "parameters", "instances", "query", "paper", "bands", "styles", "assets"] : ["schemaVersion", "mode", "parameters", "instances", "paper", "items", "assembly", "pageNumberMode"], "", errors);
        // Parameters and instances share the existing contracts, including dependency cycles.
        ValidateDashboard(new JsonObject { ["schemaVersion"] = 1, ["canvas"] = new JsonObject { ["width"] = 1200, ["height"] = 800, ["fit"] = "Contain" }, ["parameters"] = doc["parameters"]?.DeepClone(), ["instances"] = doc["instances"]?.DeepClone(), ["components"] = new JsonArray(), ["navigation"] = new JsonArray() }, errors);
        var instances = Objects(doc["instances"], "instances", errors).Select(x => StudioJson.RequiredString(x, "instanceId")).ToArray();
        void Error(string path, string message, string code = "STUDIO_VALIDATION") => errors.Add(new(code, path, message));
        double bodyWidth = 0;
        if (kind == "Report" || StudioJson.Text(doc["mode"]) == "ElectronicRecord")
        {
            if (doc["paper"] is not JsonObject paper) Error("paper", "必须设置纸张。");
            else
            {
                Allowed(paper, ["size", "orientation", "widthMm", "heightMm", "margin", "headerHeightMm", "footerHeightMm"], "paper", errors);
                if (paper["margin"] is JsonObject margin) Allowed(margin, ["top", "right", "bottom", "left"], "paper.margin", errors);
                try { bodyWidth = ReportPaper(paper).BodyWidth; } catch (StudioException e) { Error(e.Path ?? "paper", e.Message); }
            }
        }
        void Collection(JsonNode? node, string path, bool parent)
        {
            if (node is not JsonObject c) { Error(path, "必须选择集合。"); return; }
            Allowed(c, ["mode", "instanceId", "path", "datasetRevisionNId", "parameterMap", "filter"], path, errors);
            var mode = StudioJson.Text(c["mode"]);
            if (mode is not ("RootPath" or "ParentPath" or "Dataset")) Error(path + ".mode", "集合模式未登记。");
            if (mode == "RootPath" && !instances.Contains(StudioJson.Text(c["instanceId"]), StringComparer.Ordinal)) Error(path + ".instanceId", "实例不存在。");
            if (mode == "ParentPath" && !parent) Error(path, "没有父记录上下文。");
            if (mode != "Dataset" && c["path"] is not JsonArray) Error(path + ".path", "必须提供集合路径。");
            if (mode == "Dataset" && (string.IsNullOrWhiteSpace(StudioJson.Text(c["datasetRevisionNId"])) || c["parameterMap"] is not JsonArray)) Error(path, "关联数据集必须固定修订及参数映射。");
            if (mode == "Dataset" && c.ContainsKey("instanceId") || mode != "Dataset" && (c.ContainsKey("datasetRevisionNId") || c.ContainsKey("parameterMap")) || mode != "RootPath" && c.ContainsKey("instanceId")) Error(path, "集合模式包含无效字段。");
            if (c["filter"] is { } filter) ValidateCondition(filter, path + ".filter", instances, errors);
            foreach (var map in Objects(c["parameterMap"], path + ".parameterMap", errors)) ValidateBinding(map["source"], path + ".parameterMap.source", instances, errors);
        }
        void Mappings(JsonNode? node, string path)
        {
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var map in Objects(node, path, errors))
            {
                Allowed(map, ["target", "source", "type"], path, errors);
                if (!targets.Add(StudioJson.RequiredString(map, "target"))) Error(path, "映射目标重复。");
                ValidateBinding(map["source"], path + ".source", instances, errors);
            }
        }
        if (kind == "ReportBundle")
        {
            var mode = StudioJson.Text(doc["mode"]);
            if (mode == "Simple")
            {
                if (doc.ContainsKey("assembly") || doc.ContainsKey("paper")) Error("mode", "简单组合使用各子报告纸张。");
                if (StudioJson.Text(doc["pageNumberMode"]) is not ("Preserve" or "Global")) Error("pageNumberMode", "请选择页码模式。");
                var ids = new HashSet<string>();
                foreach (var item in Objects(doc["items"], "items", errors))
                {
                    Allowed(item, ["id", "reportNId", "revisionNId", "parameterMap", "enabled", "required", "startOnNewPage"], "items", errors);
                    if (!ids.Add(StudioJson.RequiredString(item, "id"))) Error("items.id", "项目编号重复。");
                    _ = StudioJson.RequiredString(item, "reportNId"); Mappings(item["parameterMap"], "items.parameterMap");
                    if (item["startOnNewPage"]?.GetValue<bool>() == false) Error("items.startOnNewPage", "简单组合的独立报告必须新页开始。");
                }
                if (ids.Count is 0 or > 200) Error("items", "组合项目需要1至200项。");
            }
            else if (mode == "ElectronicRecord")
            {
                if (doc.ContainsKey("items") || doc.ContainsKey("pageNumberMode")) Error("mode", "电子记录禁止预分页项目。");
                if (doc["assembly"] is not JsonObject assembly) { Error("assembly", "必须设置组装规则。"); return; }
                Allowed(assembly, ["root", "order", "operation", "rules"], "assembly", errors);
                Collection(assembly["root"], "assembly.root", false);
                if (assembly["operation"] is JsonObject operation) { Allowed(operation, ["collection", "order"], "assembly.operation", errors); Collection(operation["collection"], "assembly.operation.collection", true); } else Error("assembly.operation", "必须设置工序集合。");
                var ids = new HashSet<string>();
                foreach (var rule in Objects(assembly["rules"], "assembly.rules", errors))
                {
                    var path = "assembly.rules." + StudioJson.RequiredString(rule, "id");
                    if (!ids.Add(StudioJson.RequiredString(rule, "id"))) Error(path, "规则编号重复。");
                    Allowed(rule, ["id", "name", "priority", "when", "reportNId", "revisionNId", "parameterMap", "content", "repeat", "presence", "required", "emptyBehavior", "headerMode", "breakBefore"], path, errors);
                    _ = StudioJson.RequiredString(rule, "reportNId");
                    if (rule.ContainsKey("repeat") == rule.ContainsKey("presence")) Error(path, "repeat和presence必须且只能选择一个。");
                    if (rule.ContainsKey("repeat")) Collection(rule["repeat"], path + ".repeat", true);
                    if (rule.ContainsKey("presence")) Collection(rule["presence"], path + ".presence", true);
                    if (rule["when"] is { } condition) ValidateCondition(condition, path + ".when", instances, errors);
                    if (StudioJson.Text(rule["breakBefore"]) is not (null or "Auto" or "Page") || StudioJson.Text(rule["emptyBehavior"]) is not (null or "Skip") || StudioJson.Text(rule["headerMode"]) is not (null or "Inherit")) Error(path, "组装输出策略未登记。");
                    Mappings(rule["parameterMap"], path + ".parameterMap");
                    if (rule["content"] is not JsonObject content) { Error(path + ".content", "请选择输出起点。"); continue; }
                    Allowed(content, ["mode", "entryBandId", "rowMap", "ancestorMap"], path + ".content", errors);
                    if (StudioJson.Text(content["mode"]) == "WholeReport") { if (content.Any(p => p.Key != "mode")) Error(path + ".content", "完整报告不能设置子树映射。"); }
                    else if (StudioJson.Text(content["mode"]) == "BandSubtree")
                    {
                        _ = StudioJson.RequiredString(content, "entryBandId");
                        foreach (var map in Objects(content["rowMap"], path + ".rowMap", errors)) { Allowed(map, ["targetPath", "source"], path + ".rowMap", errors); if (map["targetPath"] is not JsonArray) Error(path + ".rowMap.targetPath", "必须选择目标字段路径。"); ValidateBinding(map["source"], path + ".rowMap.source", instances, errors); }
                        foreach (var map in Objects(content["ancestorMap"], path + ".ancestorMap", errors)) { Allowed(map, ["bandId", "source"], path + ".ancestorMap", errors); _ = StudioJson.RequiredString(map, "bandId"); ValidateBinding(map["source"], path + ".ancestorMap.source", instances, errors); }
                    }
                    else Error(path + ".content.mode", "输出模式未登记。");
                }
                if (ids.Count is 0 or > 200) Error("assembly.rules", "需要1至200条规则。");
            }
            else Error("mode", "组合模式未登记。");
            return;
        }
        if (StudioJson.Text(doc["mode"]) is not ("Fixed" or "Query")) Error("mode", "报告模式未登记。");
        var styles = new HashSet<string>();
        foreach (var style in Objects(doc["styles"], "styles", errors))
        {
            Allowed(style, ["id", "fontFamily", "fontSizePt", "fontWeight", "italic", "color", "backgroundColor", "textAlign", "verticalAlign", "lineHeight", "paddingMm", "border", "numberFormat", "dateFormat"], "styles", errors);
            if (!styles.Add(StudioJson.RequiredString(style, "id"))) Error("styles.id", "样式编号重复。");
            if (Number(style, "fontSizePt", 10) is < 4 or > 96 || Number(style, "lineHeight", 1.3) is < .5 or > 5) Error("styles", "字号或行高超出范围。");
        }
        var assetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in Objects(doc["assets"], "assets", errors)) { Allowed(asset, ["id", "fileNId", "referenceNId", "sha256", "mediaType"], "assets", errors); foreach (var k in new[] { "id", "fileNId", "referenceNId", "sha256", "mediaType" }) _ = StudioJson.RequiredString(asset, k); if (!assetIds.Add(StudioJson.RequiredString(asset, "id"))) Error("assets.id", "图片资产编号重复。"); if (StudioJson.Text(asset["mediaType"]) is not ("image/png" or "image/jpeg" or "image/gif" or "image/webp") || !System.Text.RegularExpressions.Regex.IsMatch(StudioJson.RequiredString(asset, "sha256"), "^[0-9a-f]{64}$")) Error("assets", "图片类型或哈希不合法。"); }
        var bands = Objects(doc["bands"], "bands", errors);
        var index = bands.GroupBy(b => StudioJson.RequiredString(b, "id")).ToDictionary(g => g.Key, g => g.First());
        if (bands.Length is 0 or > 200 || index.Count != bands.Length) Error("bands", "需要1至200个唯一的带。");
        foreach (var band in bands)
        {
            var id = StudioJson.RequiredString(band, "id"); var path = "bands." + id; var bandKind = StudioJson.Text(band["kind"]);
            Allowed(band, ["id", "kind", "name", "parentBandId", "data", "heightMm", "grow", "repeatOnPage", "keepWithNext", "controls", "sort", "group", "emptyBehavior", "emptyText"], path, errors);
            if (bandKind is not ("PageHeader" or "PageFooter" or "Detail" or "GroupHeader" or "GroupFooter" or "ReportHeader" or "ReportFooter")) Error(path + ".kind", "带类型未登记。");
            var ancestors = new HashSet<string>(); var current = band;
            while (StudioJson.Text(current["parentBandId"]) is { Length: > 0 } parent)
            {
                if (!ancestors.Add(parent) || parent == id) { Error(path + ".parentBandId", "带层级循环。"); break; }
                if (!index.TryGetValue(parent, out current)) { Error(path + ".parentBandId", "父带不存在。"); break; }
            }
            if (ancestors.Count >= 32) Error(path, "带层级超出保护深度。", "STUDIO_LIMIT");
            if (Number(band, "heightMm") is <= 0 or > 2000) Error(path + ".heightMm", "带最小高度无效。");
            if (bandKind == "Detail") Collection(band["data"], path + ".data", ancestors.Count > 0);
            if (bandKind is "PageHeader" or "PageFooter" && (band.ContainsKey("data") || ancestors.Count > 0)) Error(path, "物理页头页脚不能持有明细行。");
            if (bandKind is "GroupHeader" or "GroupFooter" && band["group"] is not JsonObject) Error(path + ".group", "分组配置缺失。");
            if (band["group"] is JsonObject group)
            {
                Allowed(group, ["keys", "pairedBandId"], path + ".group", errors);
                if (!index.TryGetValue(StudioJson.Text(group["pairedBandId"]) ?? "", out var paired) || StudioJson.Text(paired["kind"]) != (bandKind == "GroupHeader" ? "GroupFooter" : "GroupHeader") || StudioJson.Text(paired["parentBandId"]) != StudioJson.Text(band["parentBandId"]) || StudioJson.Text(paired["group"]?["pairedBandId"]) != id) Error(path + ".group.pairedBandId", "分组头尾必须在同一父实例中互相配对。");
                var keys = Objects(group["keys"], path + ".group.keys", errors);
                if (keys.Length == 0) Error(path + ".group.keys", "分组键不能为空。");
                foreach (var key in keys) ValidateBinding(key, path + ".group.keys", instances, errors);
            }
            if (StudioJson.Text(band["emptyBehavior"]) is not (null or "Skip" or "Label") || StudioJson.Text(band["emptyBehavior"]) == "Label" && string.IsNullOrWhiteSpace(StudioJson.Text(band["emptyText"]))) Error(path + ".emptyBehavior", "空集合说明未配置。");
            var controls = Objects(band["controls"], path + ".controls", errors);
            var controlIndex = controls.GroupBy(c => StudioJson.RequiredString(c, "id")).ToDictionary(g => g.Key, g => g.First());
            if (controlIndex.Count != controls.Length) Error(path + ".controls", "控件编号重复。");
            foreach (var control in controls)
            {
                var cp = path + ".controls." + StudioJson.RequiredString(control, "id"); var type = StudioJson.Text(control["type"]);
                Allowed(control, ["id", "type", "xMm", "yMm", "widthMm", "heightMm", "styleId", "value", "table", "canGrow", "keepTogether", "flow", "visibleWhen", "aggregate"], cp, errors);
                if (type is not ("Text" or "Field" or "CheckBox" or "Table" or "Html" or "Image" or "Line" or "Box" or "PageNumber")) Error(cp + ".type", "控件类型未登记。");
                if (type == "CheckBox" && (control["value"] is not JsonObject || control.ContainsKey("aggregate"))) Error(cp + ".value", "复选框必须绑定布尔值，不能使用合计。");
                if (Number(control, "xMm") < 0 || Number(control, "yMm") < 0 || Number(control, "widthMm") <= 0 || Number(control, "heightMm") <= 0 || Number(control, "xMm") + Number(control, "widthMm") > bodyWidth + .001) Error(cp, "控件越过正文宽度。", "REPORT_LAYOUT_UNSUPPORTED");
                if (StudioJson.Text(control["styleId"]) is { Length: > 0 } sid && !styles.Contains(sid)) Error(cp + ".styleId", "样式不存在。");
                if (control["visibleWhen"] is { } when) ValidateCondition(when, cp + ".visibleWhen", instances, errors);
                if (control["aggregate"] is JsonObject aggregate)
                {
                    Allowed(aggregate, ["function", "scopeBandId", "field"], cp + ".aggregate", errors);
                    if (StudioJson.Text(aggregate["function"]) is not ("Count" or "Sum" or "Avg" or "Min" or "Max")) Error(cp + ".aggregate.function", "合计函数未登记。");
                    if (!index.ContainsKey(StudioJson.Text(aggregate["scopeBandId"]) ?? "")) Error(cp + ".aggregate.scopeBandId", "合计带不存在。");
                    if (StudioJson.Text(aggregate["function"]) != "Count") ValidateBinding(aggregate["field"], cp + ".aggregate.field", instances, errors);
                }
                if (control["value"] is { } value) ValidateBinding(value, cp + ".value", instances, errors);
                if (control["flow"] is JsonObject flow)
                {
                    Allowed(flow, ["mode", "afterControlIds"], cp + ".flow", errors);
                    if (StudioJson.Text(flow["mode"]) is not ("Follow" or "Overlay")) Error(cp + ".flow.mode", "流模式未登记。");
                    if (StudioJson.Text(flow["mode"]) == "Overlay" && (control["canGrow"]?.GetValue<bool>() != false || type is "Html" or "Table")) Error(cp + ".flow", "叠印只允许固定装饰。");
                    foreach (var dependency in (flow["afterControlIds"] as JsonArray ?? []).Select(StudioJson.Text))
                        if (dependency is null || !controlIndex.TryGetValue(dependency, out var before) || before == control || Number(before, "yMm") + Number(before, "heightMm") > Number(control, "yMm")) Error(cp + ".flow.afterControlIds", "跟随关系必须引用同带上方非重叠控件。", "REPORT_LAYOUT_OVERLAP");
                }
                if (type == "Table")
                {
                    if (control["table"] is not JsonObject table) { Error(cp + ".table", "表格定义缺失。"); continue; }
                    Allowed(table, ["rowMode", "columns", "rows", "headerMode", "repeatHeader", "headerHeightMm"], cp + ".table", errors);
                    if (table.ContainsKey("headerHeightMm") && Number(table, "headerHeightMm") is < 1 or > 1000) Error(cp + ".table.headerHeightMm", "表头高度必须在1至1000毫米内。");
                    var mode = StudioJson.Text(table["rowMode"]) ?? "BandRow";
                    if (mode is not ("BandRow" or "Collection") || mode == "BandRow" && control.ContainsKey("value") || mode == "Collection" && control["value"] is not JsonObject) Error(cp + ".table.rowMode", "当前行与集合模式绑定不匹配。");
                    if (StudioJson.Text(table["headerMode"]) is not (null or "EveryInstance" or "OncePerParent" or "Hidden")) Error(cp + ".table.headerMode", "表头模式未登记。");
                    var columns = Objects(table["columns"], cp + ".table.columns", errors);
                    if (columns.Length == 0 || columns.Sum(c => Number(c, "widthMm")) > Math.Min(bodyWidth, Number(control, "widthMm")) + .001) Error(cp + ".table.columns", "表格列宽超出正文或控件。", "REPORT_LAYOUT_UNSUPPORTED");
                    var columnIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var column in columns) { Allowed(column, ["id", "label", "widthMm", "value", "styleId", "header"], cp + ".table.columns", errors); if (!columnIds.Add(StudioJson.RequiredString(column, "id"))) Error(cp, "同一表格的列编号重复。"); if (Number(column, "widthMm") <= 0) Error(cp, "列宽必须为正。"); ValidateBinding(column["value"], cp + ".table.columns.value", instances, errors); }
                    ValidateTableSpans([columns], styles, cp + ".table.columns", errors);
                    var headerCells = columns.Select(c => c["header"] as JsonObject ?? new JsonObject()).ToArray();
                    foreach (var column in columns) if (column.ContainsKey("header") && column["header"] is not JsonObject) Error(cp + ".table.columns.header", "表头单元格必须为对象。");
                    foreach (var header in headerCells) Allowed(header, ["styleId", "colSpan", "rowSpan"], cp + ".table.columns.header", errors);
                    ValidateTableSpans([headerCells], styles, cp + ".table.columns.header", errors);
                    if (table.ContainsKey("rows"))
                    {
                        var rows = Objects(table["rows"], cp + ".table.rows", errors);
                        if (rows.Length == 0) Error(cp + ".table.rows", "至少保留一行模板。");
                        var rowIds = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var row in rows)
                        {
                            Allowed(row, ["id", "cells", "heightMm"], cp + ".table.rows", errors);
                            if (row.ContainsKey("heightMm") && Number(row, "heightMm") is < 1 or > 1000) Error(cp + ".table.rows.heightMm", "行高必须在1至1000毫米内。");
                            if (!rowIds.Add(StudioJson.RequiredString(row, "id"))) Error(cp + ".table.rows", "模板行编号重复。");
                            var cells = Objects(row["cells"], cp + ".table.rows.cells", errors);
                            var cellIds = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var cell in cells)
                            {
                                Allowed(cell, ["columnId", "value", "styleId", "colSpan", "rowSpan"], cp + ".table.rows.cells", errors);
                                if (!cellIds.Add(StudioJson.RequiredString(cell, "columnId"))) Error(cp + ".table.rows.cells", "模板行的单元格重复。");
                                ValidateBinding(cell["value"], cp + ".table.rows.cells.value", instances, errors);
                            }
                            if (!cellIds.SetEquals(columnIds)) Error(cp + ".table.rows.cells", "每行必须包含所有列且不能引用已删除的列。");
                        }
                        ValidateTableSpans(rows.Select(r => columns.Select(c => (r["cells"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(cell => StudioJson.Text(cell["columnId"]) == StudioJson.Text(c["id"])) ?? new JsonObject()).ToArray()).ToArray(), styles, cp + ".table.rows.cells", errors);
                    }
                }
                void ContextCheck(JsonNode? node)
                {
                    if (node is JsonArray array) { foreach (var n in array) ContextCheck(n); return; }
                    if (node is not JsonObject obj) return;
                    if (StudioJson.Text(obj["from"]) is { } from)
                    {
                        if (from == "Constant") return;
                        if (bandKind is "PageHeader" or "PageFooter" && from is "Row" or "ParentRow" or "Root") Error(cp, "物理页头页脚无明细行上下文。");
                        if (StudioJson.Text(obj["contextBandId"]) is { } context && !ancestors.Contains(context)) Error(cp, "绑定祖先带不在当前分支。");
                    }
                    foreach (var pair in obj) ContextCheck(pair.Value);
                }
                ContextCheck(control);
            }
        }
        if (StudioJson.Text(doc["mode"]) == "Query")
        {
            if (doc["query"] is not JsonObject query) { Error("query", "查询报告必须配置结果带与条件。"); return; }
            Allowed(query, ["instanceId", "resultBandId", "filters", "columns", "defaultSort", "defaultPageSize"], "query", errors);
            if (!instances.Contains(StudioJson.Text(query["instanceId"]), StringComparer.Ordinal) || !index.TryGetValue(StudioJson.Text(query["resultBandId"]) ?? "", out var resultBand) || StudioJson.Text(resultBand["kind"]) != "Detail") Error("query", "查询实例或结果明细带不存在。");
            var result = index.GetValueOrDefault(StudioJson.Text(query["resultBandId"]) ?? "");
            var sources = (result?["controls"] as JsonArray ?? []).OfType<JsonObject>().Where(c => StudioJson.Text(c["type"]) == "Table").SelectMany(c => (c["table"]?["columns"] as JsonArray ?? []).OfType<JsonObject>().Select(column => StudioJson.RequiredString(c, "id") + "/" + StudioJson.RequiredString(column, "id"))).ToHashSet(StringComparer.Ordinal);
            var visibleIds = new HashSet<string>(StringComparer.Ordinal); var sortableIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var column in Objects(query["columns"], "query.columns", errors))
            {
                Allowed(column, ["controlId", "columnId", "label", "visible", "sortable"], "query.columns", errors);
                var id = StudioJson.RequiredString(column, "columnId");
                if (!sources.Contains(StudioJson.RequiredString(column, "controlId") + "/" + id)) Error("query.columns", "查询列不属于结果表。");
                if (column["visible"]?.GetValue<bool>() != false && (!visibleIds.Add(id) || id == "__rowKey")) Error("query.columns", "可见查询列编号重复或使用保留名称。");
                if (column["sortable"]?.GetValue<bool>() == true && column["visible"]?.GetValue<bool>() != false) sortableIds.Add(id);
            }
            if (visibleIds.Count == 0) Error("query.columns", "至少选择一个可见查询列。");
            foreach (var sort in Objects(query["defaultSort"], "query.defaultSort", errors)) { Allowed(sort, ["fieldId", "direction", "nulls"], "query.defaultSort", errors); if (!sortableIds.Contains(StudioJson.Text(sort["fieldId"]) ?? "") || StudioJson.Text(sort["direction"]) is not ("Asc" or "Desc") || StudioJson.Text(sort["nulls"]) is not (null or "First" or "Last")) Error("query.defaultSort", "默认排序必须引用可见且可排序的列。"); }
            var filterNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var filter in Objects(query["filters"], "query.filters", errors))
            {
                Allowed(filter, ["parameterName", "label", "control", "optionsDatasetRevisionNId", "valueFieldId", "labelFieldId"], "query.filters", errors);
                if (!(doc["parameters"] as JsonArray ?? []).OfType<JsonObject>().Any(p => StudioJson.Text(p["name"]) == StudioJson.Text(filter["parameterName"]))) Error("query.filters.parameterName", "条件参数不存在。");
                if (StudioJson.Text(filter["control"]) is not ("Text" or "Number" or "Select" or "Date" or "DateRange" or "BusinessSelector")) Error("query.filters.control", "条件控件未登记。");
                if (!filterNames.Add(StudioJson.RequiredString(filter, "parameterName"))) Error("query.filters", "条件参数重复。");
                if (StudioJson.Text(filter["control"]) is "Select" or "BusinessSelector") foreach (var name in new[] { "optionsDatasetRevisionNId", "valueFieldId", "labelFieldId" }) if (string.IsNullOrWhiteSpace(StudioJson.Text(filter[name]))) Error("query.filters." + name, "选项来源与值、显示字段必须完整指定。");
            }
        }
        else if (doc.ContainsKey("query")) Error("query", "固定报告不能设置查询表单。");
    }
    private static void ValidateTableSpans(JsonObject[][] rows, HashSet<string> styles, string path, List<StudioDiagnostic> errors)
    {
        var covered = new HashSet<(int Row, int Column)>();
        for (var r = 0; r < rows.Length; r++) for (var c = 0; c < rows[r].Length; c++)
        {
            var cell = rows[r][c];
            if (StudioJson.Text(cell["styleId"]) is { Length: > 0 } style && !styles.Contains(style)) errors.Add(new("STUDIO_VALIDATION", path + ".styleId", "单元格样式不存在。"));
            var height = Number(cell, "rowSpan", 1); var width = Number(cell, "colSpan", 1);
            if (height < 1 || width < 1 || height != Math.Floor(height) || width != Math.Floor(width) || height > rows.Length - r || width > rows[r].Length - c)
            { errors.Add(new("STUDIO_VALIDATION", path, "合并范围必须为整数且不能超出模板行列。")); continue; }
            if (covered.Contains((r, c)))
            { if (height != 1 || width != 1) errors.Add(new("STUDIO_VALIDATION", path, "合并范围不能重叠。")); continue; }
            for (var y = r; y < r + (int)height; y++) for (var x = c; x < c + (int)width; x++)
                if (!covered.Add((y, x))) errors.Add(new("STUDIO_VALIDATION", path, "合并范围不能重叠。"));
        }
    }
}
