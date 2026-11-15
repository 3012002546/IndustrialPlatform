using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>高级报表控件的已登记配置；存储只保存绑定与参数，不保存渲染脚本。</summary>
public static class ReportAdvancedContract
{
    public static bool IsAdvanced(string? type) => type is "CharacterComb" or "Shape" or "Barcode" or "Chart" or "Gauge" or "Sparkline" or "PivotTable";
    public static bool UsesAdvanced(JsonObject control) => IsAdvanced(StudioJson.Text(control["type"])) || StudioJson.Text(control["type"]) == "Line" && control.ContainsKey("advanced");
    public static void Validate(JsonObject control, string path, List<StudioDiagnostic> errors)
    {
        var type = StudioJson.Text(control["type"]);
        if (!UsesAdvanced(control))
        {
            if (control.ContainsKey("advanced")) errors.Add(new("STUDIO_VALIDATION", path + ".advanced", "该控件不支持高级配置。"));
            return;
        }
        void Error(string field, string message) => errors.Add(new("STUDIO_VALIDATION", path + ".advanced" + field, message));
        if (control["advanced"] is not JsonObject options) { Error("", "高级控件配置缺失。"); return; }
        if (type is not ("Line" or "Shape") && control["value"] is not JsonObject) errors.Add(new("STUDIO_VALIDATION", path + ".value", "高级数据控件必须配置值绑定。"));
        if (control.ContainsKey("aggregate")) errors.Add(new("STUDIO_VALIDATION", path + ".aggregate", "高级控件使用本次绑定数据，不另设区段合计。"));
        string[] allowed = type switch
        {
            "CharacterComb" => ["cells"], "Shape" => ["kind", "stroke", "fill", "strokeWidthMm"],
            "Line" => ["direction", "stroke", "strokeWidthMm", "dash"], "Barcode" => ["symbology", "showText"],
            "Chart" => ["kind", "categoryPath", "series"], "Gauge" => ["min", "max", "label"], "Sparkline" => [],
            "PivotTable" => ["rowPath", "columnPath", "valuePath", "aggregate", "totals"], _ => []
        };
        foreach (var field in options) if (!allowed.Contains(field.Key, StringComparer.Ordinal)) Error("." + field.Key, "高级控件配置字段未登记。");
        void Choice(string key, params string[] choices) { if (!choices.Contains(StudioJson.Text(options[key]), StringComparer.Ordinal)) Error("." + key, "请选择已登记选项。"); }
        void Boolean(string key) { if (options[key] is not JsonValue v || !v.TryGetValue<bool>(out _)) Error("." + key, "需要布尔值。"); }
        void FieldPath(JsonNode? node, string key)
        {
            if (node is not JsonArray a || a.Count is < 1 or > 16 || a.Any(n => StudioJson.Text(n) is not { Length: > 0 and <= 128 } text || text is "__proto__" or "prototype" or "constructor")) Error("." + key, "字段路径需为1至16个有效名称。");
        }
        double Numeric(string key)
        {
            if (options[key] is not JsonValue v || !double.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) { Error("." + key, "需要有限数字。"); return double.NaN; }
            return number;
        }
        double Dimension(string key) => control[key] is JsonValue dimension && double.TryParse(dimension.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
        if (type == "Barcode" && Dimension("heightMm") < (options["showText"] is JsonValue showValue && showValue.TryGetValue<bool>(out var show) && show ? 14 : 10)) Error("", "条码高度至少10毫米，显示文字时至少14毫米。");
        if (type == "Chart" && (Dimension("widthMm") < 40 || Dimension("heightMm") < 25)) Error("", "图表尺寸至少40×25毫米。");
        if (type == "Gauge" && (Dimension("widthMm") < 30 || Dimension("heightMm") < 15)) Error("", "仪表尺寸至少30×15毫米。");
        if (type == "CharacterComb") { var n = Numeric("cells"); if (n is < 1 or > 128 || n != Math.Truncate(n)) Error(".cells", "字符格数须为1至128的整数。"); }
        if (type is "Line" or "Shape")
        {
            if (!Color(StudioJson.Text(options["stroke"]))) Error(".stroke", "线条颜色必须为六位十六进制颜色。");
            var width = Numeric("strokeWidthMm"); if (width is <= 0 or > 5) Error(".strokeWidthMm", "线宽须大于0且不超过5毫米。");
            if (width >= Math.Min(Dimension("widthMm"), Dimension("heightMm"))) Error(".strokeWidthMm", "线宽必须小于控件宽度和高度。");
            if (type == "Shape") { Choice("kind", "Rectangle", "Ellipse", "Diamond"); if (StudioJson.Text(options["fill"]) != "none" && !Color(StudioJson.Text(options["fill"]))) Error(".fill", "填充须为六位颜色或none。"); }
            else { Choice("direction", "Horizontal", "Vertical", "DiagonalDown", "DiagonalUp"); Choice("dash", "Solid", "Dashed", "Dotted"); }
        }
        if (type == "Barcode") { Choice("symbology", "Code39"); Boolean("showText"); }
        if (type == "Gauge")
        {
            var min = Numeric("min"); var max = Numeric("max"); if (min >= max || Math.Abs(min) > 1e12 || Math.Abs(max) > 1e12) Error(".max", "仪表上限须大于下限且范围不超过正负一万亿。");
            if (StudioJson.Text(options["label"]) is not { Length: <= 128 }) Error(".label", "仪表标签须为不超过128字的文本。");
        }
        if (type == "Chart")
        {
            Choice("kind", "Bar", "Line"); FieldPath(options["categoryPath"], "categoryPath");
            if (options["series"] is not JsonArray series || series.Count is < 1 or > 8) Error(".series", "图表需1至8个系列。");
            else
            {
                if (Dimension("heightMm") < 21 + 4 * Math.Ceiling(series.Count / 2d)) Error(".series", "请增加图表高度，为全部系列图例和绘图区保留空间。");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in series)
                {
                    if (item is not JsonObject entry) { Error(".series", "系列必须是对象。"); continue; }
                    foreach (var field in entry) if (field.Key is not ("name" or "valuePath")) Error(".series." + field.Key, "系列字段未登记。");
                    var name = StudioJson.Text(entry["name"]); if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || !names.Add(name)) Error(".series.name", "系列名称须唯一且不超过128字。");
                    FieldPath(entry["valuePath"], "series.valuePath");
                }
            }
        }
        if (type == "PivotTable") { FieldPath(options["rowPath"], "rowPath"); FieldPath(options["columnPath"], "columnPath"); FieldPath(options["valuePath"], "valuePath"); Choice("aggregate", "Sum", "Count", "Avg", "Min", "Max"); Boolean("totals"); }
    }
    private static bool Color(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
