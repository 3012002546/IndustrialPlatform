using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;
using IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class ReportRunFoundationTests
{
    private static FrozenReportInput Input => new("Preview", null, "{\"kind\":\"Report\",\"document\":{\"schemaVersion\":1},\"dependencySnapshots\":[]}", "{}", "Html", "trace-a");
    private sealed class Identity(StudioPrincipal principal) : IStudioIdentity
    {
        public StudioPrincipal Principal { get; set; } = principal;
        public Task<StudioPrincipal> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Principal);
        public Task<StudioPrincipal> RefreshAsync(StudioPrincipal principal, CancellationToken cancellationToken) => Task.FromResult(Principal);
        public Task<string> CaptureExecutionEvidenceAsync(StudioPrincipal principal, CancellationToken cancellationToken) => Task.FromResult("fixture-session");
        public Task<StudioPrincipal> RefreshExecutionAsync(string evidence, CancellationToken cancellationToken) => Task.FromResult(Principal);
    }
    private sealed class Jobs : IStudioJobs
    {
        public int Submissions { get; private set; }
        public bool Disabled { get; set; }
        public bool LoseReply { get; set; }
        public ReportJobState? Current { get; set; }
        public Task<ReportJobState?> GetByOperationAsync(StudioReportRun run, CancellationToken token) => Task.FromResult(Current);
        public Task<ReportJobState> SubmitOnceAsync(StudioReportRun run, CancellationToken token)
        {
            if (Disabled) throw new StudioException("STUDIO_JOBS_UNAVAILABLE", "disabled", 503);
            Submissions++;
            Current = new("scheduler-one", "Queued", "0", run.OperationKey, null, null, null);
            if (LoseReply) throw new IOException("reply lost after durable acceptance");
            return Task.FromResult(Current);
        }
        public Task<ReportJobState> RequestCancelAsync(StudioReportRun run, string expectedStateVersion, CancellationToken token)
        {
            Current = Current! with { Status = Current!.Status == "Running" ? "CancelRequested" : "Cancelled", StateVersion = "2" };
            return Task.FromResult(Current);
        }
    }
    private static ReportRuns Service(StudioTestContext fixture, Identity identity, Jobs jobs) => new(new ReportRunStore(fixture.Database, fixture.Store), fixture.Store, identity, fixture.Authorization, jobs, TimeProvider.System);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepted_run_preserves_resolved_default_or_explicit_empty_sort(bool clear)
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var document = System.Text.Json.Nodes.JsonNode.Parse("""{"mode":"Query","query":{"columns":[{"columnId":"amount","sortable":true}],"defaultSort":[{"fieldId":"amount","direction":"Desc","nulls":"Last"}]}}""")!.AsObject();
        var sort = ReportDesign.ResolveSort(document, clear ? new() : null);
        var service = Service(fixture, new Identity(fixture.Principal), new Jobs());
        var accepted = await service.AcceptFrozenAsync(fixture.Principal, Input with { SortJson = sort.ToJsonString() }, "sort-accept", default);
        var stored = (await new ReportRunStore(fixture.Database, fixture.Store).FindAsync(fixture.Principal.TenantNId, accepted.RunNId, default))!;
        var frozenSort = System.Text.Json.Nodes.JsonNode.Parse(stored.AccessSnapshotJson)!["executionSort"]!.AsArray();
        if (clear) Assert.Empty(frozenSort);
        else Assert.Equal("Desc", StudioJson.Text(Assert.Single(frozenSort)!["direction"]));
    }

    [Fact]
    public async Task Queued_run_uses_sql_null_for_absent_times_and_claim_preserves_utc_time()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var updates = new List<string>();
        fixture.Database.Client.Aop.OnLogExecuting = (sql, _) =>
        {
            if (sql.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && sql.Contains("started_on", StringComparison.Ordinal)) updates.Add(sql);
        };
        var jobs = new Jobs();
        var service = Service(fixture, new Identity(fixture.Principal), jobs);
        var accepted = await service.AcceptFrozenAsync(fixture.Principal, Input, "typed-null-times", default);
        var sql = Assert.Single(updates).Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);
        Assert.Matches(@"(?i)started_on\s*=\s*NULL", sql);
        Assert.Matches(@"(?i)completed_on\s*=\s*NULL", sql);
        var started = new DateTimeOffset(2026, 10, 29, 7, 0, 0, TimeSpan.Zero);
        jobs.Current = jobs.Current! with { Status = "Running", StartedOn = started };
        var claimed = await service.BeginExecutionAsync("tenant-a", accepted.RunNId, "scheduler-one", "typed-null-times", default);
        Assert.Equal(started, claimed!.StartedOn);
        Assert.Null(claimed.CompletedOn);
        jobs.Current = jobs.Current with { Status = "Failed", CompletedOn = started.AddMinutes(1) };
        await service.GetAsync(fixture.Principal, accepted.RunNId, default);
        var stored = await new ReportRunStore(fixture.Database, fixture.Store).FindAsync("tenant-a", accepted.RunNId, default);
        Assert.Equal(started, stored!.StartedOn);
        Assert.Equal(started.AddMinutes(1), stored.CompletedOn);
    }

    [Fact]
    public async Task Embedded_jobs_initialize_same_storage_and_resolve_worker_without_platform_control_plane()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-jobs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var profile = StudioInitializationTests.Profile(Path.Combine(root, "studio.db"));
            profile.InstalledModules = ["Report"]; profile.Jobs.Provider = "Embedded";
            profile.Files = new() { Provider = "Embedded", Root = Path.Combine(root, "files") };
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            using var database = new IndustrialPlatform.PlatformStudio.Infrastructure.Persistence.StudioDatabase(profile, configuration);
            var initializer = new IndustrialPlatform.PlatformStudio.Infrastructure.Initialization.StudioServiceInitializer(database, profile, TimeProvider.System);
            await initializer.InitializeLocalAsync(default); await initializer.InitializeLocalAsync(default);
            Assert.True(File.Exists(profile.Database!.SqliteFile + ".scheduler.db"));
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddLogging(); services.AddDataProtection(); services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
            services.AddSingleton(profile); services.AddSingleton(database); services.AddSingleton(TimeProvider.System);
            services.AddStudioEmbeddedFileAudit(profile); services.AddStudioJobs(profile);
            await using var provider = services.BuildServiceProvider();
            Assert.Single(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<IndustrialPlatform.RuntimeSupport.Jobs.SchedulerWorker>());
            var store = provider.GetRequiredService<IndustrialPlatform.SystemData.Application.Scheduler.ISchedulerStore>();
            Assert.Empty(await store.ListJobsAsync("tenant-a", default));
            Assert.DoesNotContain(provider.GetServices<IndustrialPlatform.SystemData.Contracts.Scheduler.IScheduledTaskHandler>(), item => item.Definition.HandlerKey == "systemdata.reconcile");
            profile.EnabledModules = []; profile.Jobs.Provider = "Disabled";
            var stopped = new IndustrialPlatform.PlatformStudio.Infrastructure.Initialization.StudioServiceInitializer(database, profile, TimeProvider.System);
            Assert.True((await stopped.InspectLocalAsync(default)).Ready);
            await stopped.InitializeLocalAsync(default);
            var disabledServices = new ServiceCollection(); disabledServices.AddStudioJobs(profile);
            Assert.Empty(disabledServices);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Frozen_preview_is_persistent_idempotent_and_not_a_published_revision()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var jobs = new Jobs(); var identity = new Identity(fixture.Principal); var service = Service(fixture, identity, jobs);
        var first = await service.AcceptFrozenAsync(fixture.Principal, Input, "preview-once", default);
        var replay = await service.AcceptFrozenAsync(fixture.Principal, Input with { TraceId = "trace-b" }, "preview-once", default);
        Assert.Equal(first.RunNId, replay.RunNId); Assert.Equal("Queued", first.Status); Assert.Equal(1, jobs.Submissions);
        var run = (await new ReportRunStore(fixture.Database, fixture.Store).FindAsync("tenant-a", first.RunNId, default))!;
        Assert.Null(run.TemplateRevisionId); Assert.Null(run.PreviewManifestJson); Assert.Equal(DateTimeOffset.MaxValue, run.PreviewExpiresOn); Assert.Null(first.ExpiresOn);
        Assert.Equal(0, await fixture.Database.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM studio_report_artifact"));
        Assert.Equal(0, await fixture.Database.Client.Ado.GetIntAsync("SELECT COUNT(*) FROM studio_revision"));
        await Assert.ThrowsAsync<StudioException>(() => service.AcceptFrozenAsync(fixture.Principal, Input with { ParametersJson = "{\"different\":true}" }, "preview-once", default));
    }

    [Fact]
    public async Task Disabled_jobs_keep_pending_and_lost_reply_reconciles_same_operation()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var jobs = new Jobs { Disabled = true }; var identity = new Identity(fixture.Principal); var service = Service(fixture, identity, jobs);
        await Assert.ThrowsAsync<StudioException>(() => service.AcceptFrozenAsync(fixture.Principal, Input, "pending-once", default));
        var store = new ReportRunStore(fixture.Database, fixture.Store);
        Assert.Equal("PendingDispatch", (await store.FindOperationAsync("tenant-a", "pending-once", default))!.Status);
        jobs.Disabled = false; jobs.LoseReply = true;
        await Assert.ThrowsAsync<IOException>(() => service.AcceptFrozenAsync(fixture.Principal, Input, "pending-once", default));
        var replay = await service.AcceptFrozenAsync(fixture.Principal, Input, "pending-once", default);
        Assert.Equal("Queued", replay.Status); Assert.Equal(1, jobs.Submissions);
    }

    [Fact]
    public async Task Actual_claim_increments_epoch_once_and_late_result_cannot_clear_unknown()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var jobs = new Jobs(); var identity = new Identity(fixture.Principal); var service = Service(fixture, identity, jobs);
        var accepted = await service.AcceptFrozenAsync(fixture.Principal, Input, "claim-once", default);
        jobs.Current = jobs.Current! with { Status = "Running", StartedOn = DateTimeOffset.UtcNow };
        var claim = await service.BeginExecutionAsync("tenant-a", accepted.RunNId, "scheduler-one", "claim-once", default);
        Assert.NotNull(claim); Assert.Equal(1, claim.ExecutionEpoch);
        Assert.Null(await service.BeginExecutionAsync("tenant-a", accepted.RunNId, "scheduler-one", "claim-once", default));
        jobs.Current = jobs.Current with { Status = "Unknown", StateVersion = "2", ReasonCode = "WORKER_LOST" };
        Assert.Equal("Unknown", (await service.GetAsync(fixture.Principal, accepted.RunNId, default)).Status);
        Assert.False(await service.FinishUnavailableAsync(claim, false, default));
        jobs.Current = jobs.Current with { Status = "Failed" };
        Assert.Equal("Unknown", (await service.GetAsync(fixture.Principal, accepted.RunNId, default)).Status);
    }

    [Fact]
    public async Task Running_cancel_is_only_requested_and_queued_cancel_is_confirmed()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var jobs = new Jobs(); var identity = new Identity(fixture.Principal); var service = Service(fixture, identity, jobs);
        var accepted = await service.AcceptFrozenAsync(fixture.Principal, Input, "cancel-once", default);
        jobs.Current = jobs.Current! with { Status = "Running", StartedOn = DateTimeOffset.UtcNow };
        var claim = await service.BeginExecutionAsync("tenant-a", accepted.RunNId, "scheduler-one", "cancel-once", default);
        var requested = await service.CancelAsync(fixture.Principal, accepted.RunNId, ReportRuns.State(claim!).StateVersion, default);
        Assert.Equal("CancelRequested", requested.Status);
        Assert.True(await service.FinishUnavailableAsync(claim!, true, default));
        Assert.Equal("Cancelled", (await service.GetAsync(fixture.Principal, accepted.RunNId, default)).Status);
    }

    [Fact]
    public async Task Legacy_expiry_does_not_block_execution_but_revoked_scope_is_denied()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var jobs = new Jobs(); var identity = new Identity(fixture.Principal); var service = Service(fixture, identity, jobs);
        var accepted = await service.AcceptFrozenAsync(fixture.Principal, Input, "unknown-expiry", default);
        await fixture.Database.Client.Ado.ExecuteCommandAsync("UPDATE studio_report_run SET preview_expires_on=@expires WHERE n_id=@id", new { expires = DateTimeOffset.UtcNow.AddMinutes(-1), id = accepted.RunNId });
        jobs.Current = jobs.Current! with { Status = "Running", StartedOn = DateTimeOffset.UtcNow };
        Assert.NotNull(await service.BeginExecutionAsync("tenant-a", accepted.RunNId, "scheduler-one", "unknown-expiry", default));
        Assert.Null((await service.GetAsync(fixture.Principal, accepted.RunNId, default)).ExpiresOn);
        jobs.Current = jobs.Current! with { Status = "Unknown" };
        Assert.Equal("Unknown", (await service.GetAsync(fixture.Principal, accepted.RunNId, default)).Status);
        identity.Principal = fixture.Principal with { FactoryNIds = [] };
        await Assert.ThrowsAsync<StudioException>(() => service.GetAsync(fixture.Principal, accepted.RunNId, default));
        identity.Principal = fixture.Principal with { TenantNId = "tenant-b" };
        await Assert.ThrowsAsync<StudioException>(() => service.GetAsync(identity.Principal, accepted.RunNId, default));
    }
}
