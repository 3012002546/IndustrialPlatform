using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

/// <summary>Builds inert content; rich Html blocks are sent as encoded source for the admitted DOM normalizer, never directly executed.</summary>
public static class ReportHtmlComposer
{
    private static readonly HashSet<string> LayoutKeys = new(["id", "type", "xMm", "yMm", "widthMm", "heightMm", "canGrow", "keepTogether", "flow", "resolvedStyle"], StringComparer.Ordinal);
    public const string Css = "body{margin:0;font-family:sans-serif}p{orphans:2;widows:2}table{border-collapse:collapse;width:100%}td,th{border:1px solid;padding:1mm;overflow-wrap:anywhere}thead{display:table-header-group}img{max-width:100%;object-fit:contain}.report-band{position:relative;break-inside:auto}.report-break-before-page{break-before:page}.report-keep-with-next{break-after:avoid}.report-preview{position:fixed;opacity:.15;top:45%;left:30%;transform:rotate(-30deg);font-size:40pt}";
    public static string Compose(ReportContentFlow flow, bool preview)
    {
        var output = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"></head><body>");
        if (preview) output.Append("<div class=\"report-preview\">PREVIEW · 试运行</div>");
        foreach (var group in flow.Blocks.GroupBy(b => b.ParentInstanceKey, StringComparer.Ordinal))
        {
            var first = group.First();
            output.Append("<section class=\"report-band\"");
            if (StudioJson.Text(first.Definition["paginationPart"]?["key"]) is { } partKey) output.Append(" data-report-part=\"").Append(WebUtility.HtmlEncode(partKey)).Append('"');
            output.Append(" data-report-band=\"").Append(WebUtility.HtmlEncode(new JsonObject { ["heightMm"] = first.Definition["bandHeightMm"]?.DeepClone(), ["kind"] = first.Definition["bandKind"]?.DeepClone(), ["keepWithNext"] = first.Definition["bandKeepWithNext"]?.DeepClone(), ["breakBefore"] = first.Definition["bandBreakBefore"]?.DeepClone(), ["instanceKey"] = group.Key }.ToJsonString())).Append("\">");
            foreach (var block in group)
            {
                var marker = WebUtility.HtmlEncode(block.Marker); var definition = block.Definition;
                var layout = new JsonObject(definition.Where(pair => LayoutKeys.Contains(pair.Key)).Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())));
                output.Append("<div data-report-marker=\"").Append(marker).Append('"').Append(block.Repeat ? " data-report-repeat=\"true\"" : "").Append(" data-report-control=\"").Append(WebUtility.HtmlEncode(layout.ToJsonString())).Append("\" style=\"").Append(WebUtility.HtmlEncode(Style(definition["resolvedStyle"] as JsonObject))).Append("\">");
                if (block.Type == "Table")
                {
                    AppendTable(output, definition, block.Value as JsonArray ?? []);
                }
                else if (block.Type == "Html")
                {
                    // This is an inter-process source file, never a browser response. The command MUST normalize it in inert DOM before setContent.
                    var record = block.Value as JsonObject; var source = record is null ? StudioJson.Text(block.Value) : StudioJson.Text(record["html"]);
                    if (source is null) throw new StudioException("TYPE_MISMATCH", "HTML记录必须提供已保存html。", 422, block.ControlId);
                    output.Append("<section data-report-html=\"true\" data-report-resources=\"").Append(WebUtility.HtmlEncode(definition["frozenHtmlResources"]?.ToJsonString() ?? "{}")).Append("\" data-report-form-values=\"").Append(WebUtility.HtmlEncode(record?["formValues"]?.ToJsonString() ?? "{}")).Append("\">").Append(WebUtility.HtmlEncode(source)).Append("</section>");
                }
                else if (block.Type == "Image") output.Append("<img alt=\"\" style=\"display:block;width:100%;height:").Append(Numeric(definition["heightMm"], 10).ToString(CultureInfo.InvariantCulture)).Append("mm;object-fit:contain\" src=\"").Append(WebUtility.HtmlEncode(StudioJson.RequiredString(definition, "frozenImageData", 8 * 1024 * 1024))).Append("\">");
                else if (block.Type == "CheckBox") output.Append("<span role=\"checkbox\" aria-checked=\"").Append(block.Value is null ? "mixed" : block.Value.GetValue<bool>() ? "true" : "false").Append("\" style=\"display:inline-flex;align-items:center;justify-content:center;width:3.5mm;height:3.5mm;border:0.2mm solid currentColor;line-height:1\">").Append(block.Value is null ? "−" : block.Value.GetValue<bool>() ? "✓" : "").Append("</span>");
                else if (block.Type == "PageNumber") output.Append("<span data-report-page-number=\"true\"></span>");
                else output.Append(WebUtility.HtmlEncode(Formatted(block.Value, definition["resolvedStyle"] as JsonObject)));
                output.Append("</div>");
            }
            output.Append("</section>");
        }
        return output.Append("</body></html>").ToString();
    }
    private static void AppendTable(StringBuilder output, JsonObject definition, JsonArray values)
    {
        var table = definition["table"]!.AsObject();
        var columns = table["columns"]!.AsArray().OfType<JsonObject>().ToArray();
        var templates = table["rows"] as JsonArray;
        var templateCount = templates?.Count ?? 1;
        JsonObject Cell(int row, int column) => row < 0 ? columns[column]["header"] as JsonObject ?? new JsonObject()
            : (templates?[row]?["cells"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(c => StudioJson.Text(c["columnId"]) == StudioJson.Text(columns[column]["id"])) ?? new JsonObject();
        JsonObject CellStyle(int row, int column, JsonObject cell)
        {
            var style = definition["resolvedStyle"]?.DeepClone() as JsonObject ?? new JsonObject();
            if (row < 0) style["fontWeight"] = 700;
            var columnStyle = row < 0 ? null : definition["columnStyles"]?[StudioJson.RequiredString(columns[column], "id")] as JsonObject;
            foreach (var pair in columnStyle ?? []) style[pair.Key] = pair.Value?.DeepClone();
            var ownStyle = StudioJson.Text(cell["styleId"]) is { } id ? definition["tableStyles"]?[id] as JsonObject : null;
            foreach (var pair in ownStyle ?? []) style[pair.Key] = pair.Value?.DeepClone();
            style["border"] ??= new JsonObject { ["style"] = "Solid", ["widthMm"] = .2, ["color"] = "#000000" };
            style["paddingMm"] ??= new JsonObject { ["top"] = 1, ["right"] = 1, ["bottom"] = 1, ["left"] = 1 };
            return style;
        }
        var repeatHeader = table["repeatHeader"]?.GetValue<bool>() != false;
        output.Append("<table data-report-template=\"true\" data-report-repeat-header=\"").Append(repeatHeader ? "true" : "false").Append("\" style=\"table-layout:fixed\"><colgroup>");
        foreach (var column in columns) output.Append("<col style=\"width:").Append(Numeric(column["widthMm"], 20).ToString(CultureInfo.InvariantCulture)).Append("mm\">");
        output.Append("</colgroup>");
        if (definition["showHeader"]?.GetValue<bool>() == true)
        {
            output.Append(repeatHeader ? "<thead><tr" : "<tbody data-report-header=\"true\"><tr"); Height(table["headerHeightMm"]); output.Append('>');
            for (var c = 0; c < columns.Length;)
            {
                var cell = Cell(-1, c); var span = (int)Numeric(cell["colSpan"], 1);
                output.Append("<th colspan=\"").Append(span).Append("\" style=\"").Append(WebUtility.HtmlEncode(Style(CellStyle(-1, c, cell)))).Append("\">").Append(WebUtility.HtmlEncode(StudioJson.Text(columns[c]["label"]))).Append("</th>");
                c += span;
            }
            output.Append(repeatHeader ? "</tr></thead>" : "</tr></tbody>");
        }
        var covered = new HashSet<(int Row, int Column)>();
        for (var r = 0; r < values.Count; r++)
        {
            var template = r % templateCount;
            if (template == 0) output.Append("<tbody style=\"break-inside:avoid\">");
            output.Append("<tr"); Height(templates?[template]?["heightMm"]); output.Append('>');
            for (var c = 0; c < columns.Length; c++)
            {
                if (covered.Contains((r, c))) continue;
                var cell = Cell(template, c); var width = (int)Numeric(cell["colSpan"], 1); var height = (int)Numeric(cell["rowSpan"], 1);
                var style = CellStyle(template, c, cell);
                output.Append("<td colspan=\"").Append(width).Append("\" rowspan=\"").Append(height).Append("\" style=\"").Append(WebUtility.HtmlEncode(Style(style))).Append("\">").Append(WebUtility.HtmlEncode(Formatted(values[r]?[c], style))).Append("</td>");
                for (var y = r; y < r + height; y++) for (var x = c; x < c + width; x++) covered.Add((y, x));
            }
            output.Append("</tr>");
            if (template == templateCount - 1) output.Append("</tbody>");
        }
        output.Append("</table>");
        void Height(JsonNode? height) { output.Append(" style=\"height:").Append(Numeric(height, 7).ToString(CultureInfo.InvariantCulture)).Append("mm\""); }
    }
    private static string Text(JsonNode? value) => StudioJson.Text(value) ?? value?.ToJsonString() ?? "";
    private static string Formatted(JsonNode? value, JsonObject? style)
    {
        if (value is null) return "";
        try
        {
            if (StudioJson.Text(style?["dateFormat"]) is { Length: > 0 } dateFormat) return DateTimeOffset.Parse(Text(value), CultureInfo.InvariantCulture).ToString(dateFormat, CultureInfo.InvariantCulture);
            if (StudioJson.Text(style?["numberFormat"]) is { Length: > 0 } numberFormat) return decimal.Parse(Text(value), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture).ToString(numberFormat, CultureInfo.InvariantCulture);
            return Text(value);
        }
        catch (Exception error) when (error is FormatException or OverflowException) { throw new StudioException("REPORT_FORMAT_INVALID", "值或显示格式不兼容，未替换为空值或零。", 422); }
    }
    private static double Numeric(JsonNode? node, double fallback) => node is null ? fallback : double.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : throw new StudioException("REPORT_LAYOUT_UNSUPPORTED", "样式数值无效。", 422);
    private static string Style(JsonObject? style)
    {
        if (style is null) return "";
        var font = StudioJson.Text(style["fontFamily"]) ?? "sans-serif";
        if (!System.Text.RegularExpressions.Regex.IsMatch(font, @"^[\p{L}\p{N} _,-]{1,128}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new StudioException("REPORT_FONT_UNAVAILABLE", "字体键不在安全登记格式中。", 422);
        var color = StudioJson.Text(style["color"]) ?? "#000000";
        if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new StudioException("REPORT_LAYOUT_UNSUPPORTED", "颜色格式不受支持。", 422);
        string Color(JsonNode? node, string fallback)
        {
            var text = StudioJson.Text(node) ?? fallback;
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, "^#[0-9a-fA-F]{6}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new StudioException("REPORT_LAYOUT_UNSUPPORTED", "样式颜色无效。", 422);
            return text;
        }
        var css = "font-family:" + font + ";font-size:" + Numeric(style["fontSizePt"], 10).ToString(CultureInfo.InvariantCulture) + "pt;font-weight:" + Numeric(style["fontWeight"], 400).ToString(CultureInfo.InvariantCulture) + ";line-height:" + Numeric(style["lineHeight"], 1.3).ToString(CultureInfo.InvariantCulture) + ";color:" + color + ";text-align:" + (StudioJson.Text(style["textAlign"]) == "Center" ? "center" : StudioJson.Text(style["textAlign"]) == "Right" ? "right" : "left") + ";font-style:" + (style["italic"]?.GetValue<bool>() == true ? "italic" : "normal") + ";vertical-align:" + (StudioJson.Text(style["verticalAlign"]) == "Middle" ? "middle" : StudioJson.Text(style["verticalAlign"]) == "Bottom" ? "bottom" : "top");
        if (style["backgroundColor"] is { } background) css += ";background-color:" + Color(background, "#FFFFFF");
        if (style["paddingMm"] is JsonObject padding) foreach (var side in new[] { "top", "right", "bottom", "left" }) { var amount = Numeric(padding[side], 0); if (amount is < 0 or > 100) throw new StudioException("REPORT_LAYOUT_UNSUPPORTED", "内边距超限。", 422); css += ";padding-" + side + ":" + amount.ToString(CultureInfo.InvariantCulture) + "mm"; }
        if (style["border"] is JsonObject border) { var width = Numeric(border["widthMm"], .2); if (width is < 0 or > 10) throw new StudioException("REPORT_LAYOUT_UNSUPPORTED", "边框宽度超限。", 422); css += ";border:" + width.ToString(CultureInfo.InvariantCulture) + "mm " + (StudioJson.Text(border["style"]) == "Dashed" ? "dashed" : StudioJson.Text(border["style"]) == "None" ? "none" : "solid") + " " + Color(border["color"], "#000000"); }
        return css;
    }
}
