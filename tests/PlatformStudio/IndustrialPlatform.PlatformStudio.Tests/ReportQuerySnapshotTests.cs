using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;
namespace IndustrialPlatform.PlatformStudio.Tests;
public sealed class ReportQuerySnapshotTests
{
    [Fact]
    public async Task Export_uses_only_frozen_rows_and_added_columns_without_mutating_source()
    {
        var original = ReportPlanningTests.Report(new JsonArray(new JsonObject { ["id"] = "original" })); original["mode"] = "Query";
        var before = original.ToJsonString();
        var snapshot = JsonNode.Parse("""{"columns":["名称","临时数量","公式列"],"rows":[["edited",777,1554],["other",10,20]]}""")!.AsObject();
        var document = ReportQuerySnapshot.Document(original, snapshot);
        var resolver = new ReportDataResolver((_, _, _) => throw new InvalidOperationException("Must not read sources"));
        var plan = await new ReportPlanner(resolver, new()).PlanAsync(new("Report", document, new Dictionary<string, ReportDefinitionSnapshot>(), "snapshot"), new(), default);
        var html = ReportHtmlComposer.Compose(ReportExpander.Expand(plan), false);
        Assert.Equal(2, Assert.Single(plan.Instances).RowCount);
        Assert.Contains("1554", html); Assert.Contains("edited", html); Assert.DoesNotContain("original", html);
        Assert.Equal(before, original.ToJsonString());
    }
    [Theory]
    [InlineData(25, 3)]
    [InlineData(60, 8)]
    public async Task Paper_groups_repeat_identifier_preserve_values_and_start_new_pages(int count, int groups)
    {
        var original = ReportPlanningTests.Report([]); original["mode"] = "Query";
        var snapshot = new JsonObject { ["columns"] = new JsonArray(Enumerable.Range(0, count).Select(c => (JsonNode)JsonValue.Create("C" + c)!).ToArray()), ["rows"] = new JsonArray(new JsonArray(Enumerable.Range(0, count).Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()), new JsonArray(Enumerable.Range(0, count).Select(c => (JsonNode)JsonValue.Create(1000 + c)!).ToArray())) };
        var document = ReportQuerySnapshot.Document(original, snapshot, "Pdf");
        Assert.Equal("Landscape", document["paper"]!["orientation"]!.GetValue<string>());
        var resolver = new ReportDataResolver((_, _, _) => throw new InvalidOperationException());
        var plan = await new ReportPlanner(resolver, new()).PlanAsync(new("Report", document, new Dictionary<string, ReportDefinitionSnapshot>(), "paper"), new(), default);
        var full = ReportExpander.Expand(plan);
        var paged = ReportQuerySnapshot.PaginateColumns(full, "Pdf");
        if (Environment.GetEnvironmentVariable("QUERY_PAPER_EVIDENCE") is { Length: > 0 } evidence)
        {
            Directory.CreateDirectory(evidence);
            await File.WriteAllTextAsync(Path.Combine(evidence, "paper-" + count + ".json"), new JsonObject { ["html"] = ReportHtmlComposer.Compose(paged, false), ["css"] = ReportHtmlComposer.Css, ["paper"] = document["paper"]!.DeepClone(), ["format"] = "Pdf", ["preview"] = false, ["expectedMarkers"] = new JsonArray(paged.ExpectedMarkers.Select(m => (JsonNode)JsonValue.Create(m)!).ToArray()) }.ToJsonString());
        }
        Assert.Equal(groups * 2, paged.Blocks.Count);
        Assert.Equal(groups - 1, paged.Blocks.Count(b => StudioJson.Text(b.Definition["bandBreakBefore"]) == "Page"));
        Assert.Equal(paged.Blocks.Count, paged.ExpectedMarkers.Distinct().Count());
        var seen = new List<int>();
        for (var g = 0; g < groups; g++)
        {
            var first = paged.Blocks[g * 2]; var second = paged.Blocks[g * 2 + 1];
            Assert.Equal(0, first.Value![0]![0]!.GetValue<int>());
            Assert.Equal(1000, second.Value![0]![0]!.GetValue<int>());
            Assert.InRange(first.Definition["table"]!["columns"]!.AsArray().Count, 2, 9);
            seen.AddRange(first.Value![0]!.AsArray().Skip(1).Select(v => v!.GetValue<int>()));
        }
        Assert.Equal(Enumerable.Range(1, count - 1), seen);
        Assert.Same(full, ReportQuerySnapshot.PaginateColumns(full, "Xlsx"));
        Assert.Equal(count, full.Blocks[0].Definition["table"]!["columns"]!.AsArray().Count);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(60)]
    public async Task Wide_snapshot_fits_the_paper_without_dropping_columns(int count)
    {
        var original = ReportPlanningTests.Report([]); original["mode"] = "Query";
        var snapshot = new JsonObject { ["columns"] = new JsonArray(Enumerable.Range(0, count).Select(c => (JsonNode)JsonValue.Create("Column " + c)!).ToArray()), ["rows"] = new JsonArray(new JsonArray(Enumerable.Range(0, count).Select(c => (JsonNode)JsonValue.Create(c)!).ToArray())) };
        var document = ReportQuerySnapshot.Document(original, snapshot);
        Assert.DoesNotContain(StudioDocuments.Validate("Report", document), d => d.Severity == "Error");
        var resolver = new ReportDataResolver((_, _, _) => throw new InvalidOperationException());
        var plan = await new ReportPlanner(resolver, new()).PlanAsync(new("Report", document, new Dictionary<string, ReportDefinitionSnapshot>(), "wide"), new(), default);
        var html = ReportHtmlComposer.Compose(ReportExpander.Expand(plan), false);
        Assert.Contains("Column " + (count - 1), html);
    }
    [Theory]
    [InlineData("""{"columns":["x"],"rows":[[{}]]}""")]
    [InlineData("""{"columns":["x"],"rows":[[1,2]]}""")]
    [InlineData("""{"columns":[],"rows":[]}""")]
    public void Rejects_invalid_snapshot(string json) => Assert.Throws<StudioException>(() => ReportQuerySnapshot.Validate(JsonNode.Parse(json)!.AsObject()));
}
