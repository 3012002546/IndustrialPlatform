using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;
namespace IndustrialPlatform.PlatformStudio.Tests;
public sealed class ReportDocumentControlsTests
{
    internal static byte[] PdfFixture()
    {
        const string stream = "BT /F1 18 Tf 50 700 Td (R4-SOURCE-PDF) Tj ET";
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", "<< /Length " + stream.Length + " >>\nstream\n" + stream + "\nendstream"];
        var text = new System.Text.StringBuilder("%PDF-1.4\n");var offsets=new List<int>();
        for(var i=0;i<objects.Length;i++){offsets.Add(text.Length);text.Append(i+1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");}
        var xref=text.Length;text.Append("xref\n0 6\n0000000000 65535 f \n");foreach(var offset in offsets)text.Append(offset.ToString("D10",System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");text.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");return System.Text.Encoding.ASCII.GetBytes(text.ToString());
    }
    private static readonly string[] ExpectedRows = ["A1", "A2", "B1"];
    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
    internal static JsonObject SubReport(string resource = "child") => Parse("""{"id":"sub","type":"SubReport","xMm":0,"yMm":0,"widthMm":180,"heightMm":10,"canGrow":true,"subReport":{"reportNId":"child","revisionNId":"v1","parameterMap":[],"instanceMap":[{"target":"root","source":{"from":"Row","path":["items"],"nullPolicy":"Error"}}]}}""").TapReport(resource);
    private static ReportDefinitionSnapshot Definition(JsonObject doc, IReadOnlyDictionary<string, ReportDefinitionSnapshot>? deps = null, string owner = "parent") => new("Report", doc, deps ?? new Dictionary<string, ReportDefinitionSnapshot>(), "r4", owner);
    [Fact]
    public async Task Subreport_reuses_snapshot_continuously_with_unique_markers_and_ignores_child_page_bands()
    {
        var parent = ReportPlanningTests.Report([]); var child = ReportPlanningTests.Report([]);
        parent["instances"]![0] = Parse("""{"instanceId":"root","sourceType":"Dataset","datasetNId":"orders","revisionNId":"orders-v1","parameters":[]}""");
        child["instances"]![0] = Parse("""{"instanceId":"root","sourceType":"Dataset","datasetNId":"unused","revisionNId":"unused-v1","parameters":[]}""");
        parent["bands"]![0]!["controls"] = new JsonArray(SubReport());
        var header = child["bands"]![0]!.DeepClone().AsObject();header["id"]="header";header["kind"]="PageHeader";header.Remove("data");header["controls"]![0]!["value"]=Parse("""{"from":"Constant","value":"CHILD-HEADER","nullPolicy":"Error"}""");child["bands"]!.AsArray().Insert(0,header);
        Assert.Empty(StudioDocuments.Validate("Report", parent));
        var calls=0;var resolver=new ReportDataResolver((revision,_,_)=>{Assert.Equal("orders-v1",revision);calls++;return Task.FromResult(Parse("""{"data":[{"items":[{"id":"A1"},{"id":"A2"}]},{"items":[{"id":"B1"}]}]}"""));});
        var definition=Definition(parent,new Dictionary<string,ReportDefinitionSnapshot>{{"child/v1",Definition(child,owner:"child")}});
        var flow=ReportExpander.Expand(await new ReportPlanner(resolver,new()).PlanAsync(definition,new(),default));
        Assert.Equal(1,calls);Assert.Equal(ExpectedRows,flow.Blocks.Select(b=>StudioJson.Text(b.Value)));Assert.Equal(3,flow.ExpectedMarkers.Distinct().Count());Assert.All(flow.Blocks,b=>Assert.False(b.Repeat));
        var replay=new ReportDataResolver((_,_,_)=>throw new InvalidOperationException("No query during replay"));replay.RestoreSnapshot(resolver.SnapshotDocument);
        Assert.Equal(ReportHtmlComposer.Compose(flow,false),ReportHtmlComposer.Compose(ReportExpander.Expand(await new ReportPlanner(replay,new()).PlanAsync(definition,new(),default)),false));
    }
    [Fact]
    public async Task Data_driven_nested_reports_query_complete_orders_once_and_skip_empty_rows()
    {
        var parent=ReportPlanningTests.Report([]);parent["bands"]![0]!["controls"]=new JsonArray(SubReport());
        parent["instances"]![0]=Parse("""{"instanceId":"root","sourceType":"Dataset","datasetNId":"mapped","revisionNId":"mapped-v1","parameters":[]}""");
        var child=ReportPlanningTests.Report([]);child["instances"]![0]=parent["instances"]![0]!.DeepClone();
        var bundle=Parse("""{"schemaVersion":1,"mode":"ElectronicRecord","parameters":[],"instances":[{"instanceId":"orders","sourceType":"Dataset","datasetNId":"orders","revisionNId":"orders-v1","parameters":[]}],"assembly":{"stitchMode":"DataDriven","root":{"mode":"RootPath","instanceId":"orders","path":[]},"operation":{"collection":{"mode":"ParentPath","path":["ops"]}},"templates":[{"id":"nested","scope":"Operation","reportNId":"parent","revisionNId":"v1","required":false,"parameterMap":[],"instanceMap":[{"target":"root","source":{"from":"Row","path":["records"],"nullPolicy":"Error"}}]}]}}""");bundle["paper"]=parent["paper"]!.DeepClone();
        var calls=0;var data=new ReportDataResolver((revision,_,_)=>{Assert.Equal("orders-v1",revision);calls++;return Task.FromResult(Parse("""{"data":[{"id":"WO","ops":[{"id":"OP1","records":[{"items":[{"id":"DETAIL-ONE"}]}]},{"id":"OP2","records":[{"items":[]}]}]}]}"""));});
        var deps=new Dictionary<string,ReportDefinitionSnapshot>{{"parent/v1",Definition(parent)},{"child/v1",Definition(child)}};
        var definition=new ReportDefinitionSnapshot("ReportBundle",bundle,deps,"nested");var plan=await new ReportPlanner(data,new()).PlanAsync(definition,new(),default);
        Assert.Equal(1,calls);Assert.Single(plan.Instances,i=>i.Decision=="Include");Assert.Single(plan.Instances,i=>i.Decision=="Skip");Assert.Equal("DETAIL-ONE",StudioJson.Text(Assert.Single(ReportExpander.Expand(plan).Blocks).Value));
        parent["bands"]![0]!["controls"]![0]!["subReport"]!["instanceMap"]=new JsonArray();
        Assert.Equal("REPORT_SUBREPORT_MAPPING_REQUIRED",(await Assert.ThrowsAsync<StudioException>(()=>new ReportPlanner(data,new()).PlanAsync(definition,new(),default))).Code);Assert.Equal(1,calls);
    }
    [Fact]
    public async Task Nested_query_uses_its_default_sort_and_preserves_both_outline_titles()
    {
        var parent=ReportPlanningTests.Report(new JsonArray(Parse("""{"items":[{"id":"A"},{"id":"B"}]}""")));var sub=SubReport();sub["outline"]=Parse("""{"title":{"from":"Constant","value":"Parent title","nullPolicy":"Error"},"level":1}""");parent["bands"]![0]!["controls"]=new JsonArray(sub);
        var child=ReportPlanningTests.Report([]);child["mode"]="Query";child["query"]=Parse("""{"instanceId":"root","filters":[],"resultBandId":"root-band","columns":[{"controlId":"table","columnId":"id","visible":true,"sortable":true}],"defaultSort":[{"fieldId":"id","direction":"Desc","nulls":"Last"}],"defaultPageSize":20}""");
        child["bands"]![0]!["controls"]=new JsonArray(Parse("""{"id":"table","type":"Table","xMm":0,"yMm":0,"widthMm":180,"heightMm":10,"canGrow":true,"table":{"rowMode":"BandRow","columns":[{"id":"id","label":"ID","widthMm":180,"value":{"from":"Row","path":["id"],"nullPolicy":"Error"}}],"headerMode":"Hidden","repeatHeader":true},"outline":{"title":{"from":"Row","path":["id"],"nullPolicy":"Error"},"level":2}}"""));
        var original=child.ToJsonString();var definition=Definition(parent,new Dictionary<string,ReportDefinitionSnapshot>{{"child/v1",Definition(child)}});
        var flow=ReportExpander.Expand(await new ReportPlanner(new ReportDataResolver((_,_,_)=>throw new InvalidOperationException()),new()).PlanAsync(definition,new(),default));
        Assert.Equal("B",StudioJson.Text(flow.Blocks[0].Value![0]![0]));Assert.Equal("A",StudioJson.Text(flow.Blocks[1].Value![0]![0]));Assert.Equal(original,child.ToJsonString());
        Assert.Equal("Parent title",StudioJson.Text(flow.Blocks[0].Definition["inheritedOutlines"]![0]!["title"]));Assert.Equal("B",StudioJson.Text(flow.Blocks[0].Definition["resolvedOutline"]!["title"]));Assert.Equal(3,System.Text.RegularExpressions.Regex.Count(ReportHtmlComposer.Compose(flow,false),"data-report-outline="));
    }
    [Fact]
    public async Task Missing_revision_cycles_and_parent_width_mismatch_fail_before_render()
    {
        var parent=ReportPlanningTests.Report(new JsonArray(Parse("""{"items":[{"id":"X"}]}""")));parent["bands"]![0]!["controls"]=new JsonArray(SubReport());
        var resolver=new ReportDataResolver((_,_,_)=>throw new InvalidOperationException());
        Assert.Equal("DEPENDENCY_CHANGED",(await Assert.ThrowsAsync<StudioException>(()=>new ReportPlanner(resolver,new()).PlanAsync(Definition(parent),new(),default))).Code);
        var cyclic=parent.DeepClone().AsObject();cyclic["bands"]![0]!["controls"]![0]!["subReport"]!["instanceMap"]=new JsonArray();
        var deps=new Dictionary<string,ReportDefinitionSnapshot>{{"child/v1",Definition(cyclic)}};
        Assert.Equal("REPORT_SUBREPORT_CYCLE",(await Assert.ThrowsAsync<StudioException>(()=>new ReportPlanner(resolver,new()).PlanAsync(Definition(cyclic,deps),new(),default))).Code);
        var wide=ReportPlanningTests.Report(new JsonArray(Parse("""{"id":"W"}""")));wide["bands"]![0]!["controls"]![0]!["widthMm"]=190;
        Assert.Equal("REPORT_LAYOUT_UNSUPPORTED",(await Assert.ThrowsAsync<StudioException>(()=>new ReportPlanner(resolver,new()).PlanAsync(Definition(parent,new Dictionary<string,ReportDefinitionSnapshot>{{"child/v1",Definition(wide)}}),new(),default))).Code);
    }
    [Theory]
    [InlineData("PdfContent")]
    [InlineData("SubReport")]
    [InlineData("TableOfContents")]
    public void Document_controls_require_dedicated_non_repeating_bands(string type)
    {
        var doc=ReportPlanningTests.Report([]);var c=SubReport();c["type"]=type;
        if(type!="SubReport")c.Remove("subReport");if(type=="PdfContent")c["value"]=Parse("""{"from":"Constant","value":"pdf-a","nullPolicy":"Error"}""");if(type=="TableOfContents")c["toc"]=Parse("""{"title":"目录"}""");
        doc["bands"]![0]!["controls"]=new JsonArray(c);Assert.Empty(StudioDocuments.Validate("Report",doc));
        c["xMm"]=1;Assert.NotEmpty(StudioDocuments.Validate("Report",doc));c["xMm"]=0;doc["bands"]![0]!["kind"]="PageHeader";Assert.NotEmpty(StudioDocuments.Validate("Report",doc));
    }
    [Fact]
    public async Task Outline_binding_is_resolved_from_rows_and_encoded_in_inert_html()
    {
        var doc=ReportPlanningTests.Report(new JsonArray(Parse("""{"id":"<script>title</script>"}""")));doc["bands"]![0]!["controls"]![0]!["outline"]=Parse("""{"title":{"from":"Row","path":["id"],"nullPolicy":"Error"},"level":1}""");
        var flow=ReportExpander.Expand(await new ReportPlanner(new ReportDataResolver((_,_,_)=>throw new InvalidOperationException()),new()).PlanAsync(Definition(doc),new(),default));
        Assert.Equal("<script>title</script>",StudioJson.Text(flow.Blocks[0].Definition["resolvedOutline"]?["title"]));Assert.DoesNotContain("<script>",ReportHtmlComposer.Compose(flow,false),StringComparison.Ordinal);
    }
}
internal static class ReportDocumentTestHelpers
{
    internal static JsonObject TapReport(this JsonObject control,string resource){control["subReport"]!["reportNId"]=resource;return control;}
}
