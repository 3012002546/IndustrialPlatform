using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ExcelDataReader;
using W = DocumentFormat.OpenXml.Wordprocessing;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Api;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;
using IndustrialPlatform.RuntimeSupport.Jobs;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Application.Scheduler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class ReportEngineFactAttribute : FactAttribute
{
    public ReportEngineFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PF09_RENDERER_TEST_MANIFEST") is null || Environment.GetEnvironmentVariable("PF09_RENDERER_TEST_NODE") is null)
            Skip = "需要显式配置本机候选manifest与Node；未执行不计为引擎或Office验收通过。";
    }
}

public sealed class ReportEngineIntegrationTests
{
    private static readonly string[] ArchiveScenarios = new[] { "Pdf", "Html", "Xlsx", "Preview", "RenderSnapshot", "NewData", "AdvancedControls", "AdvancedXlsx", "DocumentControls", "Xls", "Docx", "Rtf", "Mht", "Text", "Csv", "Png", "Jpeg", "AdvancedDocx", "AdvancedRtf" };
    private static readonly string[] BundleModes = ["NormalBundle", "DataDrivenBundle"];
    private static readonly string[] BundleFormats = ["Pdf", "Html", "Xls", "Xlsx", "Docx", "Rtf", "Mht", "Text", "Csv", "Png", "Jpeg"];
    private static readonly string[] BundleBasicMarkers = ["BASIC-OP1", "BASIC-OP2"];
    private sealed class Identity : IStudioIdentity
    {
        public bool Revoked { get; set; }
        public StudioPrincipal Principal { get; } = new("tenant-engine", "engine-user", "test", "Engine fixture", [], StudioKinds.Permissions, ["factory-a"], "1", "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        public Task<StudioPrincipal> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Principal);
        public Task<StudioPrincipal> RefreshAsync(StudioPrincipal principal, CancellationToken cancellationToken) => Task.FromResult(Principal);
        public Task<string> CaptureExecutionEvidenceAsync(StudioPrincipal principal, CancellationToken cancellationToken)
            => Task.FromResult(JsonSerializer.Serialize(new { principal, sessionId = "engine-fixture-session" }, StudioCommands.JsonOptions));
        public Task<StudioPrincipal> RefreshExecutionAsync(string evidence, CancellationToken cancellationToken)
        {
            if (Revoked) throw new StudioException("STUDIO_UNAUTHENTICATED", "Session revoked", 401);
            Assert.True(evidence.Length > 128);
            var value = JsonNode.Parse(evidence)!;
            Assert.Equal("engine-fixture-session", value["sessionId"]!.GetValue<string>());
            Assert.Equal(Principal.SubjectNId, value["principal"]!["subjectNId"]!.GetValue<string>());
            return Task.FromResult(Principal);
        }
    }
    private static string HashFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    [ReportEngineFact]
    public async Task Pagination_honors_header_repeat_keep_with_next_and_keep_together()
    {
        var node = Path.GetFullPath(Environment.GetEnvironmentVariable("PF09_RENDERER_TEST_NODE")!);
        var manifest = Path.GetFullPath(Environment.GetEnvironmentVariable("PF09_RENDERER_TEST_MANIFEST")!);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repo.FullName, "tools", "pf09-render-report.mjs"))) repo = repo.Parent ?? throw new InvalidOperationException("Repository tools missing.");
        var script = Path.Combine(repo.FullName, "tools", "pf09-render-report.mjs");
        var root = Path.Combine(repo.FullName, ".run", "pf09-pagination-fixtures", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var completed = false;
        try
        {
            var profile = StudioInitializationTests.Profile(Path.Combine(root, "unused.db"));
            profile.Renderer = new() { Admitted = true, Command = node, CommandSha256 = HashFile(node), Script = script, ScriptSha256 = HashFile(script), EngineManifestPath = manifest, EngineManifestHash = HashFile(manifest), WorkingRoot = Path.Combine(root, "renderer"), TimeoutSeconds = 45 };
            var renderer = new ReportCommandRenderer(profile);
            static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
            static JsonObject Document(JsonArray bands) => new()
            {
                ["schemaVersion"] = 1, ["mode"] = "Fixed", ["parameters"] = new JsonArray(), ["instances"] = new JsonArray(),
                ["paper"] = Parse("""{"size":"Custom","widthMm":100,"heightMm":100,"orientation":"Portrait","margin":{"top":5,"right":5,"bottom":5,"left":5},"headerHeightMm":0,"footerHeightMm":0}"""),
                ["bands"] = bands, ["styles"] = new JsonArray(), ["assets"] = new JsonArray()
            };
            static JsonObject TextBand(string id, double height, string text) => new()
            {
                ["id"] = id, ["kind"] = "ReportHeader", ["name"] = id, ["heightMm"] = height,
                ["controls"] = new JsonArray(new JsonObject { ["id"] = id + "-text", ["type"] = "Text", ["xMm"] = 0, ["yMm"] = 0, ["widthMm"] = 80, ["heightMm"] = height, ["canGrow"] = true, ["value"] = new JsonObject { ["from"] = "Constant", ["value"] = text, ["nullPolicy"] = "Error" } })
            };
            async Task<string[]> Render(JsonObject document, string name)
            {
                var resolver = new ReportDataResolver((_, _, _) => throw new InvalidOperationException("Constant report must not query a source."));
                var definition = new ReportDefinitionSnapshot("Report", document, new Dictionary<string, ReportDefinitionSnapshot>(), "fixture");
                var flow = ReportExpander.Expand(await new ReportPlanner(resolver, new()).PlanAsync(definition, new(), default));
                ReportRenderedOutput output;
                try { output = await renderer.RenderAsync(name, flow, document["paper"]!.AsObject(), true, default, "Html"); }
                catch (StudioException exception)
                {
                    var receiptPath = Directory.EnumerateFiles(root, "renderer-stopped.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                    var receipt = receiptPath is null ? null : JsonNode.Parse(await File.ReadAllTextAsync(receiptPath));
                    var code = StudioJson.Text(receipt?["errorCode"]) ?? "MISSING_RECEIPT_ERROR_CODE";
                    var description = code switch
                    {
                        "REPORT_CELL_RANGE_MISMATCH" => "分页前后单元格文字范围不完整或重复",
                        "REPORT_LAYOUT_OVERFLOW" => "正文超出可打印区域",
                        "REPORT_LAYOUT_UNSPLITTABLE" => "没有可用的安全分页断点",
                        "REPORT_LAYOUT_WIDOW_ORPHAN" => "段落分页未保留最低行数",
                        "REPORT_MARKER_COVERAGE_MISMATCH" => "正文标记覆盖不完整",
                        _ => "查看保留的引擎回执和输入文件"
                    };
                    throw new InvalidOperationException($"Fixture={name}; StudioException.Code={exception.Code}; receipt.errorCode={code}; description={description}; rendererMessage={exception.Message}; retainedFixture={root}", exception);
                }
                var pages = new List<string>();
                foreach (var page in output.Result["pages"]!.AsArray().OfType<JsonObject>()) pages.Add(await File.ReadAllTextAsync(Path.Combine(output.Directory, StudioJson.RequiredString(page, "htmlFile"))));
                return pages.ToArray();
            }
            foreach (var repeat in new[] { false, true })
            {
                var band = Parse("""{"id":"detail","kind":"Detail","name":"Detail","heightMm":8,"data":{"mode":"RootPath","instanceId":"rows","path":[]},"controls":[{"id":"table","type":"Table","xMm":0,"yMm":0,"widthMm":60,"heightMm":8,"canGrow":true,"value":{"from":"Row","path":["lines"],"nullPolicy":"Error"},"table":{"rowMode":"Collection","headerMode":"OncePerParent","repeatHeader":true,"headerHeightMm":7,"columns":[{"id":"value","label":"CONTINUATION-HEADER","widthMm":60,"value":{"from":"Row","path":["value"],"nullPolicy":"Error"}}]}}]}""");
                band["controls"]![0]!["table"]!["repeatHeader"] = repeat;
                var document = Document(new JsonArray(band));
                var lines = new JsonArray(Enumerable.Range(0, 40).Select(index => (JsonNode)new JsonObject { ["value"] = "RECORD-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) }).ToArray());
                document["instances"]!.AsArray().Add(new JsonObject { ["instanceId"] = "rows", ["sourceType"] = "Constant", ["parameters"] = new JsonArray(), ["value"] = new JsonArray(new JsonObject { ["lines"] = lines }) });
                var pages = await Render(document, "repeat-header-" + repeat);
                Assert.True(pages.Length > 1);
                Assert.Contains("CONTINUATION-HEADER", pages[0], StringComparison.Ordinal);
                if (repeat) Assert.All(pages, page => Assert.Contains("CONTINUATION-HEADER", page, StringComparison.Ordinal));
                else
                {
                    Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(string.Concat(pages), "CONTINUATION-HEADER"));
                    Assert.All(pages.Skip(1), page => Assert.DoesNotContain("CONTINUATION-HEADER", page, StringComparison.Ordinal));
                }
                foreach (var index in Enumerable.Range(0, 40)) Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(string.Concat(pages), "RECORD-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)));
            }
            foreach (var keep in new[] { false, true })
            {
                var title = TextBand("title", 6, "KEEP-TITLE"); title["keepWithNext"] = keep;
                var pages = await Render(Document(new JsonArray(TextBand("filler", 82, "FILLER"), title, TextBand("following", 12, "FOLLOWING-CONTENT"))), "keep-next-" + keep);
                Assert.Equal(2, pages.Length);
                Assert.Contains("FOLLOWING-CONTENT", pages[1], StringComparison.Ordinal);
                Assert.Contains("KEEP-TITLE", pages[keep ? 1 : 0], StringComparison.Ordinal);
                Assert.DoesNotContain("KEEP-TITLE", pages[keep ? 0 : 1], StringComparison.Ordinal);
                var block = TextBand("block", 38, "BLOCK-FIRST\nline two\nline three\nline four\nline five\nline six\nline seven\nBLOCK-LAST");
                block["controls"]![0]!["keepTogether"] = keep;
                pages = await Render(Document(new JsonArray(TextBand("filler", 65, "FILLER"), block)), "keep-together-" + keep);
                Assert.Equal(2, pages.Length);
                Assert.Contains("BLOCK-LAST", pages[1], StringComparison.Ordinal);
                Assert.Contains("BLOCK-FIRST", pages[keep ? 1 : 0], StringComparison.Ordinal);
                Assert.DoesNotContain("BLOCK-FIRST", pages[keep ? 0 : 1], StringComparison.Ordinal);
            }
            // BandRow emits separate physical tables for one logical detail table.
            // Keep a multi-control title with its first row and repeat its header across those tables.
            foreach (var rowMode in new[] { "BandRow", "Collection" })
            foreach (var repeat in new[] { false, true })
            {
                var title = TextBand("row-title", 12, "BANDROW-TITLE"); title["keepWithNext"] = true;
                title["controls"]![0]!["widthMm"] = 50; title["controls"]![0]!["heightMm"] = 7;
                var caption = TextBand("caption", 7, "CONTEXT")["controls"]![0]!.DeepClone();
                caption["xMm"] = 52; caption["widthMm"] = 28; title["controls"]!.AsArray().Add(caption);
                var detail = Parse("""{"id":"rows","kind":"Detail","name":"Rows","heightMm":7,"data":{"mode":"RootPath","instanceId":"rows","path":[]},"controls":[{"id":"row-table","type":"Table","xMm":0,"yMm":0,"widthMm":80,"heightMm":7,"canGrow":true,"table":{"rowMode":"BandRow","columns":[{"id":"value","label":"ROW-HEADER","widthMm":80,"value":{"from":"Row","path":["value"],"nullPolicy":"Error"}}],"headerMode":"OncePerParent","repeatHeader":true}}]}""");
                detail["controls"]![0]!["table"]!["repeatHeader"] = repeat;
                var document = Document(new JsonArray(TextBand("lead", 68, "LEADING-CONTENT"), title, detail));
                document["instances"]!.AsArray().Add(new JsonObject { ["instanceId"] = "rows", ["sourceType"] = "Constant", ["parameters"] = new JsonArray(), ["value"] = new JsonArray(Enumerable.Range(0, 30).Select(i => (JsonNode)new JsonObject { ["value"] = "BANDROW-" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) }).ToArray()) });
                if (rowMode == "Collection")
                {
                    var control = detail["controls"]![0]!.AsObject();
                    control["table"]!["rowMode"] = "Collection";
                    control["value"] = Parse("""{"from":"Row","path":["lines"],"nullPolicy":"Error"}""");
                    var instance = document["instances"]![0]!.AsObject();
                    instance["value"] = new JsonArray(new JsonObject { ["lines"] = instance["value"]!.DeepClone() });
                }
                var pages = await Render(document, "row-continuation-" + rowMode + "-" + repeat);
                var titlePage = Assert.Single(pages, page => page.Contains("BANDROW-TITLE", StringComparison.Ordinal));
                Assert.Contains("BANDROW-000", titlePage, StringComparison.Ordinal);
                foreach (var index in Enumerable.Range(0, 30)) Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(string.Concat(pages), "BANDROW-" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)));
                var detailPages = pages.Where(page => System.Text.RegularExpressions.Regex.IsMatch(page, @"BANDROW-\d{3}")).ToArray();
                Assert.True(detailPages.Length > 1);
                if (repeat) Assert.All(detailPages, page => Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(page, "ROW-HEADER")));
                else Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(string.Concat(pages), "ROW-HEADER"));
            }
            completed = true;
        }
        finally { if (completed) Directory.Delete(root, true); }
    }

    [ReportEngineFact]
    public async Task Bundles_render_electronic_breaks_without_empty_pages_and_simple_parts_with_their_own_paper()
    {
        var node = Path.GetFullPath(Environment.GetEnvironmentVariable("PF09_RENDERER_TEST_NODE")!);
        var manifest = Path.GetFullPath(Environment.GetEnvironmentVariable("PF09_RENDERER_TEST_MANIFEST")!);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repo.FullName, "tools", "pf09-render-report.mjs"))) repo = repo.Parent ?? throw new InvalidOperationException("Repository tools missing.");
        var script = Path.Combine(repo.FullName, "tools", "pf09-render-report.mjs");
        var root = Path.Combine(Path.GetTempPath(), "studio-bundle-integration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var profile = StudioInitializationTests.Profile(Path.Combine(root, "unused.db"));
            profile.Renderer = new() { Admitted = true, Command = node, CommandSha256 = HashFile(node), Script = script, ScriptSha256 = HashFile(script), EngineManifestPath = manifest, EngineManifestHash = HashFile(manifest), WorkingRoot = Path.Combine(root, "renderer"), TimeoutSeconds = 45 };
            var renderer = new ReportCommandRenderer(profile);
            static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
            static JsonObject Child(string value, string orientation, string color)
            {
                var report = Parse("""{"schemaVersion":1,"mode":"Fixed","parameters":[],"instances":[{"instanceId":"rows","sourceType":"Constant","parameters":[],"value":[]}],"paper":{"size":"A4","orientation":"Portrait","margin":{"top":15,"right":15,"bottom":15,"left":15},"headerHeightMm":15,"footerHeightMm":10},"bands":[{"id":"detail","kind":"Detail","name":"Detail","heightMm":8,"data":{"mode":"RootPath","instanceId":"rows","path":[]},"controls":[{"id":"value","type":"Field","xMm":0,"yMm":0,"widthMm":100,"heightMm":8,"canGrow":true,"styleId":"body","value":{"from":"Row","path":["value"],"nullPolicy":"Error"}}]}],"styles":[{"id":"body","fontFamily":"sans-serif","fontSizePt":10,"color":"#112233"}],"assets":[]}""");
                report["instances"]![0]!["value"] = new JsonArray(new JsonObject { ["value"] = value });
                report["paper"]!["orientation"] = orientation; report["styles"]![0]!["color"] = color;
                return report;
            }
            var portrait = Child("PORTRAIT-CONTENT", "Portrait", "#112233");
            var landscape = Child("LANDSCAPE-CONTENT", "Landscape", "#445566");
            var dependencies = new Dictionary<string, ReportDefinitionSnapshot>
            {
                ["portrait/rev1"] = new("Report", portrait, new Dictionary<string, ReportDefinitionSnapshot>(), "portrait"),
                ["landscape/rev1"] = new("Report", landscape, new Dictionary<string, ReportDefinitionSnapshot>(), "landscape")
            };
            async Task<(ReportAssemblyPlan Plan, ReportRenderedOutput Output)> Render(JsonObject bundle, string name)
            {
                var resolver = new ReportDataResolver((_, _, _) => throw new InvalidOperationException("Constant bundle must not query a source."));
                var plan = await new ReportPlanner(resolver, new()).PlanAsync(new("ReportBundle", bundle, dependencies, "fixture"), new(), default);
                var output = await renderer.RenderAsync(name, ReportExpander.Expand(plan), (bundle["paper"] ?? portrait["paper"])!.AsObject(), true, default, "Html");
                return (plan, output);
            }
            static async Task<string> PageHtml(ReportRenderedOutput output, int index)
            {
                var file = StudioJson.RequiredString(output.Result["pages"]![index]!.AsObject(), "htmlFile");
                return await File.ReadAllTextAsync(Path.Combine(output.Directory, file));
            }
            var electronic = Parse("""{"schemaVersion":1,"mode":"ElectronicRecord","parameters":[],"instances":[{"instanceId":"orders","sourceType":"Constant","parameters":[],"value":[{"id":"WO","operations":[{"id":"empty-before","rows":[]},{"id":"first","rows":[{"value":"FIRST-RECORD"}]},{"id":"empty-between","rows":[]},{"id":"second","rows":[{"value":"SECOND-RECORD"}]},{"id":"empty-after","rows":[]}]}]}],"assembly":{"root":{"mode":"RootPath","instanceId":"orders","path":[]},"operation":{"collection":{"mode":"ParentPath","path":["operations"]}},"rules":[{"id":"record","reportNId":"portrait","revisionNId":"rev1","parameterMap":[],"repeat":{"mode":"ParentPath","path":["rows"]},"required":false,"content":{"mode":"BandSubtree","entryBandId":"detail","rowMap":[{"targetPath":["value"],"source":{"from":"Row","path":["value"],"nullPolicy":"Error"}}],"ancestorMap":[]}}]}}""");
            electronic["paper"] = portrait["paper"]!.DeepClone();
            foreach (var breakBefore in new[] { "Auto", "Page" })
            {
                electronic["assembly"]!["rules"]![0]!["breakBefore"] = breakBefore;
                var (plan, output) = await Render(electronic, "electronic-" + breakBefore);
                Assert.Equal(2, plan.Instances.Count(instance => instance.Decision == "Include"));
                Assert.Equal(3, plan.Instances.Count(instance => instance.Decision == "Skip"));
                Assert.Equal(breakBefore == "Page" ? 2 : 1, output.PageCount);
                var first = await PageHtml(output, 0);
                Assert.Contains("FIRST-RECORD", first, StringComparison.Ordinal);
                Assert.DoesNotContain("PORTRAIT-CONTENT", first, StringComparison.Ordinal);
                if (breakBefore == "Auto") Assert.Contains("SECOND-RECORD", first, StringComparison.Ordinal);
                else
                {
                    var second = await PageHtml(output, 1);
                    Assert.DoesNotContain("SECOND-RECORD", first, StringComparison.Ordinal);
                    Assert.Contains("SECOND-RECORD", second, StringComparison.Ordinal);
                    Assert.DoesNotContain("FIRST-RECORD", second, StringComparison.Ordinal);
                }
            }
            var simple = Parse("""{"schemaVersion":1,"mode":"Simple","parameters":[],"instances":[],"pageNumberMode":"Global","items":[{"id":"portrait-part","reportNId":"portrait","revisionNId":"rev1","parameterMap":[],"enabled":true,"required":true,"startOnNewPage":true},{"id":"landscape-part","reportNId":"landscape","revisionNId":"rev1","parameterMap":[],"enabled":true,"required":true,"startOnNewPage":true}]}""");
            foreach (var pageNumberMode in new[] { "Global", "Preserve" })
            {
                simple["pageNumberMode"] = pageNumberMode;
                var (_, output) = await Render(simple, "simple-" + pageNumberMode);
                Assert.Equal(2, output.PageCount);
                var pages = output.Result["pages"]!.AsArray();
                Assert.InRange(pages[0]!["widthMm"]!.GetValue<double>(), 209.5, 210.5);
                Assert.InRange(pages[0]!["heightMm"]!.GetValue<double>(), 296.5, 297.5);
                Assert.InRange(pages[1]!["widthMm"]!.GetValue<double>(), 296.5, 297.5);
                Assert.InRange(pages[1]!["heightMm"]!.GetValue<double>(), 209.5, 210.5);
                var first = await PageHtml(output, 0); var second = await PageHtml(output, 1);
                Assert.Contains("PORTRAIT-CONTENT", first, StringComparison.Ordinal);
                Assert.DoesNotContain("LANDSCAPE-CONTENT", first, StringComparison.Ordinal);
                Assert.Contains("LANDSCAPE-CONTENT", second, StringComparison.Ordinal);
                Assert.DoesNotContain("PORTRAIT-CONTENT", second, StringComparison.Ordinal);
                Assert.Contains("color: rgb(17, 34, 51)", first, StringComparison.Ordinal);
                Assert.Contains("color: rgb(68, 85, 102)", second, StringComparison.Ordinal);
                Assert.Contains(pageNumberMode == "Global" ? ">1 / 2<" : ">1 / 1<", first, StringComparison.Ordinal);
                Assert.Contains(pageNumberMode == "Global" ? ">2 / 2<" : ">1 / 1<", second, StringComparison.Ordinal);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [ReportEngineFact]
    public async Task Native_once_worker_renders_confirms_files_and_downloads_pdf_html_xlsx_and_frozen_preview()
    {
        var node = Path.GetFullPath(Environment.GetEnvironmentVariable("PF09_RENDERER_TEST_NODE")!);
        var manifest = Path.GetFullPath(Environment.GetEnvironmentVariable("PF09_RENDERER_TEST_MANIFEST")!);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repo.FullName, "tools", "pf09-render-report.mjs"))) repo = repo.Parent ?? throw new InvalidOperationException("Repository tools missing.");
        var script = Path.Combine(repo.FullName, "tools", "pf09-render-report.mjs");
        var root = Path.Combine(Path.GetTempPath(), "studio-render-integration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var profile = StudioInitializationTests.Profile(Path.Combine(root, "studio.db")); profile.InstalledModules = ["Report"]; profile.Jobs.Provider = "Embedded";
            profile.Files = new() { Provider = "Embedded", Root = Path.Combine(root, "files") }; profile.Identity.DataProtectionKeyPath = Path.Combine(root, "keys");
            profile.Renderer = new() { Admitted = true, Command = node, CommandSha256 = HashFile(node), Script = script, ScriptSha256 = HashFile(script), EngineManifestPath = manifest, EngineManifestHash = HashFile(manifest), WorkingRoot = Path.Combine(root, "renderer"), TimeoutSeconds = 45 };
            using var json = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new { PlatformStudio = profile }));
            var configuration = new ConfigurationBuilder().AddJsonStream(json).Build();
            var services = new ServiceCollection(); services.AddSingleton<IConfiguration>(configuration); services.AddLogging(); services.AddPlatformStudio(configuration);
            var identity = new Identity(); services.AddSingleton<IStudioIdentity>(identity); services.AddSingleton<IStudioSourceExecutor>(new StudioTestSourceExecutor());
            await using var provider = services.BuildServiceProvider(); await provider.GetRequiredService<StudioServiceInitializer>().InitializeLocalAsync(default);
            using var scope = provider.CreateScope(); var p = identity.Principal; var publishing = scope.ServiceProvider.GetRequiredService<StudioPublishing>();
            var document = JsonNode.Parse("""{"schemaVersion":1,"mode":"Fixed","parameters":[],"instances":[{"instanceId":"rows","sourceType":"Constant","value":[{"value":"FILLED-ONE"},{"value":"FILLED-TWO"}],"parameters":[]}],"paper":{"size":"A4","orientation":"Portrait","margin":{"top":15,"right":15,"bottom":15,"left":15},"headerHeightMm":15,"footerHeightMm":10},"bands":[{"id":"detail","kind":"Detail","name":"Detail","heightMm":8,"grow":true,"data":{"mode":"RootPath","instanceId":"rows","path":[]},"controls":[{"id":"table","type":"Table","xMm":0,"yMm":0,"widthMm":180,"heightMm":8,"canGrow":true,"table":{"rowMode":"BandRow","columns":[{"id":"value","label":"填写结果","widthMm":180,"value":{"from":"Row","path":["value"],"nullPolicy":"Error"}}],"headerMode":"OncePerParent","repeatHeader":true}}]}],"styles":[],"assets":[]}""")!.AsObject();
            var resource = await publishing.CreateAsync(p, new("engine-report", "Report", "Engine report", null, null, document), "create-engine", default);
            var validation = await publishing.ValidateAsync(p, resource.NId, document, default);
            var revision = await publishing.PublishAsync(p, resource.NId, new(resource.OptimisticVersion, resource.ConcurrencyVersion, "Engine fixture", true, validation.DependencyPlanHash), "publish-engine", default);
            var fixedRevision = await scope.ServiceProvider.GetRequiredService<IStudioResourceStore>().FindRevisionAsync(p.TenantNId, revision.NId, default) ?? throw new InvalidOperationException("Revision missing.");
            // Interactive preview succeeds before the Jobs worker starts and
            // creates neither report runs nor archived artifacts.
            var direct = scope.ServiceProvider.GetRequiredService<ReportPreview>();
            var preview = await direct.RenderAsync(p, new("Report", resource.NId, document, new()), default);
            Assert.Equal("Succeeded", preview.Status);
            var directHtml = Assert.Single(preview.Pages).Html;
            Assert.Contains("FILLED-ONE", directHtml, StringComparison.Ordinal);
            Assert.Contains("FILLED-TWO", directHtml, StringComparison.Ordinal);
            Assert.Contains("PREVIEW", directHtml, StringComparison.Ordinal);
            var queryDocument = document.DeepClone().AsObject(); queryDocument["mode"] = "Query";
            queryDocument["query"] = JsonNode.Parse("""{"instanceId":"rows","resultBandId":"detail","filters":[],"columns":[{"controlId":"table","columnId":"value","visible":true,"sortable":true}],"defaultSort":[{"fieldId":"value","direction":"Desc","nulls":"Last"}],"defaultPageSize":20}""");
            var queryPreview = await direct.RenderAsync(p, new("Report", resource.NId, queryDocument, new()), default);
            var sortedHtml = Assert.Single(queryPreview.Pages).Html;
            Assert.Contains("FILLED-ONE", sortedHtml, StringComparison.Ordinal);
            Assert.Contains("FILLED-TWO", sortedHtml, StringComparison.Ordinal);
            Assert.True(sortedHtml.IndexOf("FILLED-TWO", StringComparison.Ordinal) < sortedHtml.IndexOf("FILLED-ONE", StringComparison.Ordinal));
            var directDb = provider.GetRequiredService<StudioDatabase>();
            Assert.Equal(0, await directDb.Client.Queryable<StudioReportRun>().CountAsync());
            Assert.Equal(0, await directDb.Client.Queryable<StudioReportArtifact>().CountAsync());
            Assert.Empty(Directory.GetDirectories(profile.Renderer.WorkingRoot));
            identity.Revoked = true;
            Assert.Equal("STUDIO_UNAUTHENTICATED", (await Assert.ThrowsAsync<StudioException>(() => direct.RenderAsync(p, new("Report", resource.NId, document, new()), default))).Code);
            Assert.Empty(Directory.GetDirectories(profile.Renderer.WorkingRoot));
            identity.Revoked = false;
            var worker = provider.GetServices<IHostedService>().OfType<SchedulerWorker>().Single();
            // A hosted worker must not inherit the test caller's mutable SqlSugar client context.
            Task workerStart;
            using (ExecutionContext.SuppressFlow()) workerStart = worker.StartAsync(default);
            await workerStart;
            try
            {
                var runs = scope.ServiceProvider.GetRequiredService<ReportRuns>(); var outputs = scope.ServiceProvider.GetRequiredService<ReportOutputs>(); var files = provider.GetRequiredService<IFileStore>();
                string? sourceRunNId = null;
                foreach (var format in ArchiveScenarios.Concat(BundleModes.SelectMany(mode => BundleFormats.Select(format => mode + "-" + format))))
                {
                    var isBundle = format.StartsWith("NormalBundle-", StringComparison.Ordinal) || format.StartsWith("DataDrivenBundle-", StringComparison.Ordinal);
                    var outputFormat = isBundle ? format[(format.IndexOf('-') + 1)..] : format;
                    FrozenReportInput input = new("Production", fixedRevision.Id, null, "{}", format, "engine-fixture");
                    if (BundleFormats.Contains(format))
                    {
                        var beforeDraftRuns = await directDb.Client.Queryable<StudioReportRun>().CountAsync();
                        var beforeDraftArtifacts = await directDb.Client.Queryable<StudioReportArtifact>().CountAsync();
                        var download = await direct.ExportAsync(p, new("Report", resource.NId, document, new()), format, default);
                        Assert.NotEmpty(download.Content); Assert.Equal(ReportFormats.ContentType(format), download.ContentType);
                        Assert.Equal(beforeDraftRuns, await directDb.Client.Queryable<StudioReportRun>().CountAsync());
                        Assert.Equal(beforeDraftArtifacts, await directDb.Client.Queryable<StudioReportArtifact>().CountAsync());
                        if (Environment.GetEnvironmentVariable("PF09_R5_OUTPUT") is { Length: > 0 } outputDirectory)
                            await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "draft-Single-" + format + ReportFormats.Extension(download.ContentType)), download.Content);
                    }
                    if (format == "DocumentControls")
                    {
                        var composed = document.DeepClone().AsObject();
                        var created = await publishing.CreateAsync(p, new("engine-documents", "Report", "R4 document archive", null, null, composed), "create-documents", default);
                        using var pdfSource = new MemoryStream(ReportDocumentControlsTests.PdfFixture());
                        var sourceHash = Convert.ToHexStringLower(SHA256.HashData(pdfSource.ToArray()));var owned=provider.GetRequiredService<IOwnedFileService>();var transfer=provider.GetRequiredService<IFileService>();var context=new TrustedServiceContext(p.TenantNId,"platform-studio",p.SubjectNId);
                        var upload=await OwnedFileUploads.CommitAsync(owned,transfer,context,new("pdf-fixture","platform-studio","StudioAsset",created.NId,"source.pdf","application/pdf",pdfSource.Length,sourceHash),pdfSource,default);
                        foreach(var file in await files.ListPendingScanAsync(100,default))await files.UpdateFileAsync(file with {ScanStatus="Clean"},default);
                        var reference=await owned.CommitAsync(context,new(upload.Prepared.ReferenceNId,upload.File.FileNId,sourceHash),default);Assert.Equal("Active",reference.Status);
                        composed["assets"]!.AsArray().Add(new JsonObject { ["id"]="source-pdf",["fileNId"]=upload.File.FileNId,["referenceNId"]=reference.ReferenceNId,["sha256"]=sourceHash,["mediaType"]="application/pdf" });
                        var sub=ReportDocumentControlsTests.SubReport(resource.NId);sub["subReport"]!["revisionNId"]=revision.NId;sub["subReport"]!["instanceMap"]=JsonNode.Parse("""[{"target":"rows","source":{"from":"Instance","instanceId":"rows","path":[],"nullPolicy":"Error"}}]""");sub["outline"]=JsonNode.Parse("""{"title":{"from":"Constant","value":"固定子报表","nullPolicy":"Error"},"level":1}""");
                        composed["bands"]=new JsonArray(
                            JsonNode.Parse("""{"id":"toc","kind":"ReportHeader","name":"目录","heightMm":10,"controls":[{"id":"toc","type":"TableOfContents","xMm":0,"yMm":0,"widthMm":180,"heightMm":10,"canGrow":true,"toc":{"title":"归档目录"}}]}"""),
                            new JsonObject{["id"]="sub",["kind"]="ReportHeader",["name"]="子报表",["heightMm"]=10,["controls"]=new JsonArray(sub)},
                            JsonNode.Parse("""{"id":"pdf","kind":"ReportFooter","name":"PDF","heightMm":10,"controls":[{"id":"pdf","type":"PdfContent","xMm":0,"yMm":0,"widthMm":180,"heightMm":10,"canGrow":true,"value":{"from":"Constant","value":"source-pdf","nullPolicy":"Error"},"outline":{"title":{"from":"Constant","value":"原始附件","nullPolicy":"Error"},"level":1}}]}"""));
                        var saved=await publishing.SaveDraftAsync(p,created.NId,new(created.OptimisticVersion,created.ConcurrencyVersion,created.Name,null,null,composed),default);var savedResource=saved;
                        var validated=await publishing.ValidateAsync(p,created.NId,composed,default);Assert.True(validated.Valid,string.Join(";",validated.Diagnostics.Select(d=>d.Message)));
                        var published=await publishing.PublishAsync(p,created.NId,new(StudioJson.Text(savedResource["optimisticVersion"]),Guid.Parse(StudioJson.RequiredString(savedResource.AsObject(),"concurrencyVersion")),"R4 integration",true,validated.DependencyPlanHash),"publish-documents",default);
                        var beforeRuns=await directDb.Client.Queryable<StudioReportRun>().CountAsync();var beforeArtifacts=await directDb.Client.Queryable<StudioReportArtifact>().CountAsync();
                        var badHash=composed.DeepClone().AsObject();badHash["assets"]![0]!["sha256"]=new string('0',64);
                        Assert.Equal("REPORT_RESOURCE_INVALID",(await Assert.ThrowsAsync<StudioException>(()=>direct.RenderAsync(p,new("Report",created.NId,badHash,new()),default))).Code);
                        var wrongOwner=composed.DeepClone().AsObject();wrongOwner["bands"]=new JsonArray(composed["bands"]![2]!.DeepClone());
                        await Assert.ThrowsAsync<StudioException>(()=>direct.RenderAsync(p,new("Report",resource.NId,wrongOwner,new()),default));
                        var documentPreview=await direct.RenderAsync(p,new("Report",created.NId,composed,new()),default);Assert.Equal("Succeeded",documentPreview.Status);Assert.Equal(2,documentPreview.Pages.Count);Assert.Contains("R4-SOURCE-PDF",string.Concat(documentPreview.Pages.Select(x=>x.Html)),StringComparison.Ordinal);
                        Assert.Equal(beforeRuns,await directDb.Client.Queryable<StudioReportRun>().CountAsync());Assert.Equal(beforeArtifacts,await directDb.Client.Queryable<StudioReportArtifact>().CountAsync());
                        var stored=(await scope.ServiceProvider.GetRequiredService<IStudioResourceStore>().FindRevisionAsync(p.TenantNId,published.NId,default))!;input=new("Production",stored.Id,null,"{}","Pdf","engine-fixture");
                    }
                    if (format is "AdvancedControls" or "AdvancedXlsx" or "AdvancedDocx" or "AdvancedRtf")
                    {
                        var advanced = ReportAdvancedControlsTests.AdvancedDocument();
                        if (format == "AdvancedXlsx") advanced["bands"] = new JsonArray(advanced["bands"]!.AsArray().Where(band => StudioJson.Text(band!["controls"]![0]!["type"]) is "CharacterComb" or "PivotTable").Select(band => band!.DeepClone()).ToArray());
                        var advancedResource = await publishing.CreateAsync(p, new("engine-" + format, "Report", "Advanced controls archive", null, null, advanced), "create-" + format, default);
                        var advancedValidation = await publishing.ValidateAsync(p, advancedResource.NId, advanced, default);
                        var advancedRevision = await publishing.PublishAsync(p, advancedResource.NId, new(advancedResource.OptimisticVersion, advancedResource.ConcurrencyVersion, "R3 archive regression", true, advancedValidation.DependencyPlanHash), "publish-" + format, default);
                        var storedAdvanced = (await scope.ServiceProvider.GetRequiredService<IStudioResourceStore>().FindRevisionAsync(p.TenantNId, advancedRevision.NId, default))!;
                        var beforeRuns = await directDb.Client.Queryable<StudioReportRun>().CountAsync();
                        var beforeArtifacts = await directDb.Client.Queryable<StudioReportArtifact>().CountAsync();
                        var advancedPreview = await direct.RenderAsync(p, new("Report", advancedResource.NId, advanced, new()), default);
                        Assert.Equal("Succeeded", advancedPreview.Status);
                        Assert.Contains(format == "AdvancedControls" ? "data:image/png;base64," : "12.3", string.Concat(advancedPreview.Pages.Select(page => page.Html)), StringComparison.Ordinal);
                        Assert.Equal(beforeRuns, await directDb.Client.Queryable<StudioReportRun>().CountAsync());
                        Assert.Equal(beforeArtifacts, await directDb.Client.Queryable<StudioReportArtifact>().CountAsync());
                        input = new("Production", storedAdvanced.Id, null, "{}", format == "AdvancedXlsx" ? "Xlsx" : format == "AdvancedDocx" ? "Docx" : format == "AdvancedRtf" ? "Rtf" : "Html", "engine-fixture");
                    }
                    if (format == "Preview")
                    {
                        var previewDocument = document.DeepClone().AsObject();
                        previewDocument["bands"]!.AsArray().Add(JsonNode.Parse("""{"id":"page-footer","kind":"PageFooter","name":"Page footer","heightMm":7,"controls":[{"id":"page-number","type":"PageNumber","xMm":160,"yMm":0,"widthMm":20,"heightMm":5}]}"""));
                        using var image = new MemoryStream(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg=="));
                        var imageHash = Convert.ToHexStringLower(SHA256.HashData(image.ToArray())); var owned = provider.GetRequiredService<IOwnedFileService>(); var transfer = provider.GetRequiredService<IFileService>(); var context = new TrustedServiceContext(p.TenantNId, "platform-studio", p.SubjectNId);
                        var imageUpload = await OwnedFileUploads.CommitAsync(owned, transfer, context, new("image-fixture", "platform-studio", "StudioAsset", resource.NId, "pixel.png", "image/png", image.Length, imageHash), image, default);
                        foreach (var file in await files.ListPendingScanAsync(100, default)) await files.UpdateFileAsync(file with { ScanStatus = "Clean" }, default);
                        var imageReference = await owned.CommitAsync(context, new(imageUpload.Prepared.ReferenceNId, imageUpload.File.FileNId, imageHash), default); Assert.Equal("Active", imageReference.Status);
                        previewDocument["assets"]!.AsArray().Add(new JsonObject { ["id"] = "pixel", ["fileNId"] = imageUpload.File.FileNId, ["referenceNId"] = imageReference.ReferenceNId, ["sha256"] = imageHash, ["mediaType"] = "image/png" });
                        previewDocument["bands"]!.AsArray().Add(JsonNode.Parse("""{"id":"image-header","kind":"ReportHeader","name":"Frozen image","heightMm":10,"controls":[{"id":"image","type":"Image","xMm":0,"yMm":0,"widthMm":10,"heightMm":10,"canGrow":false,"value":{"from":"Constant","value":"pixel","nullPolicy":"Error"}}]}"""));
                        previewDocument["bands"]!.AsArray().Add(JsonNode.Parse("""{"id":"filled-html","kind":"ReportFooter","name":"Saved record","heightMm":8,"controls":[{"id":"saved-html","type":"Html","xMm":0,"yMm":0,"widthMm":180,"heightMm":8,"canGrow":true,"value":{"from":"Constant","nullPolicy":"Error","value":{"html":"<p>填写记录<input name=\"entry\"></p>","sourceRecordKey":"record-1","recordedOn":"2026-10-28T00:00:00Z","resourceRefs":[],"formValues":{"entry":"RICH-FILLED"}}}}]}"""));
                        var savedRecord = previewDocument["bands"]!.AsArray().Last()!["controls"]![0]!["value"]!["value"]!.AsObject(); savedRecord["html"] = StudioJson.Text(savedRecord["html"]) + "<img src=\"record:pixel\">";
                        savedRecord["resourceRefs"] = new JsonArray(new JsonObject { ["src"] = "record:pixel", ["fileNId"] = imageUpload.File.FileNId, ["referenceNId"] = imageReference.ReferenceNId, ["sha256"] = imageHash, ["mediaType"] = "image/png" });
                        var checks = new JsonArray();
                        for (var index = 0; index < 3; index++) checks.Add(new JsonObject { ["id"] = "check-" + index, ["type"] = "CheckBox", ["xMm"] = index * 12, ["yMm"] = 0, ["widthMm"] = 10, ["heightMm"] = 6, ["value"] = new JsonObject { ["from"] = "Constant", ["value"] = index == 2 ? null : JsonValue.Create(index == 0), ["nullPolicy"] = "Null" } });
                        previewDocument["bands"]!.AsArray().Add(new JsonObject { ["id"] = "checkbox-footer", ["kind"] = "ReportFooter", ["name"] = "Boolean states", ["heightMm"] = 8, ["controls"] = checks });
                        var frozen = await scope.ServiceProvider.GetRequiredService<ReportDesign>().FreezeAsync(p, new("Report", resource.NId, previewDocument, new()), default); input = new("Preview", null, frozen.Frozen.ToJsonString(), frozen.Parameters.ToJsonString(), "Html", "engine-fixture");
                    }
                    if (isBundle)
                    {
                        var bundle = JsonNode.Parse("""{"schemaVersion":1,"mode":"ElectronicRecord","parameters":[],"instances":[{"instanceId":"orders","sourceType":"Constant","parameters":[],"value":[{"id":"WO","ops":[{"id":"OP1","basic":[{"value":"BASIC-OP1"}],"details":[{"value":"DETAIL-OP1"}]},{"id":"OP2","basic":[{"value":"BASIC-OP2"}],"details":[]}]}]}],"assembly":{"stitchMode":"DataDriven","root":{"mode":"RootPath","instanceId":"orders","path":[]},"operation":{"collection":{"mode":"ParentPath","path":["ops"]}},"templates":[],"rules":[]}}""")!.AsObject();
                        bundle["instances"]![0]!["value"]![0]!["ops"]![0]!["details"] = new JsonArray(Enumerable.Range(0, 40).Select(i => (JsonNode)new JsonObject { ["value"] = $"DETAIL-OP1-{i:000}" }).ToArray());
                        bundle["paper"] = document["paper"]!.DeepClone();
                        foreach (var id in new[] { "basic", "details" })
                        {
                            var candidate = new JsonObject { ["id"] = id, ["priority"] = id == "basic" ? 1 : 2, ["reportNId"] = resource.NId, ["revisionNId"] = revision.NId, ["parameterMap"] = new JsonArray(), ["required"] = id == "basic" };
                            var normal = candidate.DeepClone().AsObject();
                            candidate["scope"] = "Operation";
                            candidate["instanceMap"] = new JsonArray(new JsonObject { ["target"] = "rows", ["source"] = new JsonObject { ["from"] = "Row", ["path"] = new JsonArray(id), ["nullPolicy"] = "Error" } });
                            bundle["assembly"]!["templates"]!.AsArray().Add(candidate);
                            normal["repeat"] = new JsonObject { ["mode"] = "ParentPath", ["path"] = new JsonArray(id) };
                            normal["content"] = JsonNode.Parse("""{"mode":"BandSubtree","entryBandId":"detail","rowMap":[{"targetPath":["value"],"source":{"from":"Row","path":["value"],"nullPolicy":"Error"}}],"ancestorMap":[]}""");
                            bundle["assembly"]!["rules"]!.AsArray().Add(normal);
                        }
                        if (format.StartsWith("NormalBundle-", StringComparison.Ordinal)) bundle["assembly"]!["stitchMode"] = "Normal";
                        var bundleResource = await publishing.CreateAsync(p, new("engine-" + format, "ReportBundle", format, null, null, bundle), "create-" + format, default);
                        var bundleValidation = await publishing.ValidateAsync(p, bundleResource.NId, bundle, default);
                        var bundleRevision = await publishing.PublishAsync(p, bundleResource.NId, new(bundleResource.OptimisticVersion, bundleResource.ConcurrencyVersion, "Bundle archive regression", true, bundleValidation.DependencyPlanHash), "publish-" + format, default);
                        var storedRevision = (await scope.ServiceProvider.GetRequiredService<IStudioResourceStore>().FindRevisionAsync(p.TenantNId, bundleRevision.NId, default))!;
                        var beforeRuns = await directDb.Client.Queryable<StudioReportRun>().CountAsync();
                        var beforeArtifacts = await directDb.Client.Queryable<StudioReportArtifact>().CountAsync();
                        var bundlePreview = await direct.RenderAsync(p, new("ReportBundle", bundleResource.NId, bundle, new()), default);
                        Assert.Equal("Succeeded", bundlePreview.Status);
                        Assert.True(bundlePreview.Pages.Count >= 2);
                        var bundleHtml = string.Concat(bundlePreview.Pages.Select(page => page.Html));
                        foreach (var marker in Enumerable.Range(0, 40).Select(i => $"DETAIL-OP1-{i:000}").Concat(BundleBasicMarkers))
                            Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(bundleHtml, marker));
                        foreach (var page in bundlePreview.Pages.Where(page => page.Html.Contains("DETAIL-OP1-", StringComparison.Ordinal)))
                            Assert.Contains("填写结果", page.Html, StringComparison.Ordinal);
                        Assert.Equal(beforeRuns, await directDb.Client.Queryable<StudioReportRun>().CountAsync());
                        Assert.Equal(beforeArtifacts, await directDb.Client.Queryable<StudioReportArtifact>().CountAsync());
                        var download = await direct.ExportAsync(p, new("ReportBundle", bundleResource.NId, bundle, new()), outputFormat, default);
                        Assert.NotEmpty(download.Content); Assert.Equal(ReportFormats.ContentType(outputFormat), download.ContentType);
                        Assert.Equal(beforeRuns, await directDb.Client.Queryable<StudioReportRun>().CountAsync());
                        Assert.Equal(beforeArtifacts, await directDb.Client.Queryable<StudioReportArtifact>().CountAsync());
                        if (Environment.GetEnvironmentVariable("PF09_R5_OUTPUT") is { Length: > 0 } outputDirectory)
                            await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "draft-" + format + ReportFormats.Extension(download.ContentType)), download.Content);
                        input = new("Production", storedRevision.Id, null, "{}", outputFormat, "engine-fixture");
                    }
                    var accepted = format is "RenderSnapshot" or "NewData" ? await scope.ServiceProvider.GetRequiredService<ReportRetries>().RetryAsync(p, sourceRunNId!, (await runs.GetAsync(p, sourceRunNId!, default)).StateVersion, format, "run-" + format, "engine-fixture", default) : await runs.AcceptFrozenAsync(p, input, "run-" + format, default);
                    if (format == "Pdf") sourceRunNId = accepted.RunNId;
                    var state = accepted;
                    for (var attempt = 0; attempt < 120 && state.Status is not ("Succeeded" or "Failed" or "Unknown" or "Cancelled"); attempt++)
                    {
                        // Explicit fixture scanner evidence; this does not test ClamAV or a production scanner.
                        foreach (var file in await files.ListPendingScanAsync(100, default)) await files.UpdateFileAsync(file with { ScanStatus = "Clean" }, default);
                        await Task.Delay(250); state = await runs.GetAsync(p, accepted.RunNId, default);
                    }
                    Assert.True(state.Status == "Succeeded", state.Status + ":" + state.ErrorCode);
                    var db = provider.GetRequiredService<StudioDatabase>(); var run = await scope.ServiceProvider.GetRequiredService<IReportRunStore>().FindAsync(p.TenantNId, state.RunNId, default);
                    if (format == "Preview")
                    {
                        await using var page = await outputs.PreviewPageAsync(p, state.RunNId, 1, default); using var text = new StreamReader(page);
                        var html = await text.ReadToEndAsync(); Assert.Contains("FILLED-ONE", html, StringComparison.Ordinal); Assert.Contains("RICH-FILLED", html, StringComparison.Ordinal); Assert.Contains("data:image/png;base64,", html, StringComparison.Ordinal); Assert.DoesNotContain("<input", html, StringComparison.Ordinal);
                        foreach (var checkedState in new[] { "true", "false", "mixed" }) Assert.Contains("aria-checked=\"" + checkedState + "\"", html, StringComparison.Ordinal);
                        Assert.Equal(0, await db.Client.Queryable<StudioReportArtifact>().Where(a => a.RunId == run!.Id).CountAsync());
                        Assert.Equal(1, await db.Client.Queryable<StudioRevision>().CountAsync());
                        var expiry = DateTimeOffset.UtcNow.AddMinutes(-1);
                        var nativeStore = provider.GetRequiredService<ISchedulerStore>(); var previewNative = (await nativeStore.GetRunAsync(p.TenantNId, run!.SchedulerRunNId!, default))!;
                        for (var wait = 0; wait < 100 && previewNative.Status != "Succeeded"; wait++) { await Task.Delay(100); previewNative = (await nativeStore.GetRunAsync(p.TenantNId, run.SchedulerRunNId!, default))!; }
                        Assert.Equal("Succeeded", previewNative.Status);
                        Assert.Equal(DateTimeOffset.MaxValue, run.PreviewExpiresOn);
                        // Legacy timestamps must not expire a design preview or release its files.
                        await db.Client.Updateable<StudioReportRun>().SetColumns(r => new StudioReportRun { PreviewExpiresOn = expiry }).Where(r => r.Id == run.Id).ExecuteCommandAsync();
                        var longLived = JsonSerializer.SerializeToNode(await outputs.PreviewAsync(p, run.NId, default), JsonSerializerOptions.Web)!;
                        Assert.Null(longLived["expiresOn"]); Assert.NotEmpty(longLived["pages"]!.AsArray());
                        await outputs.ListAsync(p, 1, 20, default);
                        var ownedService = provider.GetRequiredService<IOwnedFileService>(); var previewContext = new TrustedServiceContext(p.TenantNId, "platform-studio", p.SubjectNId);
                        Assert.All((await ownedService.ListByOwnerAsync(previewContext, "ReportPreview", run.NId, null, default)).Items, item => Assert.Equal("Active", item.Status));
                        await using var retainedPage = await outputs.PreviewPageAsync(p, run.NId, 1, default);
                        using var retainedText = new StreamReader(retainedPage); Assert.Contains("FILLED-ONE", await retainedText.ReadToEndAsync(), StringComparison.Ordinal);
                        continue;
                    }
                    var artifact = await db.Client.Queryable<StudioReportArtifact>().Where(a => a.RunId == run!.Id && a.Purpose == "Output").FirstAsync();
                    var provenance = JsonNode.Parse(artifact.EngineManifestJson)!.AsObject(); Assert.Equal(64, StudioJson.RequiredString(provenance, "inputSha256").Length); Assert.NotEmpty(provenance["pages"]!.AsArray()); Assert.NotNull(provenance["cellFragmentLedger"]); Assert.DoesNotContain("bundlePath", artifact.EngineManifestJson, StringComparison.Ordinal);
                    var downloaded = await outputs.ArtifactAsync(p, state.RunNId, artifact.NId, default); await using var content = downloaded.Content;
                    using var bytes = new MemoryStream(); await content.CopyToAsync(bytes);
                    Assert.Equal(artifact.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
                    if (Environment.GetEnvironmentVariable("PF09_R5_OUTPUT") is { Length: > 0 } exportFolder)
                        await File.WriteAllBytesAsync(Path.Combine(exportFolder, "archived-" + format + ReportFormats.Extension(downloaded.ContentType)), bytes.ToArray());
                    Assert.Equal(ReportFormats.ContentType(artifact.Format), downloaded.ContentType);
                    if (format == "Xls")
                    {
                        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                        bytes.Position = 0; using var workbook = ExcelReaderFactory.CreateBinaryReader(bytes);
                        Assert.True(workbook.Read()); Assert.Equal("填写结果", workbook.GetString(0));
                        Assert.True(workbook.Read()); Assert.Equal("FILLED-ONE", workbook.GetString(0));
                    }
                    if (format is "Docx" or "AdvancedDocx")
                    {
                        bytes.Position = 0; using var docx = WordprocessingDocument.Open(bytes, false);
                        Assert.Empty(new OpenXmlValidator().Validate(docx));
                        var body = docx.MainDocumentPart!.Document!.Body!; Assert.NotEmpty(body.ChildElements);
                        if (format == "Docx")
                        {
                            var logical = Assert.Single(body.Elements<W.Table>());
                            Assert.Equal(3, logical.Elements<W.TableRow>().Count());
                            Assert.NotNull(logical.Elements<W.TableRow>().First().TableRowProperties?.GetFirstChild<W.TableHeader>());
                        }
                        if (format == "AdvancedDocx") { Assert.NotEmpty(docx.MainDocumentPart.ImageParts); Assert.NotEmpty(body.Elements<W.Table>()); }
                    }
                    if (format is "Png" or "Jpeg") { bytes.Position = 0; using var zip = new System.IO.Compression.ZipArchive(bytes, System.IO.Compression.ZipArchiveMode.Read, true); Assert.Equal(artifact.PageCount + 1, zip.Entries.Count); Assert.NotNull(zip.GetEntry("pages.json")); }
                    if (format == "DocumentControls")
                    {
                        Assert.Equal(2,artifact.PageCount);Assert.Contains("documentsSha256",artifact.EngineManifestJson,StringComparison.Ordinal);Assert.Contains("pdfNativeSha256",artifact.EngineManifestJson,StringComparison.Ordinal);Assert.Equal("Production",run!.ExecutionKind);
                        if(Environment.GetEnvironmentVariable("PF09_R4_OUTPUT") is {Length:>0} folder)await File.WriteAllBytesAsync(Path.Combine(folder,"archived-documents.pdf"),bytes.ToArray());
                    }
                    if (format == "AdvancedControls")
                    {
                        var html = System.Text.Encoding.UTF8.GetString(bytes.ToArray());
                        foreach (var type in new[] { "Line", "Shape", "Barcode", "Chart", "Gauge", "Sparkline" }) Assert.Contains("alt=\"" + type + "\"", html, StringComparison.Ordinal);
                        Assert.Contains("12.3", html, StringComparison.Ordinal);
                        Assert.Contains("advancedSha256", artifact.EngineManifestJson, StringComparison.Ordinal);
                        Assert.DoesNotContain("data-report-advanced=", html, StringComparison.Ordinal);
                        Assert.DoesNotContain("PREVIEW", html, StringComparison.Ordinal);
                        Assert.Equal("Production", run!.ExecutionKind);
                        if (Environment.GetEnvironmentVariable("PF09_R3_OUTPUT") is { Length: > 0 } advancedOutput) await File.WriteAllBytesAsync(Path.Combine(advancedOutput, "archived-advanced.html"), bytes.ToArray());
                    }
                    if (format == "AdvancedXlsx")
                    {
                        bytes.Position = 0; StudioWorkbooks.ValidateXlsx(bytes);
                        bytes.Position = 0;
                        using var zip = new System.IO.Compression.ZipArchive(bytes, System.IO.Compression.ZipArchiveMode.Read, true);
                        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")!;
                        using var sheetReader = new StreamReader(sheet.Open()); var xml = await sheetReader.ReadToEndAsync();
                        Assert.Contains("12.3", xml, StringComparison.Ordinal);
                        Assert.Contains("光刻胶PR-26", xml, StringComparison.Ordinal);
                        Assert.DoesNotContain("pivotTable", string.Join(",", zip.Entries.Select(entry => entry.FullName)), StringComparison.Ordinal);
                        if (Environment.GetEnvironmentVariable("PF09_R3_OUTPUT") is { Length: > 0 } advancedOutput) await File.WriteAllBytesAsync(Path.Combine(advancedOutput, "archived-advanced.xlsx"), bytes.ToArray());
                    }
                    if (isBundle)
                    {
                        Assert.Equal(outputFormat, artifact.Format);
                        if (outputFormat == "Html")
                        {
                            var html = System.Text.Encoding.UTF8.GetString(bytes.ToArray());
                            foreach (var marker in Enumerable.Range(0, 40).Select(i => $"DETAIL-OP1-{i:000}").Concat(BundleBasicMarkers))
                                Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(html, marker));
                            Assert.DoesNotContain("FILLED-ONE", html, StringComparison.Ordinal);
                            Assert.DoesNotContain("PREVIEW", html, StringComparison.Ordinal);
                        }
                        if (outputFormat is "Xls" or "Docx")
                        {
                            bytes.Position = 0;
                            string text;
                            if (outputFormat == "Xls")
                            {
                                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                                using var reader = ExcelReaderFactory.CreateBinaryReader(bytes, new ExcelReaderConfiguration { LeaveOpen = true });
                                var cells = new List<string>();
                                while (reader.Read())
                                    for (var c = 0; c < reader.FieldCount; c++) cells.Add(reader.GetString(c) ?? "");
                                text = string.Join("\n", cells);
                            }
                            else
                            {
                                using var bundleDocx = WordprocessingDocument.Open(bytes, false);
                                Assert.Empty(new OpenXmlValidator().Validate(bundleDocx));
                                var body = bundleDocx.MainDocumentPart!.Document!.Body!;
                                text = body.InnerText;
                                Assert.NotEmpty(body.Elements<W.Table>());
                                if (format.StartsWith("DataDrivenBundle-", StringComparison.Ordinal))
                                    Assert.All(body.Elements<W.Table>(), table =>
                                        Assert.NotNull(table.Elements<W.TableRow>().First().TableRowProperties?.GetFirstChild<W.TableHeader>()));
                            }
                            foreach (var marker in Enumerable.Range(0, 40).Select(i => $"DETAIL-OP1-{i:000}").Concat(BundleBasicMarkers))
                                Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(text, marker));
                            Assert.True(text.IndexOf("BASIC-OP1", StringComparison.Ordinal) < text.IndexOf("DETAIL-OP1-000", StringComparison.Ordinal));
                            Assert.True(text.IndexOf("DETAIL-OP1-039", StringComparison.Ordinal) < text.IndexOf("BASIC-OP2", StringComparison.Ordinal));
                            Assert.DoesNotContain("PREVIEW", text, StringComparison.Ordinal);
                        }
                        Assert.Equal("Production", run!.ExecutionKind);
                        if (outputFormat is "Pdf" or "Html" or "Mht" or "Png" or "Jpeg") Assert.True(artifact.PageCount >= 2);
                        else Assert.Null(artifact.PageCount);
                        Assert.Single(await db.Client.Queryable<StudioReportArtifact>().Where(a => a.RunId == run.Id && a.Purpose == "DataSnapshot").ToListAsync());
                    }
                    if (format == "Xlsx") { bytes.Position = 0; StudioWorkbooks.ValidateXlsx(bytes); }
                    if (format == "Xlsx")
                    {
                        var books = scope.ServiceProvider.GetRequiredService<StudioWorkbooks>();
                        Assert.Equal("WORKBOOK_SCAN_PENDING", (await Assert.ThrowsAsync<StudioException>(() => books.ImportArtifactAsync(p, "Report working copy", artifact.NId, "report-copy", outputs, default))).Code);
                        foreach (var pending in await files.ListPendingScanAsync(100, default)) await files.UpdateFileAsync(pending with { ScanStatus = "Clean" }, default);
                        var workbook = await books.ImportArtifactAsync(p, "Report working copy", artifact.NId, "report-copy", outputs, default);
                        Assert.Equal("1", workbook.ContentVersion); Assert.Equal(artifact.Sha256, (await books.VersionsAsync(p, workbook.NId, default)).Single().Sha256);
                        await books.ArchiveAsync(p, workbook.NId, new(workbook.OptimisticVersion, workbook.ConcurrencyVersion), default);
                        Assert.Equal(artifact.Sha256, (await db.Client.Queryable<StudioReportArtifact>().Where(a => a.NId == artifact.NId).FirstAsync()).Sha256);
                    }
                    Assert.Equal(2, await db.Client.Queryable<StudioReportArtifact>().Where(a => a.RunId == run!.Id).CountAsync());
                    await Assert.ThrowsAsync<StudioException>(() => outputs.ArtifactAsync(p with { TenantNId = "other" }, state.RunNId, artifact.NId, default));
                    if (format == "Pdf")
                    {
                        // Simulate a crash after owned-file confirmation but before the local report commit was observed.
                        var schedulerStore = provider.GetRequiredService<ISchedulerStore>();
                        var native = (await schedulerStore.GetRunAsync(p.TenantNId, run!.SchedulerRunNId!, default))!;
                        for (var wait = 0; wait < 100 && native.Status != "Succeeded"; wait++) { await Task.Delay(100); native = (await schedulerStore.GetRunAsync(p.TenantNId, run.SchedulerRunNId!, default))!; }
                        Assert.Equal("Succeeded", native.Status);
                        Assert.True(await schedulerStore.SaveRunAsync(native with { Status = "Unknown", ActiveJobId = native.JobId, StateVersion = native.StateVersion + 1 }, native.StateVersion, default));
                        await db.Client.Deleteable<StudioReportArtifact>().Where(a => a.RunId == run.Id).ExecuteCommandAsync();
                        await db.Client.Updateable<StudioReportRun>().SetColumns(r => new StudioReportRun { Status = "Unknown", StateVersion = run.StateVersion + 1 }).Where(r => r.Id == run.Id).ExecuteCommandAsync();
                        var unknown = await runs.GetAsync(p, run.NId, default); Assert.Equal("Unknown", unknown.Status);
                        var directory = StudioJson.RequiredString(JsonNode.Parse(run.ProgressJson)!["rendererEvidence"]!.AsObject(), "directory", 4096); var receiptPath = Path.Combine(directory, "renderer-stopped.json"); var validReceipt = await File.ReadAllTextAsync(receiptPath);
                        var tampered = JsonNode.Parse(validReceipt)!.AsObject(); tampered["inputSha256"] = new string('0', 64); await File.WriteAllTextAsync(receiptPath, tampered.ToJsonString());
                        var reconciliation = scope.ServiceProvider.GetRequiredService<ReportReconciliation>();
                        Assert.Equal("REPORT_RESOLUTION_UNCONFIRMED", (await Assert.ThrowsAsync<StudioException>(() => reconciliation.ResolveAsync(p, run.NId, unknown.StateVersion, "集成夹具核实原渲染已停止且文件完整", default))).Code);
                        Assert.NotNull((await schedulerStore.GetRunAsync(p.TenantNId, native.NId, default))!.ActiveJobId);
                        await File.WriteAllTextAsync(receiptPath, validReceipt);
                        var resolved = await reconciliation.ResolveAsync(p, run.NId, unknown.StateVersion, "集成夹具核实原渲染已停止且文件完整", default); Assert.Equal("Succeeded", resolved.Status);
                        Assert.Equal(2, await db.Client.Queryable<StudioReportArtifact>().Where(a => a.RunId == run.Id).CountAsync());
                        Assert.Null((await schedulerStore.GetRunAsync(p.TenantNId, native.NId, default))!.ActiveJobId);
                        // Retry the trusted native decision after a hypothetical cross-store reply loss; a different proof must not replay.
                        var resolvedNative = (await schedulerStore.GetRunAsync(p.TenantNId, native.NId, default))!; using var resolutionJson = JsonDocument.Parse(resolvedNative.ResultSummaryJson!);
                        var proof = resolutionJson.RootElement.GetProperty("resolution").GetProperty("evidenceReference").GetString()!;
                        var trusted = new TrustedServiceContext(p.TenantNId, "platform-studio", p.SubjectNId); var submission = provider.GetRequiredService<IScheduledTaskSubmission>();
                        Assert.Equal("Succeeded", (await submission.ResolveOnceAsync(trusted, run.OperationKey, new("0", "ConfirmedSucceeded", "集成夹具验证跨库回执丢失的相同核对重放", proof), default)).Status);
                        await Assert.ThrowsAsync<IndustrialPlatform.SystemData.Application.Operations.SystemDataOperationException>(() => submission.ResolveOnceAsync(trusted, run.OperationKey, new("0", "ConfirmedSucceeded", "集成夹具拒绝不同核对依据进行重放", "wrong-proof"), default));
                    }
                }
            }
            finally { using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await worker.StopAsync(shutdown.Token); }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
