using System.Globalization;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Report;

/// <summary>仅消费本次已绑定的快照值；高级控件不发起额外查询，透视统计使用decimal保留数量精度。</summary>
public static class ReportAdvancedValues
{
    public static JsonNode? Resolve(JsonObject control, JsonNode? value)
    {
        if (!ReportAdvancedContract.UsesAdvanced(control)) return value;
        var diagnostics = new List<StudioDiagnostic>(); ReportAdvancedContract.Validate(control, "control", diagnostics);
        if (diagnostics.FirstOrDefault() is { } error) throw new StudioException(error.Code, error.Message, 422, error.Path);
        var type = StudioJson.RequiredString(control, "type"); var options = control["advanced"]!.AsObject();
        var id = StudioJson.RequiredString(control, "id");
        switch (type)
        {
            case "Line": case "Shape": return null;
            case "CharacterComb":
                var text = Scalar(value, id);
                if (new StringInfo(text).LengthInTextElements > (int)decimal.Parse(options["cells"]!.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture)) throw Invalid(id, "字符超过格数，未截断内容。", "REPORT_CHARACTER_OVERFLOW");
                return JsonValue.Create(text);
            case "Barcode":
                var code = StudioJson.Text(value);
                if (code is null || code.Length is < 1 or > 40 || code.Any(c => !"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%".Contains(c, StringComparison.Ordinal))) throw Invalid(id, "Code39仅支持1至40个已登记大写字符，不自动改写数据。", "REPORT_BARCODE_INVALID");
                var minimum = (16 * (code.Length + 2) - 1 + 20) * .25m;
                if (Number(control["widthMm"], id) < minimum) throw Invalid(id, "条码宽度不足以保留0.25毫米窄条及静区。", "REPORT_BARCODE_TOO_SMALL");
                return JsonValue.Create(code);
            case "Gauge":
                var number = Number(value, id);
                if (number < Number(options["min"], id) || number > Number(options["max"], id)) throw Invalid(id, "仪表值超出所配置的范围。", "REPORT_GAUGE_OUT_OF_RANGE");
                return JsonValue.Create(number);
            case "Sparkline":
                var spark = Rows(value, 200, id);
                return new JsonArray(spark.Select(v => v is null ? null : JsonValue.Create(Number(v, id))).ToArray<JsonNode?>());
            case "Chart":
                var rows = Rows(value, 200, id).DeepClone().AsArray();
                foreach (var row in rows)
                {
                    _ = Scalar(Path(row, options["categoryPath"]!.AsArray(), id), id);
                    foreach (var series in options["series"]!.AsArray().OfType<JsonObject>())
                        if (Path(row, series["valuePath"]!.AsArray(), id) is { } n) _ = Number(n, id);
                }
                return rows;
            case "PivotTable": return Pivot(options, Rows(value, 100000, id), id);
            default: throw Invalid(id, "高级控件类型未登记。");
        }
    }
    private static JsonArray Rows(JsonNode? value, int maximum, string id)
    {
        if (value is not JsonArray rows) throw Invalid(id, "控件集合绑定必须返回数组。");
        if (rows.Count > maximum) throw Invalid(id, "控件数据超过数量预算。", "STUDIO_LIMIT");
        return rows;
    }
    private static string Scalar(JsonNode? value, string id)
    {
        if (value is not JsonValue) throw Invalid(id, "需要非空标量值。");
        var text = StudioJson.Text(value) ?? value.ToJsonString();
        if (text.Length > 1024) throw Invalid(id, "标签文本超过1024字预算。", "STUDIO_LIMIT");
        return text;
    }
    private static decimal Number(JsonNode? value, string id)
    {
        if (value is not JsonValue || !decimal.TryParse(StudioJson.Text(value) ?? value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number is < -1000000000000m or > 1000000000000m) throw Invalid(id, "需要正负一万亿范围内的有限数字；空值不当零。");
        return number;
    }
    private static JsonNode? Path(JsonNode? row, JsonArray path, string id)
    {
        var current = row;
        foreach (var segment in path)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment!.GetValue<string>(), out current)) throw Invalid(id, "高级控件字段路径不存在。", "PATH_INVALID");
        }
        return current;
    }
    private static StudioException Invalid(string id, string message, string code = "TYPE_MISMATCH") => new(code, message, 422, id);
    private static JsonObject Pivot(JsonObject options, JsonArray source, string id)
    {
        var rowKeys = new List<JsonNode>(); var colKeys = new List<JsonNode>();
        var rowIndex = new Dictionary<string, int>(StringComparer.Ordinal); var colIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var cells = new Dictionary<(int Row, int Column), List<decimal>>();
        var rowValues = new Dictionary<int, List<decimal>>(); var colValues = new Dictionary<int, List<decimal>>(); var all = new List<decimal>();
        var totals = options["totals"]!.GetValue<bool>();
        int Key(JsonNode? node, Dictionary<string, int> index, List<JsonNode> keys)
        {
            _ = Scalar(node, id); var canonical = StudioJson.Canonicalize(node);
            if (!index.TryGetValue(canonical, out var i)) { i = index.Count; index.Add(canonical, i); keys.Add(node!.DeepClone()); }
            return i;
        }
        static void Add<TKey>(Dictionary<TKey, List<decimal>> target, TKey key, decimal n) where TKey : notnull
        { if (!target.TryGetValue(key, out var list)) { list = []; target.Add(key, list); } list.Add(n); }
        foreach (var sourceRow in source)
        {
            var r = Key(Path(sourceRow, options["rowPath"]!.AsArray(), id), rowIndex, rowKeys);
            var c = Key(Path(sourceRow, options["columnPath"]!.AsArray(), id), colIndex, colKeys);
            if (rowIndex.Count > 200 || colIndex.Count > 64 || (rowIndex.Count + 1 + (totals ? 1 : 0)) * (colIndex.Count + 1 + (totals ? 1 : 0)) > 2048) throw Invalid(id, "透视表超过200行、64列或2048个含表头/合计的单元格预算。", "STUDIO_LIMIT");
            if (Path(sourceRow, options["valuePath"]!.AsArray(), id) is not { } field) continue;
            var n = Number(field, id); Add(cells, (r, c), n); Add(rowValues, r, n); Add(colValues, c, n); all.Add(n);
        }
        JsonNode? Aggregate(List<decimal>? values)
        {
            var function = StudioJson.Text(options["aggregate"]); values ??= [];
            decimal? result = function == "Count" ? values.Count : values.Count == 0 ? null : function switch
            { "Sum" => values.Sum(), "Avg" => values.Average(), "Min" => values.Min(), "Max" => values.Max(), _ => throw Invalid(id, "透视函数未登记。") };
            return result is { } number ? JsonValue.Create(number.ToString(CultureInfo.InvariantCulture)) : null;
        }
        var resultRows = new JsonArray();
        for (var r = 0; r < rowKeys.Count; r++)
        {
            var values = new JsonArray(); for (var c = 0; c < colKeys.Count; c++) values.Add(Aggregate(cells.GetValueOrDefault((r, c))));
            resultRows.Add(new JsonObject { ["key"] = rowKeys[r].DeepClone(), ["values"] = values, ["total"] = totals ? Aggregate(rowValues.GetValueOrDefault(r)) : null });
        }
        return new JsonObject { ["columns"] = new JsonArray(colKeys.Select(k => k.DeepClone()).ToArray()), ["rows"] = resultRows,
            ["totals"] = totals ? new JsonArray(Enumerable.Range(0, colKeys.Count).Select(c => Aggregate(colValues.GetValueOrDefault(c))).ToArray()) : new JsonArray(), ["grandTotal"] = totals ? Aggregate(all) : null };
    }
}
