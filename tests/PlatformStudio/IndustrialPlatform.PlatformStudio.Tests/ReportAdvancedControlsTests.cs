using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class ReportAdvancedControlsTests
{
    internal static JsonObject AdvancedDocument() => Document(Examples());
    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();
    private static JsonNode Value(string json) => JsonNode.Parse(json)!;
    private static JsonObject Control(string type, string options, JsonNode? value = null, int width = 170, int height = 40) => new()
    {
        ["id"] = type, ["type"] = type, ["xMm"] = 0, ["yMm"] = 0, ["widthMm"] = width, ["heightMm"] = height,
        ["advanced"] = Obj(options), ["value"] = new JsonObject { ["from"] = "Constant", ["value"] = value, ["nullPolicy"] = "Null" }, ["canGrow"] = type == "PivotTable", ["keepTogether"] = type != "PivotTable"
    };
    private static JsonObject[] Examples() =>
    [
        Control("CharacterComb", """{"cells":12}""", JsonValue.Create("光刻胶PR-26"), height: 10),
        Control("Line", """{"direction":"DiagonalDown","stroke":"#264F73","strokeWidthMm":0.4,"dash":"Dashed"}""", height: 10),
        Control("Shape", """{"kind":"Ellipse","stroke":"#264F73","fill":"#D7EEF7","strokeWidthMm":0.4}""", width: 60, height: 25),
        Control("Barcode", """{"symbology":"Code39","showText":true}""", JsonValue.Create("PR-2026"), height: 20),
        Control("Chart", """{"kind":"Bar","categoryPath":["label"],"series":[{"name":"实际","valuePath":["value"]},{"name":"计划","valuePath":["planned"]}]}""", Value("""[{"label":"010混合","value":-5,"planned":8},{"label":"020过滤","value":0,"planned":10},{"label":"030灌装","value":12,"planned":null}]"""), height: 50),
        Control("Gauge", """{"min":0,"max":100,"label":"完成率"}""", JsonValue.Create(72), height: 25),
        Control("Sparkline", "{}", Value("[2,5,null,-2,0,8]"), height: 20),
        Control("PivotTable", """{"rowPath":["row"],"columnPath":["column"],"valuePath":["value"],"aggregate":"Sum","totals":true}""", Value("""[{"row":"010混合","column":"A料","value":"10.1"},{"row":"010混合","column":"A料","value":"2.2"},{"row":"010混合","column":"B料","value":null},{"row":"020过滤","column":"B料","value":"-5"}]"""), height: 35)
    ];
    private static JsonObject Document(JsonObject[] controls)
    {
        var doc = ReportPlanningTests.Report(new JsonArray(new JsonObject { ["id"] = "ONE" }));
        var originalBand = doc["bands"]![0]!.AsObject(); var bands = new JsonArray();
        foreach (var control in controls)
        {
            var band = originalBand.DeepClone().AsObject(); band["id"] = "band-" + control["id"]!.GetValue<string>(); band["controls"] = new JsonArray(control.DeepClone()); band["heightMm"] = control["heightMm"]!.DeepClone(); bands.Add(band);
        }
        doc["bands"] = bands; return doc;
    }
    [Fact]
    public async Task Advanced_controls_round_trip_plan_once_and_emit_inert_payloads_from_snapshot()
    {
        var doc = Document(Examples());
        Assert.Empty(StudioDocuments.Validate("Report", doc));
        var saved = JsonNode.Parse(doc.ToJsonString())!.AsObject(); Assert.Empty(StudioDocuments.Validate("Report", saved));
        saved["instances"]![0]!.AsObject()["sourceType"] = "Dataset"; saved["instances"]![0]!.AsObject().Remove("value"); saved["instances"]![0]!["revisionNId"] = "single-snapshot"; saved["instances"]![0]!["datasetNId"] = "snapshot-source";
        var calls = 0;
        var data = new ReportDataResolver((revision, _, _) => { Assert.Equal("single-snapshot", revision); calls++; return Task.FromResult(new JsonObject { ["data"] = new JsonArray(new JsonObject { ["id"] = "ONE" }) }); });
        var definition = new ReportDefinitionSnapshot("Report", saved, new Dictionary<string, ReportDefinitionSnapshot>(), "r3");
        var plan = await new ReportPlanner(data, new()).PlanAsync(definition, new(), default);
        var flow = ReportExpander.Expand(plan); var html = ReportHtmlComposer.Compose(flow, false);
        Assert.Equal(1, calls); Assert.Equal(8, flow.Blocks.Count); Assert.Equal(8, Regex.Count(html, "data-report-advanced="));
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal); Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        var restored = new ReportDataResolver((_, _, _) => throw new InvalidOperationException("No new source queries allowed")); restored.RestoreSnapshot(data.SnapshotDocument);
        var replay = await new ReportPlanner(restored, new()).PlanAsync(definition, new(), default);
        Assert.Equal(html, ReportHtmlComposer.Compose(ReportExpander.Expand(replay), false));
        if (Environment.GetEnvironmentVariable("PF09_R3_OUTPUT") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "all-controls-input.json"), new JsonObject { ["html"] = html, ["css"] = ReportHtmlComposer.Css, ["paper"] = doc["paper"]!.DeepClone(), ["format"] = "Pdf", ["expectedMarkers"] = new JsonArray(flow.ExpectedMarkers.Select(m => (JsonNode)JsonValue.Create(m)!).ToArray()) }.ToJsonString());
            await File.WriteAllTextAsync(Path.Combine(folder, "all-controls-document.json"), doc.ToJsonString());
            await File.WriteAllTextAsync(Path.Combine(folder, "all-controls-values.json"), new JsonArray(flow.Blocks.Select(b => (JsonNode)new JsonObject { ["type"] = b.Type, ["value"] = b.Value?.DeepClone() }).ToArray()).ToJsonString());
        }
    }
    [Fact]
    public void Chart_preserves_numeric_strings_and_shared_category_fields_without_mutating_snapshot()
    {
        var control = Control("Chart", """{"kind":"Line","categoryPath":["label"],"series":[{"name":"数量","valuePath":["nested","value"]}]}""");
        var source = Value("""[{"label":"010","nested":{"value":"-12.50"}},{"label":"020","nested":{"value":null}}]""");
        var resolved = ReportAdvancedValues.Resolve(control, source)!;
        Assert.Equal("-12.50", resolved[0]!["nested"]!["value"]!.GetValue<string>());
        Assert.Null(resolved[1]!["nested"]!["value"]);
        Assert.Equal("-12.50", source[0]!["nested"]!["value"]!.GetValue<string>());
        control["advanced"]!["categoryPath"] = new JsonArray("nested", "value");
        source = Value("""[{"nested":{"value":"001"}}]""");
        Assert.Equal("001", ReportAdvancedValues.Resolve(control, source)![0]!["nested"]!["value"]!.GetValue<string>());
    }
    [Fact]
    public void Shape_rejects_stroke_wider_than_its_geometry()
    {
        var control = Control("Shape", """{"kind":"Ellipse","stroke":"#000000","fill":"none","strokeWidthMm":5}""", width: 1);
        var errors = new List<StudioDiagnostic>();
        ReportAdvancedContract.Validate(control, "control", errors);
        Assert.Contains(errors, error => error.Path == "control.advanced.strokeWidthMm");
    }
    [Fact]
    public void Character_comb_counts_graphemes_and_never_truncates()
    {
        var c = Control("CharacterComb", """{"cells":3}""", JsonValue.Create("胶e\u0301😀"));
        var text = c["value"]!["value"]!; Assert.Equal("胶e\u0301😀", ReportAdvancedValues.Resolve(c, text)!.GetValue<string>());
        c["advanced"]!["cells"] = JsonNode.Parse("3.0"); Assert.Equal("胶e\u0301😀", ReportAdvancedValues.Resolve(c, text)!.GetValue<string>());
        c["advanced"]!["cells"] = 2;
        Assert.Equal("REPORT_CHARACTER_OVERFLOW", Assert.Throws<StudioException>(() => ReportAdvancedValues.Resolve(c, text)).Code);
    }
    [Theory]
    [InlineData("abc")]
    [InlineData("AB*C")]
    [InlineData("中文")]
    [InlineData("")]
    public void Barcode_rejects_unsupported_content_without_rewriting(string value)
    {
        var c = Control("Barcode", """{"symbology":"Code39","showText":true}""");
        Assert.Equal("REPORT_BARCODE_INVALID", Assert.Throws<StudioException>(() => ReportAdvancedValues.Resolve(c, JsonValue.Create(value))).Code);
    }
    [Fact]
    public void Barcode_preserves_spaces_and_enforces_modules_and_readable_height()
    {
        var c = Control("Barcode", """{"symbology":"Code39","showText":true}""");
        Assert.Equal("A B-12", ReportAdvancedValues.Resolve(c, JsonValue.Create("A B-12"))!.GetValue<string>());
        c["widthMm"] = 10; Assert.Equal("REPORT_BARCODE_TOO_SMALL", Assert.Throws<StudioException>(() => ReportAdvancedValues.Resolve(c, JsonValue.Create("A B-12"))).Code);
        c["heightMm"] = 13; Assert.NotEmpty(StudioDocuments.Validate("Report", Document([c])));
    }
    [Fact]
    public void Chart_gauge_and_sparkline_keep_null_gaps_and_reject_invalid_values()
    {
        var controls = Examples(); var chart = controls[4]; var value = chart["value"]!["value"]!;
        Assert.True(JsonNode.DeepEquals(value, ReportAdvancedValues.Resolve(chart, value)));
        value[1]! ["value"] = "bad"; Assert.Throws<StudioException>(() => ReportAdvancedValues.Resolve(chart, value));
        value[1]!["value"] = 0; value[1]!.AsObject().Remove("planned"); Assert.Equal("PATH_INVALID", Assert.Throws<StudioException>(() => ReportAdvancedValues.Resolve(chart, value)).Code);
        Assert.Equal("REPORT_GAUGE_OUT_OF_RANGE", Assert.Throws<StudioException>(() => ReportAdvancedValues.Resolve(controls[5], JsonValue.Create(101))).Code);
        var spark = ReportAdvancedValues.Resolve(controls[6], Value("[0,null,-1]"))!.AsArray(); Assert.Null(spark[1]); Assert.Equal(0, spark[0]!.GetValue<decimal>());
        Assert.Equal("STUDIO_LIMIT", Assert.Throws<StudioException>(() => ReportAdvancedValues.Resolve(controls[6], new JsonArray(Enumerable.Range(0, 201).Select(n => (JsonNode)JsonValue.Create(n)!).ToArray()))).Code);
    }
    [Theory]
    [InlineData("Sum", "12.3", "7.3")]
    [InlineData("Count", "2", "3")]
    [InlineData("Avg", "6.15", "2.4333333333333333333333333333")]
    [InlineData("Min", "2.2", "-5")]
    [InlineData("Max", "10.1", "10.1")]
    public void Pivot_uses_decimal_aggregation_and_underlying_values_for_totals(string aggregate, string cell, string total)
    {
        var c = Examples()[7]; c["advanced"]!["aggregate"] = aggregate;
        var result = ReportAdvancedValues.Resolve(c, c["value"]!["value"])!;
        Assert.Equal(cell, result["rows"]![0]!["values"]![0]!.GetValue<string>()); Assert.Equal(total, result["grandTotal"]!.GetValue<string>());
        Assert.Equal(aggregate == "Count" ? "0" : null, StudioJson.Text(result["rows"]![0]!["values"]![1]));
        Assert.Equal("010混合", result["rows"]![0]!["key"]!.GetValue<string>()); Assert.Equal("A料", result["columns"]![0]!.GetValue<string>());
    }
    [Fact]
    public void Pivot_limits_include_headers_and_totals_and_does_not_hide_missing_fields()
    {
        var c = Examples()[7]; var records = new JsonArray();
        for (var i = 0; i < 44; i++) records.Add(new JsonObject { ["row"] = i, ["column"] = i, ["value"] = 1 });
        Assert.Equal("STUDIO_LIMIT", Assert.Throws<StudioException>(() => ReportAdvancedValues.Resolve(c, records)).Code);
        Assert.Equal("PATH_INVALID", Assert.Throws<StudioException>(() => ReportAdvancedValues.Resolve(c, Value("""[{"row":"A","column":"B"}]"""))).Code);
        Assert.Throws<StudioException>(() => ReportAdvancedValues.Resolve(c, Value("""[{"row":"A","column":"B","value":"not-number"}]""")));
    }
    [Fact]
    public void Empty_data_is_explicit_and_readability_bounds_are_validated()
    {
        var controls = Examples();
        Assert.Empty(ReportAdvancedValues.Resolve(controls[4], new JsonArray())!.AsArray());
        Assert.Empty(ReportAdvancedValues.Resolve(controls[6], new JsonArray())!.AsArray());
        var pivot = ReportAdvancedValues.Resolve(controls[7], new JsonArray())!;
        Assert.Empty(pivot["rows"]!.AsArray()); Assert.Null(pivot["grandTotal"]);
        controls[4]["widthMm"] = 39; controls[5]["heightMm"] = 14;
        Assert.NotEmpty(StudioDocuments.Validate("Report", Document([controls[4]])));
        Assert.NotEmpty(StudioDocuments.Validate("Report", Document([controls[5]])));
        var shape = controls[2]; shape["advanced"]!["fill"] = "url(https://example.invalid/image)";
        Assert.NotEmpty(StudioDocuments.Validate("Report", Document([shape])));
        var chart = Examples()[4]; chart["heightMm"] = 25;
        chart["advanced"]!["series"] = new JsonArray(Enumerable.Range(0, 8).Select(i => (JsonNode)new JsonObject { ["name"] = "series-" + i, ["valuePath"] = new JsonArray("value") }).ToArray());
        Assert.NotEmpty(StudioDocuments.Validate("Report", Document([chart])));
        chart["heightMm"] = 37;
        Assert.Empty(StudioDocuments.Validate("Report", Document([chart])));
    }
    [Fact]
    public void Unsupported_options_and_injected_markup_fail_or_remain_inert()
    {
        foreach (var c in Examples()) { c["advanced"]!["script"] = "alert(1)"; Assert.NotEmpty(StudioDocuments.Validate("Report", Document([c]))); }
        var attack = ((char)34) + "><script>alert(1)</script>";
        var c2 = Control("CharacterComb", """{"cells":128}""", JsonValue.Create(attack));
        var definition = c2.DeepClone().AsObject(); definition["bandKind"] = "ReportHeader"; definition["bandHeightMm"] = 10;
        var block = new ReportFlowBlock("m", "b", "c", "CharacterComb", definition, JsonValue.Create(attack), "parent");
        var html = ReportHtmlComposer.Compose(new ReportContentFlow([block], ["m"]), false);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        var match = Regex.Match(html, "data-report-advanced=\"([^\"]+)\"");
        Assert.Equal(attack, JsonNode.Parse(WebUtility.HtmlDecode(match.Groups[1].Value))!["value"]!.GetValue<string>());
    }
}
