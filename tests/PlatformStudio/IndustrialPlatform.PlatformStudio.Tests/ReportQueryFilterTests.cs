using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class ReportQueryFilterTests
{
    private static JsonArray Array(string json) => JsonNode.Parse(json)!.AsArray();
    private static JsonObject Document()
    {
        var rows = new JsonArray(Enumerable.Range(1, 320).Select(i => (JsonNode)new JsonObject { ["id"] = i, ["name"] = i > 200 ? "CMP" : "Other", ["amount"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture), ["day"] = i % 2 == 0 ? "2026-11-16T23:59:59+08:00" : "2026-11-15", ["valid"] = false }).ToArray());
        var doc = ReportPlanningTests.Report(rows); doc["mode"] = "Query";
        var columns = new JsonArray(); var queryColumns = new JsonArray(); var filters = new JsonArray();
        foreach (var (id, type) in new[] { ("id", "Int32"), ("name", "String"), ("amount", "Decimal"), ("day", "DateTime"), ("valid", "Boolean") })
        {
            columns.Add(new JsonObject { ["id"] = id, ["label"] = id, ["widthMm"] = 30, ["value"] = new JsonObject { ["from"] = "Row", ["path"] = new JsonArray(id), ["nullPolicy"] = "Null" } });
            queryColumns.Add(new JsonObject { ["controlId"] = "table", ["columnId"] = id, ["visible"] = id != "name", ["sortable"] = true, ["dataType"] = type });
            filters.Add(new JsonObject { ["fieldId"] = id, ["label"] = id, ["dataType"] = type });
        }
        doc["bands"]![0]!["controls"] = new JsonArray(new JsonObject { ["id"] = "table", ["type"] = "Table", ["xMm"] = 0, ["yMm"] = 0, ["widthMm"] = 150, ["heightMm"] = 8, ["canGrow"] = true, ["table"] = new JsonObject { ["rowMode"] = "BandRow", ["columns"] = columns, ["headerMode"] = "OncePerParent", ["repeatHeader"] = true } });
        doc["query"] = new JsonObject { ["instanceId"] = "root", ["resultBandId"] = "root-band", ["columns"] = queryColumns, ["filters"] = filters, ["defaultSort"] = new JsonArray(), ["defaultPageSize"] = 20 };
        return doc;
    }

    [Fact]
    public async Task Filters_all_rows_before_count_and_pagination_and_export_uses_same_predicate()
    {
        await using var fixture = await StudioTestContext.CreateAsync(); fixture.Profile.InstalledModules = ["Report"];
        var design = new ReportDesign(fixture.Store, fixture.Authorization, fixture.Publishing, new DatasetExecution(fixture.Store, fixture.Publishing, fixture.Authorization, fixture.SourceExecutor, new(), TimeProvider.System));
        var doc = Document(); var before = doc.ToJsonString();
        var filters = Array("""[{"fieldId":"name","value":"cmp"},{"fieldId":"amount","min":"210","max":"300"},{"fieldId":"day","min":"2026-11-16","max":"2026-11-16"},{"fieldId":"valid","value":false}]""");
        var result = await design.QueryAsync(fixture.Principal, null, new(null, new(), 2, 20, Document: doc, Filters: filters), default);
        Assert.Equal("46", result["totalCount"]!.GetValue<string>());
        Assert.Equal(20, result["items"]!.AsArray().Count);
        Assert.Equal(250, result["items"]![0]!["id"]!.GetValue<int>());
        Assert.Null(result["items"]![0]!["name"]);
        var export = ReportDesign.ApplyQuerySort(doc, null);
        var resolver = new ReportDataResolver((_, _, _) => throw new InvalidOperationException());
        var plan = await new ReportPlanner(resolver, new()).PlanAsync(new("Report", export, new Dictionary<string, ReportDefinitionSnapshot>(), "test", QueryFilters: ReportQueryFilters.Compile(doc, filters)), new(), default);
        var html = ReportHtmlComposer.Compose(ReportExpander.Expand(plan), false);
        Assert.Contains("300", html); Assert.DoesNotContain(">320<", html);
        Assert.Equal(46, Assert.Single(plan.Instances).RowCount);
        Assert.Equal(before, doc.ToJsonString());
        var all = await design.QueryAsync(fixture.Principal, null, new(null, new(), 1, 10000, Document: doc, All: true), default);
        Assert.Equal(320, all["items"]!.AsArray().Count);
        Assert.Equal(320, all["items"]![319]!["id"]!.GetValue<int>());
        var empty = await design.QueryAsync(fixture.Principal, null, new(null, new(), Document: doc, Filters: Array("""[{"fieldId":"amount","min":"999"}]""")), default);
        Assert.Equal("0", empty["totalCount"]!.GetValue<string>()); Assert.Empty(empty["items"]!.AsArray());
    }

    [Theory]
    [InlineData("[{\"fieldId\":\"unknown\",\"value\":\"x\"}]")]
    [InlineData("[{\"fieldId\":\"amount\",\"min\":\"bad\"}]")]
    [InlineData("[{\"fieldId\":\"amount\",\"min\":10,\"max\":2}]")]
    [InlineData("[{\"fieldId\":\"day\",\"min\":\"2026-02-30\"}]")]
    [InlineData("[{\"fieldId\":\"name\",\"value\":\"x\"},{\"fieldId\":\"name\",\"value\":\"y\"}]")]
    [InlineData("[{\"fieldId\":\"name\",\"value\":\"x\",\"path\":[\"secret\"]}]")]
    public void Rejects_invalid_or_unconfigured_filters(string json) => Assert.Throws<StudioException>(() => ReportQueryFilters.Resolve(Document(), Array(json)));
}
