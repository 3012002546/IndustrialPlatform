using System.IO.Compression;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class ReportPlanningTests
{
    private static readonly string[] TreeExpected = ["A", "B", "C", "D", "E", "F"];
    private static readonly string[] RunsExpected = ["R1", "R2"];
    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
    private static JsonObject Paper => Parse("""{"size":"A4","orientation":"Portrait","margin":{"top":15,"right":15,"bottom":15,"left":15},"headerHeightMm":15,"footerHeightMm":10}""");
    private static JsonObject Field(string id, string path) => new() { ["id"] = id, ["type"] = "Field", ["xMm"] = 0, ["yMm"] = 0, ["widthMm"] = 50, ["heightMm"] = 5, ["value"] = Parse("""{"from":"Row","path":[],"nullPolicy":"Error"}""") };
    private static JsonObject Report(JsonArray rows)
    {
        var field = Field("value", "id"); field["value"]!["path"] = new JsonArray("id");
        return new() { ["schemaVersion"] = 1, ["mode"] = "Fixed", ["parameters"] = new JsonArray(), ["instances"] = new JsonArray(new JsonObject { ["instanceId"] = "root", ["sourceType"] = "Constant", ["value"] = rows, ["parameters"] = new JsonArray() }), ["paper"] = Paper, ["bands"] = new JsonArray(new JsonObject { ["id"] = "root-band", ["kind"] = "Detail", ["name"] = "Root", ["heightMm"] = 10, ["controls"] = new JsonArray(field), ["data"] = Parse("""{"mode":"RootPath","instanceId":"root","path":[]}""") }), ["styles"] = new JsonArray(), ["assets"] = new JsonArray() };
    }
    private static ReportDataResolver Resolver => new((_, _, _) => throw new InvalidOperationException("No sources expected"));
    private static ReportDefinitionSnapshot Definition(JsonObject doc) => new("Report", doc, new Dictionary<string, ReportDefinitionSnapshot>(), "fixture");

    [Fact]
    public void Table_column_style_reference_cannot_silently_fall_back_to_default()
    {
        var document = Report(new JsonArray(new JsonObject { ["id"] = "row" }));
        var table = document["bands"]![0]!["controls"]![0]!.AsObject();
        table["type"] = "Table"; table.Remove("value");
        table["table"] = Parse("""{"rowMode":"BandRow","columns":[{"id":"value","label":"Value","widthMm":50,"styleId":"missing-style","value":{"from":"Row","path":["id"],"nullPolicy":"Error"}}]}""");
        Assert.Contains(StudioDocuments.Validate("Report", document), diagnostic => diagnostic.Path.EndsWith("columns.styleId", StringComparison.Ordinal));
        table["table"]!["columns"]![0]!.AsObject().Remove("styleId");
        Assert.Empty(StudioDocuments.Validate("Report", document));
    }

    [Fact]
    public async Task Table_cell_styles_spans_and_heights_are_preserved_per_record()
    {
        var doc = Report(JsonNode.Parse("""[{"id":"A"},{"id":"B"},{"id":"C"},{"id":"D"},{"id":"E"},{"id":"F"}]""")!.AsArray());
        var control = doc["bands"]![0]!["controls"]![0]!.AsObject();
        control["type"] = "Table"; control.Remove("value");
        var table = Parse("""{"rowMode":"BandRow","headerMode":"OncePerParent","repeatHeader":true,"headerHeightMm":9,"columns":[{"id":"a","label":"Title","widthMm":30,"header":{"colSpan":2,"styleId":"cell"}},{"id":"b","label":"Covered header","widthMm":20}],"rows":[{"id":"r1","heightMm":12,"cells":[{"columnId":"a","rowSpan":2,"styleId":"cell","value":{"from":"Row","path":["id"],"nullPolicy":"Error"}},{"columnId":"b","value":{"from":"Constant","value":"Note","nullPolicy":"Error"}}]},{"id":"r2","heightMm":8,"cells":[{"columnId":"a","value":{"from":"Constant","value":"Hidden value","nullPolicy":"Error"}},{"columnId":"b","value":{"from":"Constant","value":"Second","nullPolicy":"Error"}}]}]}""");
        control["table"] = table;
        foreach (var column in table["columns"]!.AsArray()) column!["value"] = Parse("""{"from":"Constant","value":null,"nullPolicy":"Null"}""");
        doc["styles"]!.AsArray().Add(Parse("""{"id":"cell","fontFamily":"sans-serif","fontSizePt":14,"fontWeight":700,"textAlign":"Center","verticalAlign":"Middle","backgroundColor":"#ddeeff","border":{"style":"Dashed","widthMm":0.5,"color":"#112233"}}"""));
        Assert.Empty(StudioDocuments.Validate("Report", doc));
        var plan = await new ReportPlanner(Resolver, new()).PlanAsync(Definition(doc), new(), default);
        Assert.Equal(6, plan.Instances[0].RowCount);
        var html = ReportHtmlComposer.Compose(ReportExpander.Expand(plan), false);
        Assert.Equal(6, System.Text.RegularExpressions.Regex.Count(html, "rowspan=\"2\""));
        Assert.Equal(6, System.Text.RegularExpressions.Regex.Count(html, ">Note</td>"));
        Assert.Equal(6, System.Text.RegularExpressions.Regex.Count(html, ">Second</td>"));
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(html, "<thead>"));
        Assert.Contains("<th colspan=\"2\"", html, StringComparison.Ordinal);
        Assert.Contains("height:12mm", html, StringComparison.Ordinal);
        Assert.Contains("height:8mm", html, StringComparison.Ordinal);
        Assert.Contains("background-color:#ddeeff", html, StringComparison.Ordinal);
        Assert.Contains("vertical-align:middle", html, StringComparison.Ordinal);
        Assert.Contains("border:0.5mm dashed #112233", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden value", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Covered header", html, StringComparison.Ordinal);
        table["rows"]![0]!["cells"]![0]!["rowSpan"] = 3;
        Assert.NotEmpty(StudioDocuments.Validate("Report", doc));
        table["rows"]![0]!["cells"]![0]!["rowSpan"] = 2;
        table["rows"]![1]!["cells"]![0]!["colSpan"] = 2;
        Assert.NotEmpty(StudioDocuments.Validate("Report", doc));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Table_header_repeat_flag_preserves_first_header_and_controls_continuation(bool repeatHeader)
    {
        var doc = Report(new JsonArray(new JsonObject { ["id"] = "ROW" }));
        var control = doc["bands"]![0]!["controls"]![0]!.AsObject(); control["type"] = "Table"; control.Remove("value");
        control["table"] = Parse("""{"rowMode":"BandRow","headerMode":"OncePerParent","columns":[{"id":"id","label":"ONLY-HEADER","widthMm":50,"value":{"from":"Row","path":["id"],"nullPolicy":"Error"}}]}""");
        control["table"]!["repeatHeader"] = repeatHeader;
        var flow = ReportExpander.Expand(await new ReportPlanner(Resolver, new()).PlanAsync(Definition(doc), new(), default));
        var html = ReportHtmlComposer.Compose(flow, false);
        Assert.Contains("ONLY-HEADER", html, StringComparison.Ordinal);
        Assert.Contains(">ROW</td>", html, StringComparison.Ordinal);
        Assert.Contains("data-report-repeat-header=\"" + (repeatHeader ? "true" : "false") + "\"", html, StringComparison.Ordinal);
        if (repeatHeader) Assert.Contains("<thead>", html, StringComparison.Ordinal);
        else { Assert.DoesNotContain("<thead>", html, StringComparison.Ordinal); Assert.Contains("<tbody data-report-header=\"true\">", html, StringComparison.Ordinal); }
    }

    [Theory]
    [InlineData("true", "true", "✓")]
    [InlineData("false", "false", "")]
    [InlineData("null", "mixed", "−")]
    public async Task CheckBox_preserves_boolean_and_unknown_values_in_output(string json, string state, string glyph)
    {
        var doc = Report(new JsonArray(new JsonObject { ["id"] = "one", ["checked"] = JsonNode.Parse(json) }));
        var control = doc["bands"]![0]!["controls"]![0]!.AsObject();
        control["type"] = "CheckBox";
        control["value"]!["path"] = new JsonArray("checked");
        control["value"]!["nullPolicy"] = "Null";
        Assert.Empty(StudioDocuments.Validate("Report", doc));
        var flow = ReportExpander.Expand(await new ReportPlanner(Resolver, new()).PlanAsync(Definition(doc), new(), default));
        Assert.Equal(json, flow.Blocks.Single().Value?.ToJsonString() ?? "null");
        Assert.Contains("aria-checked=\"" + state + "\"", ReportHtmlComposer.Compose(flow, false), StringComparison.Ordinal);
        Assert.Contains(">" + glyph + "</span>", ReportHtmlComposer.Compose(flow, false), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckBox_rejects_string_values_and_missing_binding()
    {
        var doc = Report(new JsonArray(new JsonObject { ["id"] = "true" }));
        var control = doc["bands"]![0]!["controls"]![0]!.AsObject();
        control["type"] = "CheckBox";
        Assert.Equal("TYPE_MISMATCH", (await Assert.ThrowsAsync<StudioException>(() => new ReportPlanner(Resolver, new()).PlanAsync(Definition(doc), new(), default))).Code);
        control.Remove("value");
        Assert.Contains(StudioDocuments.Validate("Report", doc), d => d.Path.EndsWith(".value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Five_levels_use_independent_contexts_and_no_fixed_three_level_limit()
    {
        var rows = JsonNode.Parse("""[{"id":"A","children":[{"id":"B","children":[{"id":"C","children":[{"id":"D","children":[{"id":"E","children":[]}]}]}]}]},{"id":"F","children":[]}]""")!.AsArray();
        var doc = Report(rows);
        for (var i = 1; i < 5; i++)
        {
            var c = Field("value-" + i, "id"); c["value"]!["path"] = new JsonArray("id");
            doc["bands"]!.AsArray().Add(new JsonObject { ["id"] = "child-" + i, ["kind"] = "Detail", ["name"] = "Child", ["parentBandId"] = i == 1 ? "root-band" : "child-" + (i - 1), ["heightMm"] = 10, ["controls"] = new JsonArray(c), ["data"] = Parse("""{"mode":"ParentPath","path":["children"]}""") });
        }
        Assert.Empty(StudioDocuments.Validate("Report", doc));
        var plan = await new ReportPlanner(Resolver, new()).PlanAsync(Definition(doc), new(), default);
        var flow = ReportExpander.Expand(plan);
        Assert.Equal(TreeExpected, flow.Blocks.Select(b => StudioJson.Text(b.Value)));
        Assert.Equal(6, flow.ExpectedMarkers.Count);
    }
    [Fact]
    public void Layout_and_ancestry_reject_width_overflow_cycle_and_invalid_follow()
    {
        var doc = Report(new()); var band = doc["bands"]![0]!.AsObject(); var control = band["controls"]![0]!.AsObject();
        control["widthMm"] = 999; band["parentBandId"] = "root-band"; control["flow"] = Parse("""{"mode":"Follow","afterControlIds":["value"]}""");
        var diagnostics = StudioDocuments.Validate("Report", doc);
        Assert.Contains(diagnostics, d => d.Code == "REPORT_LAYOUT_UNSUPPORTED"); Assert.Contains(diagnostics, d => d.Path.EndsWith("parentBandId", StringComparison.Ordinal)); Assert.Contains(diagnostics, d => d.Code == "REPORT_LAYOUT_OVERLAP");
    }
    [Fact]
    public async Task Empty_label_does_not_execute_row_binding_and_missing_path_is_not_empty()
    {
        var doc = Report(new()); doc["bands"]![0]!["emptyBehavior"] = "Label"; doc["bands"]![0]!["emptyText"] = "None";
        var planner = new ReportPlanner(Resolver, new()); var plan = await planner.PlanAsync(Definition(doc), new(), default);
        Assert.Equal("None", StudioJson.Text(plan.Instances[0].Blocks.Single().Value)); Assert.Equal(0, plan.Instances[0].RowCount);
        doc["bands"]![0]!["data"]!["path"] = new JsonArray("missing");
        Assert.Equal("PATH_INVALID", (await Assert.ThrowsAsync<StudioException>(() => new ReportPlanner(Resolver, new()).PlanAsync(Definition(doc), new(), default))).Code);
    }
    [Fact]
    public async Task BandRow_does_not_iterate_again_and_collection_parent_row_is_band_row()
    {
        var doc = Report(JsonNode.Parse("""[{"id":"OP1","lines":[{"id":"M1"},{"id":"M2"},{"id":"M3"}]},{"id":"OP2","lines":[]}]""")!.AsArray());
        var controls = doc["bands"]![0]!["controls"]!.AsArray(); controls.Clear();
        JsonObject Table(string id, bool collection) { var c = Field(id, ""); c["type"] = "Table"; c["table"] = Parse("""{"rowMode":"BandRow","columns":[{"id":"id","label":"ID","widthMm":50,"value":{"from":"Row","path":["id"],"nullPolicy":"Error"}}],"headerMode":"OncePerParent","repeatHeader":true}"""); if (collection) { c["table"]!["rowMode"] = "Collection"; c["value"] = Parse("""{"from":"Row","path":["lines"],"nullPolicy":"Error"}"""); c["table"]!["columns"]!.AsArray().Add(Parse("""{"id":"parent","label":"Parent","widthMm":10,"value":{"from":"ParentRow","path":["id"],"nullPolicy":"Error"}}""")); c["widthMm"] = 80; } else c.Remove("value"); return c; }
        controls.Add(Table("ops", false)); controls.Add(Table("lines", true));
        var plan = await new ReportPlanner(Resolver, new()).PlanAsync(Definition(doc), new(), default);
        var blocks = plan.Instances[0].Blocks;
        Assert.Equal(2, blocks.Where(b => b.ControlId == "ops").Sum(b => b.Value!.AsArray().Count)); Assert.Equal(3, blocks.Where(b => b.ControlId == "lines").Sum(b => b.Value!.AsArray().Count));
        Assert.Equal("OP1", StudioJson.Text(blocks.First(b => b.ControlId == "lines").Value![0]![1]));
        Assert.True(blocks[0].Definition["showHeader"]!.GetValue<bool>()); Assert.False(blocks[2].Definition["showHeader"]!.GetValue<bool>());
        var html = ReportHtmlComposer.Compose(ReportExpander.Expand(plan), false);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Count(html, "<colgroup>"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(html, "<thead>"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(html, "<col style=\"width:10mm\">"));
    }
    [Fact]
    public async Task Multiple_template_rows_repeat_per_record_without_multiplying_source_count_or_headers()
    {
        var doc = Report(JsonNode.Parse("""[{"id":"A"},{"id":"B"},{"id":"C"},{"id":"D"},{"id":"E"},{"id":"F"}]""")!.AsArray());
        var control = doc["bands"]![0]!["controls"]![0]!.AsObject();
        control["type"] = "Table"; control.Remove("value");
        control["table"] = Parse("""{"rowMode":"BandRow","columns":[{"id":"id","label":"ID","widthMm":50,"value":{"from":"Row","path":["id"],"nullPolicy":"Error"}}],"headerMode":"OncePerParent","repeatHeader":true,"rows":[{"id":"main","cells":[{"columnId":"id","value":{"from":"Row","path":["id"],"nullPolicy":"Error"}}]},{"id":"note","cells":[{"columnId":"id","value":{"from":"Constant","nullPolicy":"Error","value":"Note"}}]}]}""");
        Assert.Empty(StudioDocuments.Validate("Report", doc));
        var plan = await new ReportPlanner(Resolver, new()).PlanAsync(Definition(doc), new(), default);
        Assert.Equal(6, plan.Instances[0].RowCount);
        Assert.Equal(12, plan.Instances[0].Blocks.Sum(b => b.Value!.AsArray().Count));
        Assert.Equal("A", StudioJson.Text(plan.Instances[0].Blocks[0].Value![0]![0]));
        Assert.Equal("Note", StudioJson.Text(plan.Instances[0].Blocks[0].Value![1]![0]));
        Assert.Single(plan.Instances[0].Blocks, b => b.Definition["showHeader"]!.GetValue<bool>());
        var html = ReportHtmlComposer.Compose(ReportExpander.Expand(plan), false);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(html, "<thead>"));
        Assert.Equal(6, System.Text.RegularExpressions.Regex.Count(html, ">Note</td>"));
        var table = control["table"]!.AsObject();
        Assert.Equal("Row", StudioJson.Text(ReportTableTemplates.Binding(table, table["columns"]![0]!.AsObject())["from"]));
        table["rows"]![1]!["cells"]![0]!["columnId"] = "deleted";
        Assert.Contains(StudioDocuments.Validate("Report", doc), d => d.Path.EndsWith("rows.cells", StringComparison.Ordinal));
        table["rows"] = new JsonArray();
        Assert.Contains(StudioDocuments.Validate("Report", doc), d => d.Path.EndsWith("table.rows", StringComparison.Ordinal));
    }
    [Fact]
    public async Task Collection_template_rows_keep_parent_bindings_and_column_order()
    {
        var doc = Report(JsonNode.Parse("""[{"id":"P","lines":[{"id":"A"},{"id":"B"}]}]""")!.AsArray());
        var control = doc["bands"]![0]!["controls"]![0]!.AsObject();
        control["type"] = "Table";
        control["value"] = Parse("""{"from":"Row","path":["lines"],"nullPolicy":"Error"}""");
        control["table"] = Parse("""{"rowMode":"Collection","columns":[{"id":"child","label":"Child","widthMm":25,"value":{"from":"Constant","nullPolicy":"Error","value":"legacy"}},{"id":"parent","label":"Parent","widthMm":25,"value":{"from":"Constant","nullPolicy":"Error","value":"legacy"}}],"headerMode":"OncePerParent","repeatHeader":true,"rows":[{"id":"main","cells":[{"columnId":"parent","value":{"from":"ParentRow","path":["id"],"nullPolicy":"Error"}},{"columnId":"child","value":{"from":"Row","path":["id"],"nullPolicy":"Error"}}]},{"id":"note","cells":[{"columnId":"child","value":{"from":"Constant","nullPolicy":"Error","value":"Note"}},{"columnId":"parent","value":{"from":"Constant","nullPolicy":"Error","value":""}}]}]}""");
        Assert.Empty(StudioDocuments.Validate("Report", doc));
        var plan = await new ReportPlanner(Resolver, new()).PlanAsync(Definition(doc), new(), default);
        var cells = plan.Instances[0].Blocks.Single().Value!.AsArray();
        Assert.Equal(4, cells.Count);
        Assert.Equal("A", StudioJson.Text(cells[0]![0])); Assert.Equal("P", StudioJson.Text(cells[0]![1]));
        Assert.Equal("B", StudioJson.Text(cells[2]![0])); Assert.Equal("Note", StudioJson.Text(cells[3]![0]));
    }
    [Fact]
    public async Task Electronic_subtree_repeat_maps_objects_without_requerying_entry_or_outputting_ancestors()
    {
        var child = Report(new()); child["bands"]![0]!["data"] = Parse("""{"mode":"Dataset","datasetRevisionNId":"never-query-entry","parameterMap":[]}""");
        var childField = child["bands"]![0]!["controls"]![0]!.AsObject(); childField["value"] = Parse("""{"from":"Row","path":["id"],"nullPolicy":"Error"}""");
        var doc = Parse("""{"schemaVersion":1,"mode":"ElectronicRecord","parameters":[],"instances":[{"instanceId":"orders","sourceType":"Constant","parameters":[],"value":[{"id":"WO-A","operations":[{"id":"OP1","runs":[{"id":"R1","lines":[{"id":"M1"}]},{"id":"R2","lines":[]}]},{"id":"OP2","runs":[]}]}]}],"assembly":{"root":{"mode":"RootPath","instanceId":"orders","path":[]},"operation":{"collection":{"mode":"ParentPath","path":["operations"]}},"rules":[{"id":"weigh","reportNId":"child","revisionNId":"rev1","parameterMap":[],"repeat":{"mode":"ParentPath","path":["runs"]},"required":false,"content":{"mode":"BandSubtree","entryBandId":"root-band","rowMap":[{"targetPath":["id"],"source":{"from":"Row","path":["id"],"nullPolicy":"Error"}},{"targetPath":["lines"],"source":{"from":"Row","path":["lines"],"nullPolicy":"Error"}}],"ancestorMap":[]}}]}}"""); doc["paper"] = Paper;
        var definition = new ReportDefinitionSnapshot("ReportBundle", doc, new Dictionary<string, ReportDefinitionSnapshot> { ["child/rev1"] = Definition(child) }, "fixture");
        var plan = await new ReportPlanner(Resolver, new()).PlanAsync(definition, new(), default);
        Assert.Equal(2, plan.Instances.Count(i => i.Decision == "Include")); Assert.Single(plan.Instances, i => i.Reason == "SkippedEmpty");
        Assert.Equal(RunsExpected, ReportExpander.Expand(plan).Blocks.Select(b => StudioJson.Text(b.Value)));
        Assert.IsType<JsonArray>(plan.Instances[0].ResolvedMappings["row"]!["lines"]);
        doc["assembly"]!["rules"]![0]!["required"] = true;
        Assert.Equal("DATA_REQUIRED", (await Assert.ThrowsAsync<StudioException>(() => new ReportPlanner(Resolver, new()).PlanAsync(definition, new(), default))).Code);
    }
    [Theory]
    [InlineData("Auto")]
    [InlineData("Page")]
    public async Task Electronic_page_break_marks_only_first_band_of_included_instances(string breakBefore)
    {
        var child = Report(new JsonArray(new JsonObject { ["id"] = "BODY" }));
        child["bands"]!.AsArray().Insert(0, Parse("""{"id":"title","kind":"ReportHeader","name":"Title","heightMm":5,"controls":[{"id":"heading","type":"Text","xMm":0,"yMm":0,"widthMm":50,"heightMm":5,"value":{"from":"Constant","value":"TITLE","nullPolicy":"Error"}}]}"""));
        var doc = Parse("""{"schemaVersion":1,"mode":"ElectronicRecord","parameters":[],"instances":[{"instanceId":"orders","sourceType":"Constant","parameters":[],"value":[{"id":"WO","operations":[{"id":"first","rows":[{}]},{"id":"empty","rows":[]},{"id":"second","rows":[{}]}]}]}],"assembly":{"root":{"mode":"RootPath","instanceId":"orders","path":[]},"operation":{"collection":{"mode":"ParentPath","path":["operations"]}},"rules":[{"id":"record","reportNId":"child","revisionNId":"rev1","parameterMap":[],"presence":{"mode":"ParentPath","path":["rows"]},"required":false,"content":{"mode":"WholeReport"}}]}}""");
        doc["paper"] = Paper; doc["assembly"]!["rules"]![0]!["breakBefore"] = breakBefore;
        var definition = new ReportDefinitionSnapshot("ReportBundle", doc, new Dictionary<string, ReportDefinitionSnapshot> { ["child/rev1"] = Definition(child) }, "fixture");
        var plan = await new ReportPlanner(Resolver, new()).PlanAsync(definition, new(), default);
        var included = plan.Instances.Where(instance => instance.Decision == "Include").ToArray();
        Assert.Equal(2, included.Length);
        Assert.Empty(Assert.Single(plan.Instances, instance => instance.Decision == "Skip").Blocks);
        foreach (var instance in included)
        {
            Assert.Equal("title", instance.Blocks[0].BandId);
            Assert.Equal(breakBefore == "Page" ? "Page" : null, StudioJson.Text(instance.Blocks[0].Definition["bandBreakBefore"]));
            Assert.All(instance.Blocks.Skip(1), block => Assert.Null(block.Definition["bandBreakBefore"]));
        }
        var html = System.Net.WebUtility.HtmlDecode(ReportHtmlComposer.Compose(ReportExpander.Expand(plan), false));
        Assert.Equal(breakBefore == "Page" ? 2 : 0, System.Text.RegularExpressions.Regex.Count(html, "\"breakBefore\":\"Page\""));
        Assert.Null(child["bands"]![0]!["controls"]![0]!["bandBreakBefore"]);
    }
    [Fact]
    public async Task One_observation_is_reused_and_failure_is_never_empty()
    {
        var calls = 0; var resolver = new ReportDataResolver((_, _, _) => { calls++; return Task.FromResult(new JsonObject { ["data"] = new JsonArray(), ["revisionNId"] = "rev", ["observedOn"] = "2026-10-28T00:00:00Z" }); });
        await resolver.DatasetAsync("rev", new(), default); await resolver.DatasetAsync("rev", new(), default); Assert.Equal(1, calls); Assert.Single(resolver.Manifest);
        var failed = new ReportDataResolver((_, _, _) => throw new StudioException("SOURCE_TIMEOUT", "timeout", 504));
        Assert.Equal("SOURCE_TIMEOUT", (await Assert.ThrowsAsync<StudioException>(() => failed.DatasetAsync("rev", new(), default))).Code);
    }
    [Fact]
    public async Task Paired_groups_keep_parent_scope_and_aggregate_before_pagination()
    {
        var doc = Report(JsonNode.Parse("""[{"id":"R1","category":"A","amount":"1"},{"id":"R2","category":"B","amount":"2"},{"id":"R3","category":"A","amount":"3"}]""")!.AsArray());
        var category = Field("category", ""); category["value"]!["path"] = new JsonArray("category");
        var sum = Field("sum", ""); sum.Remove("value"); sum["aggregate"] = Parse("""{"function":"Sum","scopeBandId":"root-band","field":{"from":"Row","path":["amount"],"nullPolicy":"Null"}}""");
        var header = Parse("""{"id":"group-head","kind":"GroupHeader","name":"Group","heightMm":5,"group":{"keys":[{"from":"Row","path":["category"],"nullPolicy":"Error"}],"pairedBandId":"group-foot"},"controls":[]}"""); header["controls"]!.AsArray().Add(category);
        var footer = Parse("""{"id":"group-foot","kind":"GroupFooter","name":"Subtotal","heightMm":5,"group":{"keys":[{"from":"Row","path":["category"],"nullPolicy":"Error"}],"pairedBandId":"group-head"},"controls":[]}"""); footer["controls"]!.AsArray().Add(sum);
        doc["bands"]!.AsArray().Insert(0, header); doc["bands"]!.AsArray().Add(footer);
        var plan = await new ReportPlanner(Resolver, new()).PlanAsync(Definition(doc), new(), default);
        Assert.Equal("A,R1,R3,4,B,R2,2", string.Join(',', plan.Instances[0].Blocks.Select(b => StudioJson.Text(b.Value))));
        Assert.Equal(3, plan.Instances[0].RowCount);
    }
    [Fact]
    public async Task Empty_scope_aggregates_are_count_zero_sum_zero_and_nullable_others()
    {
        var doc = Report(new()); var controls = new JsonArray();
        foreach (var function in new[] { "Count", "Sum", "Avg", "Min", "Max" })
        {
            var control = Field(function, ""); control.Remove("value"); control["aggregate"] = new JsonObject { ["function"] = function, ["scopeBandId"] = "root-band", ["field"] = Parse("""{"from":"Row","path":["amount"],"nullPolicy":"Null"}""") }; controls.Add(control);
        }
        doc["bands"]!.AsArray().Add(new JsonObject { ["id"] = "totals", ["kind"] = "ReportFooter", ["name"] = "Totals", ["heightMm"] = 10, ["controls"] = controls });
        var plan = await new ReportPlanner(Resolver, new()).PlanAsync(Definition(doc), new(), default); var values = plan.Instances[0].Blocks.Select(b => b.Value).ToArray();
        Assert.Equal("0", StudioJson.Text(values[0])); Assert.Equal("0", StudioJson.Text(values[1])); Assert.All(values.Skip(2), Assert.Null);
    }
    [Fact]
    public void Xlsx_validation_keeps_original_zip_bytes_and_rejects_macro_external_and_invalid_packages()
    {
        using var valid = Package(); var before = valid.ToArray(); StudioWorkbooks.ValidateXlsx(valid); Assert.Equal(before, valid.ToArray());
        using var external = Package("xl/externalLinks/externalLink1.xml"); Assert.Throws<StudioException>(() => StudioWorkbooks.ValidateXlsx(external));
        using var macros = Package("xl/vbaProject.bin"); Assert.Throws<StudioException>(() => StudioWorkbooks.ValidateXlsx(macros));
        using var invalid = new MemoryStream(new byte[] { 1, 2, 3 }); Assert.Throws<StudioException>(() => StudioWorkbooks.ValidateXlsx(invalid));
    }
    [Fact]
    public async Task Simple_parts_pin_each_paper_and_print_numbering_without_mutating_child_definition()
    {
        var child = Report(JsonNode.Parse("""[{"id":"A"}]""")!.AsArray());
        var doc = Parse("""{"schemaVersion":1,"mode":"Simple","parameters":[],"instances":[],"items":[{"id":"one","reportNId":"child","revisionNId":"rev1","parameterMap":[],"enabled":true,"required":true,"startOnNewPage":true}],"pageNumberMode":"Global"}""");
        var definition = new ReportDefinitionSnapshot("ReportBundle", doc, new Dictionary<string, ReportDefinitionSnapshot> { ["child/rev1"] = Definition(child) }, "fixture");
        var flow = ReportExpander.Expand(await new ReportPlanner(Resolver, new()).PlanAsync(definition, new(), default));
        Assert.Equal("one", StudioJson.Text(flow.Blocks[0].Definition["paginationPart"]?["key"]));
        Assert.Equal("Global", StudioJson.Text(flow.Blocks[0].Definition["paginationPart"]?["pageNumberMode"]));
        Assert.Null(child["bands"]![0]!["controls"]![0]!["paginationPart"]);
        Assert.Contains("data-report-part=\"one\"", ReportHtmlComposer.Compose(flow, false), StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Query_sort_applies_source_binding_path_and_null_placement_to_all_generated_rows(bool explicitRows)
    {
        var doc = Report(JsonNode.Parse("""[{"id":"two","amount":2},{"id":"empty","amount":null},{"id":"one","amount":1}]""")!.AsArray());
        doc["mode"] = "Query";
        var table = Field("table", ""); table["type"] = "Table"; table.Remove("value"); table["table"] = Parse("""{"rowMode":"BandRow","columns":[{"id":"quantity","label":"Quantity","widthMm":40,"value":{"from":"Row","path":["amount"],"nullPolicy":"Null"}}],"headerMode":"OncePerParent","repeatHeader":true}""");
        doc["bands"]![0]!["controls"] = new JsonArray(table);
        doc["query"] = Parse("""{"instanceId":"root","filters":[],"resultBandId":"root-band","columns":[{"controlId":"table","columnId":"quantity","visible":true,"sortable":true}],"defaultSort":[],"defaultPageSize":20}""");
        if (explicitRows)
        {
            var definition = table["table"]!.AsObject();
            definition["rows"] = new JsonArray(new JsonObject { ["id"] = "main", ["cells"] = new JsonArray(new JsonObject { ["columnId"] = "quantity", ["value"] = definition["columns"]![0]!["value"]!.DeepClone() }) });
            definition["columns"]![0]!["value"] = Parse("""{"from":"Constant","value":"legacy","nullPolicy":"Error"}""");
        }
        var sorted = ReportDesign.ApplyQuerySort(doc, JsonNode.Parse("""[{"fieldId":"quantity","direction":"Desc","nulls":"Last"}]""")!.AsArray());
        var flow = ReportExpander.Expand(await new ReportPlanner(Resolver, new()).PlanAsync(Definition(sorted), new(), default));
        Assert.Equal(new string?[] { "2", "1", null }, flow.Blocks.Select(block => block.Value![0]![0]?.ToJsonString()));
        Assert.Null(doc["bands"]![0]!["sort"]);
        Assert.Throws<StudioException>(() => ReportDesign.ApplyQuerySort(doc, JsonNode.Parse("""[{"fieldId":"hidden","direction":"Asc"}]""")!.AsArray()));
    }
    [Fact]
    public async Task Query_and_generated_report_share_default_empty_and_invalid_sort_semantics()
    {
        await using var fixture = await StudioTestContext.CreateAsync(); fixture.Profile.InstalledModules = ["LowCode", "Report"];
        var datasets = new DatasetExecution(fixture.Store, fixture.Publishing, fixture.Authorization, fixture.SourceExecutor, new(), TimeProvider.System);
        var design = new ReportDesign(fixture.Store, fixture.Authorization, fixture.Publishing, datasets);
        var doc = Report(JsonNode.Parse("""[{"id":"one","amount":1},{"id":"two","amount":2}]""")!.AsArray()); doc["mode"] = "Query";
        var table = doc["bands"]![0]!["controls"]![0]!.AsObject(); table["type"] = "Table"; table.Remove("value");
        table["table"] = Parse("""{"columns":[{"id":"quantity","label":"Quantity","widthMm":50,"value":{"from":"Row","path":["amount"],"nullPolicy":"Error"}}],"headerMode":"Hidden"}""");
        doc["query"] = Parse("""{"instanceId":"root","filters":[],"resultBandId":"root-band","columns":[{"controlId":"value","columnId":"quantity","visible":true,"sortable":true}],"defaultSort":[{"fieldId":"quantity","direction":"Desc","nulls":"Last"}],"defaultPageSize":20}""");
        foreach (var sort in new JsonArray?[] { null, new() })
        {
            var expected = sort is null ? new[] { "2", "1" } : new[] { "1", "2" };
            var query = await design.QueryAsync(fixture.Principal, null, new(null, new(), Sort: sort, Document: doc), default);
            Assert.Equal(expected, query["items"]!.AsArray().Select(row => row!["quantity"]!.ToJsonString()));
            var acceptedSort = ReportDesign.ResolveSort(doc, sort);
            var sorted = ReportDesign.ApplyQuerySort(doc, acceptedSort);
            var flow = ReportExpander.Expand(await new ReportPlanner(Resolver, new()).PlanAsync(Definition(sorted), new(), default));
            Assert.Equal(expected, flow.Blocks.Select(block => block.Value![0]![0]!.ToJsonString()));
        }
        // Bundle children use the same default as direct preview and a new formal run.
        var bundle = Parse("""{"schemaVersion":1,"mode":"Simple","parameters":[],"instances":[],"items":[{"id":"one","reportNId":"child","revisionNId":"rev1","parameterMap":[],"enabled":true,"required":true,"startOnNewPage":true}],"pageNumberMode":"Global"}""");
        var bundleFlow = ReportExpander.Expand(await new ReportPlanner(Resolver, new()).PlanAsync(new("ReportBundle", bundle, new Dictionary<string, ReportDefinitionSnapshot> { ["child/rev1"] = Definition(doc) }, "fixture"), new(), default));
        Assert.Collection(bundleFlow.Blocks, block => Assert.Equal("2", block.Value![0]![0]!.ToJsonString()), block => Assert.Equal("1", block.Value![0]![0]!.ToJsonString()));
        Assert.Null(doc["bands"]![0]!["sort"]);
        var existing = ReportDesign.ApplyQuerySort(doc, null);
        Assert.Empty(ReportDesign.ApplyQuerySort(existing, new())["bands"]![0]!["sort"]!.AsArray());
        foreach (var json in new[] { "[1]", "[{\"fieldId\":\"quantity\",\"direction\":\"Sideways\"}]", "[{\"fieldId\":\"quantity\",\"nulls\":\"Random\"}]", "[{\"fieldId\":\"quantity\"},{\"fieldId\":\"quantity\"}]", "[{\"fieldId\":\"hidden\"}]" })
        {
            var sort = JsonNode.Parse(json)!.AsArray();
            var reportError = Assert.Throws<StudioException>(() => ReportDesign.ApplyQuerySort(doc, sort));
            var queryError = await Assert.ThrowsAsync<StudioException>(() => design.QueryAsync(fixture.Principal, null, new(null, new(), Sort: sort, Document: doc), default));
            Assert.Equal(reportError.Code, queryError.Code);
        }
    }
    [Fact]
    public async Task Repeated_explicit_child_draft_is_frozen_once_and_reused_by_every_bundle_item()
    {
        await using var fixture = await StudioTestContext.CreateAsync(); fixture.Profile.InstalledModules = ["LowCode", "Report"];
        var datasets = new DatasetExecution(fixture.Store, fixture.Publishing, fixture.Authorization, fixture.SourceExecutor, new(), TimeProvider.System);
        var design = new ReportDesign(fixture.Store, fixture.Authorization, fixture.Publishing, datasets);
        var child = Report(new JsonArray(new JsonObject { ["id"] = "REUSED", ["payload"] = new string('x', 900_000) }));
        var resource = await fixture.Publishing.CreateAsync(fixture.Principal, new("reused-child", "Report", "Repeated child", null, null, child), "create-reused-child", default);
        var bundle = Parse("""{"schemaVersion":1,"mode":"Simple","parameters":[],"instances":[],"items":[],"pageNumberMode":"Global"}""");
        for (var index = 0; index < 10; index++) bundle["items"]!.AsArray().Add(new JsonObject { ["id"] = "item-" + index, ["reportNId"] = resource.NId, ["parameterMap"] = new JsonArray(), ["enabled"] = true, ["required"] = true, ["startOnNewPage"] = true });
        var frozen = await design.FreezeAsync(fixture.Principal, new("ReportBundle", null, bundle, new(), DraftDependencies: [new(resource.NId, resource.OptimisticVersion, resource.ConcurrencyVersion)]), default);
        var dependency = Assert.Single(frozen.Frozen["dependencySnapshots"]!.AsArray());
        Assert.Equal(resource.NId, StudioJson.Text(dependency!["resourceNId"]));
        Assert.Single(frozen.Definition.Dependencies);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(frozen.Frozen.ToJsonString()) < 2 * 1024 * 1024);
        Assert.All(frozen.Definition.Document["items"]!.AsArray(), item => Assert.Equal(StudioJson.Text(dependency["revisionNId"]), StudioJson.Text(item!["revisionNId"])));
        Assert.All(bundle["items"]!.AsArray(), item => Assert.Null(item!["revisionNId"]));
        var plan = await new ReportPlanner(Resolver, new()).PlanAsync(frozen.Definition, frozen.Parameters, default);
        Assert.Equal(10, plan.Instances.Count(instance => instance.Decision == "Include"));
        Assert.All(ReportExpander.Expand(plan).Blocks, block => Assert.Equal("REUSED", StudioJson.Text(block.Value)));
    }
    [Fact]
    public async Task Shared_xlsx_writer_produces_real_xml_preserves_spaces_and_never_executes_formula_text()
    {
        using var memory = new MemoryStream();
        await using (var writer = await IndustrialPlatform.RuntimeSupport.Exports.StreamingXlsxWriter.CreateAsync(memory, default)) await writer.WriteRowAsync(["  填写值  ", "=SUM(A1:A2)", "😀"], default);
        memory.Position = 0; StudioWorkbooks.ValidateXlsx(memory);
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = await reader.ReadToEndAsync();
        Assert.Contains("xml:space=\"preserve\"", xml, StringComparison.Ordinal); Assert.Contains("=SUM(A1:A2)", xml, StringComparison.Ordinal); Assert.DoesNotContain("<f>", xml, StringComparison.Ordinal); Assert.Contains("😀", xml, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Snapshot_restore_preserves_observed_data_without_querying_and_rejects_unrecorded_queries()
    {
        var captured = new ReportDataResolver((revision, parameters, token) => Task.FromResult(Parse("""{"revisionNId":"r1","data":[{"id":"old"}],"observedOn":"2026-10-28T00:00:00Z","receivedOn":"2026-10-28T00:00:01Z","truncated":false}""")));
        await captured.DatasetAsync("r1", new(), default);
        var calls = 0; var restored = new ReportDataResolver((revision, parameters, token) => { calls++; throw new InvalidOperationException("Snapshot replay must not call a source."); });
        restored.RestoreSnapshot(captured.SnapshotDocument);
        Assert.Equal("old", StudioJson.Text((await restored.DatasetAsync("r1", new(), default))![0]!["id"]));
        Assert.Equal("REPORT_SNAPSHOT_MISSING", (await Assert.ThrowsAsync<StudioException>(() => restored.DatasetAsync("r2", new(), default))).Code);
        Assert.Equal(0, calls); Assert.Equal(captured.Manifest.ToJsonString(), restored.Manifest.ToJsonString());
    }
    internal static MemoryStream Package(string? extra = null)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open())) writer.Write("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            using (var writer = new StreamWriter(zip.CreateEntry("xl/workbook.xml").Open())) writer.Write("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Record\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            using (var writer = new StreamWriter(zip.CreateEntry("_rels/.rels").Open())) writer.Write("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            using (var writer = new StreamWriter(zip.CreateEntry("xl/_rels/workbook.xml.rels").Open())) writer.Write("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            using (var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open())) writer.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\"><v>2</v></c><c r=\"B1\"><f>A1*3</f><v>6</v></c></row></sheetData><mergeCells count=\"1\"><mergeCell ref=\"A2:B2\"/></mergeCells></worksheet>");
            if (extra is not null) { using var writer = new StreamWriter(zip.CreateEntry(extra).Open()); writer.Write("x"); }
        }
        memory.Position = 0; return memory;
    }
}
