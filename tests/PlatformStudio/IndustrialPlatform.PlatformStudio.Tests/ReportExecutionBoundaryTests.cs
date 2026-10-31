using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;
using IndustrialPlatform.RuntimeSupport.Files;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class ReportExecutionBoundaryTests
{
    private sealed class Identity(StudioPrincipal principal) : IStudioIdentity
    {
        public int Refreshes { get; set; }
        public int ExecutionRefreshes { get; private set; }
        public int RevokeAfter { get; set; } = int.MaxValue;
        public Task<StudioPrincipal> GetCurrentAsync(CancellationToken token) => Task.FromResult(principal);
        public Task<StudioPrincipal> RefreshAsync(StudioPrincipal original, CancellationToken token) { Refreshes++; return Task.FromResult(principal); }
        public Task<string> CaptureExecutionEvidenceAsync(StudioPrincipal original, CancellationToken token) => Task.FromResult("boundary-session");
        public Task<StudioPrincipal> RefreshExecutionAsync(string evidence, CancellationToken token)
        {
            Assert.Equal("boundary-session", evidence);
            return Task.FromResult(++ExecutionRefreshes > RevokeAfter ? principal with { FactoryNIds = [] } : principal);
        }
    }
    private sealed class Jobs : IStudioJobs
    {
        public ReportJobState? Current { get; set; }
        public Task<ReportJobState?> GetByOperationAsync(StudioReportRun run, CancellationToken token) => Task.FromResult(Current);
        public Task<ReportJobState> SubmitOnceAsync(StudioReportRun run, CancellationToken token) => Task.FromResult(Current = new("boundary-job", "Queued", "0", run.OperationKey, null, null, null));
        public Task<ReportJobState> RequestCancelAsync(StudioReportRun run, string version, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class OwnedFiles : IOwnedFileService
    {
        public HashSet<string> References { get; } = ["output-reference", "snapshot-reference"];
        public int Reads { get; private set; }
        public int Tickets { get; private set; }
        public Task<OwnedFileListV1> ListByOwnerAsync(TrustedServiceContext context, string kind, string owner, string? cursor, CancellationToken token)
            => Task.FromResult(new OwnedFileListV1(References.Select(id => new OwnedFileStatusV1(id, "file", "Active", "1", null)).ToArray(), null));
        public Task<OwnedFileReadTicketV1> GetReadTicketAsync(TrustedServiceContext context, string reference, CancellationToken token) { Tickets++; return Task.FromResult(new OwnedFileReadTicketV1("ticket", DateTimeOffset.UtcNow.AddMinutes(1))); }
        public Task<Stream> ReadAsync(TrustedServiceContext context, OwnedFileReadV1 request, CancellationToken token) { Reads++; Assert.Equal("ticket", request.AuthorizationTicket); return Task.FromResult<Stream>(new MemoryStream("[]"u8.ToArray())); }
        public Task<OwnedFilePreparedV1> PrepareAsync(TrustedServiceContext context, OwnedFilePrepareV1 request, CancellationToken token) => throw new InvalidOperationException("Unexpected file confirmation.");
        public Task<OwnedFileStatusV1> CommitAsync(TrustedServiceContext context, OwnedFileCommitV1 request, CancellationToken token) => throw new NotSupportedException();
        public Task<OwnedFileStatusV1> GetStatusAsync(TrustedServiceContext context, string reference, CancellationToken token) => throw new NotSupportedException();
        public Task<OwnedFileStatusV1> ReleaseAsync(TrustedServiceContext context, OwnedFileReleaseV1 request, CancellationToken token) => throw new NotSupportedException();
        public Task<OwnedFileStatusV1> SetRetentionAsync(TrustedServiceContext context, OwnedFileRetentionV1 request, CancellationToken token) => throw new NotSupportedException();
    }
    private static JsonObject Document() => JsonNode.Parse("""
        {"schemaVersion":1,"mode":"Fixed","parameters":[],"instances":[{"instanceId":"rows","sourceType":"Constant","parameters":[],"value":[{"value":"FIRST-ROW"},{"value":"SECOND-ROW"}]}],"paper":{"size":"A4","orientation":"Portrait","margin":{"top":15,"right":15,"bottom":15,"left":15},"headerHeightMm":15,"footerHeightMm":10},"bands":[{"id":"detail","kind":"Detail","name":"Detail","heightMm":8,"data":{"mode":"RootPath","instanceId":"rows","path":[]},"controls":[{"id":"table","type":"Table","xMm":0,"yMm":0,"widthMm":180,"heightMm":8,"canGrow":true,"table":{"rowMode":"BandRow","columns":[{"id":"value","label":"Value","widthMm":180,"value":{"from":"Row","path":["value"],"nullPolicy":"Error"}}],"headerMode":"OncePerParent","repeatHeader":true}}]}],"styles":[],"assets":[]}
        """)!.AsObject();
    private static ReportDesign Design(StudioTestContext fixture) => new(fixture.Store, fixture.Authorization, fixture.Publishing,
        new DatasetExecution(fixture.Store, fixture.Publishing, fixture.Authorization, fixture.SourceExecutor, new(), TimeProvider.System));
    private static ReportRuns Runs(StudioTestContext fixture, Identity identity, Jobs jobs) => new(new ReportRunStore(fixture.Database, fixture.Store), fixture.Store, identity, fixture.Authorization, jobs, TimeProvider.System);
    private static async Task<StudioReportRun> ClaimAsync(StudioTestContext fixture, ReportRuns runs, Jobs jobs, FrozenReportInput input)
    {
        var accepted = await runs.AcceptFrozenAsync(fixture.Principal, input, "boundary-run", default);
        jobs.Current = jobs.Current! with { Status = "Running", StartedOn = DateTimeOffset.UtcNow };
        return (await runs.BeginExecutionAsync(fixture.Principal.TenantNId, accepted.RunNId, "boundary-job", "boundary-run", default))!;
    }

    [Fact]
    public async Task Detail_download_and_retry_snapshot_reuse_one_authorized_run_and_keep_file_ownership_check()
    {
        await using var fixture = await StudioTestContext.CreateAsync(); fixture.Profile.InstalledModules = ["Report"];
        var document = Document(); var p = fixture.Principal;
        var resource = await fixture.Publishing.CreateAsync(p, new("boundary-report", "Report", "Boundary report", null, null, document), "create-boundary", default);
        var validation = await fixture.Publishing.ValidateAsync(p, resource.NId, document, default);
        var published = await fixture.Publishing.PublishAsync(p, resource.NId, new(resource.OptimisticVersion, resource.ConcurrencyVersion, "Boundary fixture", true, validation.DependencyPlanHash), "publish-boundary", default);
        var revision = (await fixture.Store.FindRevisionAsync(p.TenantNId, published.NId, default))!;
        var identity = new Identity(p); var jobs = new Jobs(); var runs = Runs(fixture, identity, jobs);
        var claim = await ClaimAsync(fixture, runs, jobs, new("Production", revision.Id, null, "{}", "Html", "boundary"));
        claim.Status = "Succeeded"; claim.SourceManifestJson = "[]"; claim.CompletedOn = DateTimeOffset.UtcNow;
        claim.ProgressJson = "{\"stage\":\"FileConfirmed\",\"dataSnapshot\":{\"referenceNId\":\"snapshot-reference\"}}";
        var artifact = new StudioReportArtifact { NId = "boundary-artifact", TenantNId = p.TenantNId, RunId = claim.Id, Purpose = "Output", Format = "Html", FileNId = "file", FileReferenceNId = "output-reference", Sha256 = new string('a', 64), ContentLength = 2, PageCount = 1, EngineManifestJson = "{}", CreatedOn = DateTimeOffset.UtcNow };
        Assert.True(await new ReportRunStore(fixture.Database, fixture.Store).CompleteAsync(claim, [artifact], default));
        var owned = new OwnedFiles(); var authorization = new ReportOwnedFiles(runs, fixture.Authorization, fixture.Store, owned);
        var outputs = new ReportOutputs(runs, authorization, fixture.Database, fixture.Store);
        identity.Refreshes = 0;
        var detail = JsonSerializer.SerializeToNode(await outputs.DetailAsync(p, claim.NId, default), StudioCommands.JsonOptions)!;
        Assert.Equal("Succeeded", detail["status"]!.GetValue<string>()); Assert.Single(detail["artifacts"]!.AsArray()); Assert.Equal(1, identity.Refreshes);
        identity.Refreshes = 0;
        var output = await outputs.ArtifactAsync(p, claim.NId, artifact.NId, default); await output.Content.DisposeAsync();
        Assert.Equal(1, identity.Refreshes); Assert.Equal(1, owned.Reads); Assert.Equal(1, owned.Tickets);
        identity.Refreshes = 0;
        await using (var snapshot = await authorization.ReadRetrySnapshotAsync(p, claim.NId, "snapshot-reference", default)) Assert.Equal(2, snapshot.Length);
        Assert.Equal(1, identity.Refreshes);
        owned.References.Remove("output-reference");
        var denied = await Assert.ThrowsAsync<StudioException>(() => outputs.ArtifactAsync(p, claim.NId, artifact.NId, default));
        Assert.Equal("STUDIO_NOT_FOUND", denied.Code); Assert.Equal(2, owned.Tickets); Assert.Equal(2, owned.Reads);
    }

    [ReportEngineFact]
    public async Task Worker_rechecks_original_factory_scope_after_render_before_confirming_files()
    {
        await using var fixture = await StudioTestContext.CreateAsync(); fixture.Profile.InstalledModules = ["Report"];
        using var command = new CommandFixture("Completed");
        var identity = new Identity(fixture.Principal); var jobs = new Jobs(); var runs = Runs(fixture, identity, jobs); var design = Design(fixture);
        var frozen = await design.FreezeAsync(fixture.Principal, new("Report", null, Document(), new()), default);
        var claim = await ClaimAsync(fixture, runs, jobs, new("Preview", null, frozen.Frozen.ToJsonString(), "{}", "Html", "boundary"));
        identity.RevokeAfter = identity.ExecutionRefreshes + 1;
        var owned = new OwnedFiles(); var ownedFiles = new ReportOwnedFiles(runs, fixture.Authorization, fixture.Store, owned);
        var resources = new ReportResources(identity, fixture.Publishing, null!, null!, null!, owned);
        var execution = new ReportExecution(new ReportRunStore(fixture.Database, fixture.Store), fixture.Store, runs, fixture.Authorization,
            fixture.Publishing, design, command.Renderer, owned, null!, TimeProvider.System, new ReportRetries(runs, ownedFiles, fixture.Database), resources);
        var result = await execution.ExecuteAsync(claim, default);
        Assert.Equal("Failed", result.Outcome);
        var stored = (await new ReportRunStore(fixture.Database, fixture.Store).FindAsync(claim.TenantNId, claim.NId, default))!;
        Assert.Equal("STUDIO_FORBIDDEN", stored.ErrorCode);
        Assert.Single(Directory.GetFiles(command.WorkingRoot, "result.json", SearchOption.AllDirectories));
        Assert.Equal(0, await fixture.Database.Client.Queryable<StudioReportArtifact>().CountAsync());
    }

    [ReportEngineFact]
    public async Task Direct_preview_applies_default_sort_without_mutating_draft_and_cleans_confirmed_failures()
    {
        await using var fixture = await StudioTestContext.CreateAsync(); fixture.Profile.InstalledModules = ["Report"];
        var identity = new Identity(fixture.Principal); var design = Design(fixture);
        var resources = new ReportResources(identity, fixture.Publishing, null!, null!, null!, null!);
        var document = Document(); document["mode"] = "Query";
        document["query"] = JsonNode.Parse("""{"instanceId":"rows","filters":[],"resultBandId":"detail","columns":[{"controlId":"table","columnId":"value","visible":true,"sortable":true}],"defaultSort":[{"fieldId":"value","direction":"Desc","nulls":"Last"}],"defaultPageSize":20}""");
        var original = document.ToJsonString();
        using (var command = new CommandFixture("Completed"))
        {
            var preview = new ReportPreview(design, identity, command.Renderer, resources);
            var result = await preview.RenderAsync(fixture.Principal, new("Report", null, document, new()), default);
            var html = Assert.Single(result.Pages).Html;
            Assert.Contains("FIRST-ROW", html, StringComparison.Ordinal); Assert.Contains("SECOND-ROW", html, StringComparison.Ordinal);
            Assert.True(html.IndexOf("SECOND-ROW", StringComparison.Ordinal) < html.IndexOf("FIRST-ROW", StringComparison.Ordinal));
            Assert.Equal(original, document.ToJsonString()); Assert.Empty(Directory.GetDirectories(command.WorkingRoot));
        }
        foreach (var reason in new[] { "Failed", "TimedOut", "Cancelled" })
        {
            using var command = new CommandFixture(reason);
            var preview = new ReportPreview(design, identity, command.Renderer, resources);
            if (reason == "Cancelled") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preview.RenderAsync(fixture.Principal, new("Report", null, Document(), new()), default));
            else Assert.Equal(reason == "TimedOut" ? "REPORT_RENDER_TIMEOUT" : "REPORT_LAYOUT_UNSUPPORTED", (await Assert.ThrowsAsync<StudioException>(() => preview.RenderAsync(fixture.Principal, new("Report", null, Document(), new()), default))).Code);
            Assert.Empty(Directory.GetDirectories(command.WorkingRoot));
        }
    }

    [ReportEngineFact]
    public async Task Renderer_notifies_confirmed_cancellation_and_rejects_missing_or_mismatched_stop_evidence()
    {
        using (var command = new CommandFixture("AwaitCancel"))
        {
            using var cancellation = new CancellationTokenSource(); var stopped = false;
            var workspace = command.Renderer.CreateWorkspace("cancel");
            var rendering = command.Renderer.RenderAsync("cancel", new([], []), new(), true, cancellation.Token, "Html", workspace, confirmedStopped: () => stopped = true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(Path.Combine(workspace, "ready"))) { deadline.Token.ThrowIfCancellationRequested(); await Task.Delay(20, deadline.Token); }
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rendering); Assert.True(stopped);
        }
        foreach (var reason in new[] { "Missing", "Mismatch" })
        {
            using var command = new CommandFixture(reason); var stopped = false;
            var workspace = command.Renderer.CreateWorkspace("unconfirmed");
            await Assert.ThrowsAsync<ReportRendererUnconfirmedException>(() => command.Renderer.RenderAsync("unconfirmed", new([], []), new(), true, default, "Html", workspace, confirmedStopped: () => stopped = true));
            Assert.False(stopped); Assert.True(File.Exists(Path.Combine(workspace, "input.json")));
        }
    }

    // Synthetic renderer validates the host process/receipt boundary, not pagination correctness.
    // Real Paged.js output remains covered by ReportEngineIntegrationTests.
    private sealed class CommandFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "studio-render-boundary-" + Guid.NewGuid().ToString("N"));
        public string WorkingRoot => Path.Combine(root, "working");
        public ReportCommandRenderer Renderer { get; }
        public CommandFixture(string reason)
        {
            Directory.CreateDirectory(root);
            var node = Path.GetFullPath(Environment.GetEnvironmentVariable("PF09_RENDERER_TEST_NODE")!);
            var script = Path.Combine(root, "renderer.mjs"); var manifest = Path.Combine(root, "manifest.json");
            File.WriteAllText(manifest, "{}");
            File.WriteAllText(script, """
                import { readFile, writeFile } from 'node:fs/promises';
                import { existsSync } from 'node:fs';
                import { createHash } from 'node:crypto';
                import { join } from 'node:path';
                let reason = '$REASON$';
                const output = process.argv[process.argv.indexOf('--output') + 1];
                const inputBytes = await readFile(process.argv[process.argv.indexOf('--input') + 1]);
                const input = JSON.parse(inputBytes);
                if (reason === 'AwaitCancel') {
                  await writeFile(join(output, 'ready'), 'ready');
                  while (!existsSync(join(output, 'cancel.request'))) await new Promise(resolve => setTimeout(resolve, 10));
                  reason = 'Cancelled';
                }
                if (reason === 'Completed') {
                  await writeFile(join(output, 'page.html'), input.html);
                  await writeFile(join(output, 'result.json'), JSON.stringify({engineManifestHash: input.engineManifestHash, pages: [{pageNo: 1, htmlFile: 'page.html'}], markerLedger: input.expectedMarkers.map(marker => ({marker, fragmentIndex: 0})), diagnostics: []}));
                }
                if (reason !== 'Missing') await writeFile(join(output, 'renderer-stopped.json'), JSON.stringify({engineManifestHash: input.engineManifestHash, inputSha256: reason === 'Mismatch' ? 'wrong' : createHash('sha256').update(inputBytes).digest('hex'), processId: process.pid, reason: reason === 'Mismatch' ? 'Completed' : reason, errorCode: 'REPORT_LAYOUT_UNSUPPORTED'}));
                process.exitCode = reason === 'Cancelled' ? 2 : reason === 'Completed' ? 0 : 1;
                """.Replace("$REASON$", reason, StringComparison.Ordinal));
            static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            var profile = StudioInitializationTests.Profile(Path.Combine(root, "unused.db"));
            profile.Renderer = new() { Admitted = true, Command = node, CommandSha256 = Hash(node), Script = script, ScriptSha256 = Hash(script), EngineManifestPath = manifest, EngineManifestHash = Hash(manifest), WorkingRoot = WorkingRoot, TimeoutSeconds = 15 };
            Renderer = new(profile);
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
