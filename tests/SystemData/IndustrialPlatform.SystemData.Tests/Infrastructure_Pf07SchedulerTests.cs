using System.Text.Json;
using Hangfire;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Operations;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Application.Scheduler;
using IndustrialPlatform.SystemData.Contracts.Auditing;
using IndustrialPlatform.SystemData.Contracts.Scheduler;
using IndustrialPlatform.SystemData.Domain.Scheduler;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.SystemData.Infrastructure.Reliability;
using IndustrialPlatform.SystemData.Infrastructure.Scheduler;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

/// <summary>真实文件库、数据库唯一活动槽和原生队列；不以伪造运行成功验证执行器。</summary>
public sealed partial class Pf07SchedulerTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "pf07-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqlSugarDbContext _database;
    private readonly SqlSchedulerStore _store;
    private readonly HangfireSchedulerEngine _engine;
    private readonly RecordingAudit _audit = new();
    private readonly TestHandler _handler = new();
    private readonly SchedulerAdministration _administration;
    private readonly SqlSystemDataWriteTransaction _transaction;
    public Pf07SchedulerTests()
    {
        SQLitePCL.Batteries_V2.Init();
        var databaseOptions = Options.Create(new SqlSugarOptions { DbType = DbType.Sqlite, ConnectionString = "Data Source=" + _file });
        _database = new(databaseOptions); _store = new(_database); _transaction = new(_database);
        _engine = new(Options.Create(new SchedulerOptions { Enabled = true }), _database, databaseOptions, _store);
        _administration = new(_store, _engine, [_handler], _audit, new InMemoryControlPlaneOutbox(), _transaction, TimeProvider.System);
    }
    private async Task InitializeAsync()
    {
        foreach (var step in SchedulerSchemaMigrations.All) await step.Apply(_database.SqlSugar, CancellationToken.None);
    }
    private async Task<SchedulerJobDto> CreateAsync(int attempts = 1)
    {
        using var json = JsonDocument.Parse("{\"schemaVersion\":1}");
        var job = await _administration.CreateAsync("tenant-a", "admin", "trace-a", new SchedulerJobCreate
        { NId = "test.job", Name = "真实队列任务", HandlerKey = _handler.Definition.HandlerKey, Parameters = json.RootElement.Clone(), MaxAttempts = attempts }, CancellationToken.None);
        return await _administration.SetStateAsync("tenant-a", "admin", "trace-a", job.NId, "enable", new(job.OptimisticVersion, job.ConcurrencyVersion), CancellationToken.None);
    }
    [Fact]
    public void Runtime_storage_rejects_missing_file_without_creating_database()
    {
        Assert.Throws<InvalidOperationException>(() => SchedulerStorageSchema.OpenExisting(DbType.Sqlite, "Data Source=" + _file));
        Assert.False(File.Exists(_file + ".scheduler.db"));
    }
    [Fact]
    public async Task Manual_request_replays_once_and_conflicting_command_is_rejected()
    {
        await InitializeAsync(); var job = await CreateAsync(); var version = new SchedulerVersionCommand(job.OptimisticVersion, job.ConcurrencyVersion);
        var first = await _administration.TriggerAsync("tenant-a", "admin", "trace-a", job.NId, version, "stable-request", CancellationToken.None);
        var replay = await _administration.TriggerAsync("tenant-a", "admin", "trace-b", job.NId, version, "stable-request", CancellationToken.None);
        Assert.Equal(first.RunNId, replay.RunNId);
        Assert.Single((await _store.ListRunsAsync("tenant-a", CancellationToken.None)));
        Assert.Single(_audit.Facts, fact => fact.Action == "scheduler.run.manual");
        var conflict = await Assert.ThrowsAsync<SystemDataOperationException>(() => _administration.TriggerAsync("tenant-a", "admin", "trace", job.NId, version with { OptimisticVersion = "999" }, "stable-request", CancellationToken.None));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Null(await _store.GetRunAsync("tenant-b", first.RunNId, CancellationToken.None));
    }
    [Fact]
    public async Task Unique_activity_slot_blocks_second_run_until_actual_completion()
    {
        await InitializeAsync(); var job = await CreateAsync();
        var first = await _administration.TriggerAsync("tenant-a", "admin", "trace", job.NId, new(job.OptimisticVersion, job.ConcurrencyVersion), "one", CancellationToken.None);
        var second = await _administration.TriggerAsync("tenant-a", "admin", "trace", job.NId, new(job.OptimisticVersion, job.ConcurrencyVersion), "two", CancellationToken.None);
        var a = (await _store.GetRunAsync("tenant-a", first.RunNId, CancellationToken.None))!;
        var b = (await _store.GetRunAsync("tenant-a", second.RunNId, CancellationToken.None))!;
        Assert.True(await _store.TryStartAsync(a, Guid.NewGuid(), "worker", "native-a", DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.False(await _store.TryStartAsync(b, Guid.NewGuid(), "worker", "native-b", DateTimeOffset.UtcNow, CancellationToken.None));
        a = (await _store.GetRunAsync("tenant-a", a.NId, CancellationToken.None))!;
        Assert.True(await _store.SaveRunAsync(a with { Status = "Unknown", StateVersion = a.StateVersion + 1 }, a.StateVersion, CancellationToken.None));
        Assert.False(await _store.TryStartAsync(b, Guid.NewGuid(), "worker", "native-b", DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));
    }
    [Fact]
    public async Task Native_worker_executes_registered_handler_and_records_actual_attempts()
    {
        await InitializeAsync(); var job = await CreateAsync(2); _handler.FailFirst = true;
        var accepted = await _administration.TriggerAsync("tenant-a", "admin", "trace", job.NId, new(job.OptimisticVersion, job.ConcurrencyVersion), "native-request", CancellationToken.None);
        var run = (await _store.GetRunAsync("tenant-a", accepted.RunNId, CancellationToken.None))!;
        await _engine.EnqueueAsync(run, CancellationToken.None);
        using var server = new BackgroundJobServer(new BackgroundJobServerOptions
        { WorkerCount = 2, Queues = ["default"], SchedulePollingInterval = TimeSpan.FromMilliseconds(100), Activator = new TestActivator(new(_store, _administration, _transaction)) }, _engine.Storage);
        run = await WaitAsync(accepted.RunNId, item => item.Status == "Succeeded", TimeSpan.FromSeconds(60));
        Assert.Equal(2, _handler.Calls); Assert.Equal(2, run.AttemptsUsed); Assert.Null(run.ActiveJobId);
        var attempts = await _engine.GetAttemptsAsync(run.EngineReference, CancellationToken.None);
        Assert.Equal("Available", attempts.Availability); Assert.Equal(2, attempts.Items.Count);
        Assert.Equal("Failed", attempts.Items[0].Status); Assert.Equal("Succeeded", attempts.Items[1].Status);
        Assert.All(attempts.Items, attempt => Assert.NotNull(attempt.CompletedOn));
    }
    [Fact]
    public async Task Cooperative_cancel_stops_actual_handler_and_frees_activity_slot()
    {
        await InitializeAsync(); var job = await CreateAsync(); _handler.WaitForCancellation = true;
        var accepted = await _administration.TriggerAsync("tenant-a", "admin", "trace", job.NId, new(job.OptimisticVersion, job.ConcurrencyVersion), "cancel-request", CancellationToken.None);
        await _engine.EnqueueAsync((await _store.GetRunAsync("tenant-a", accepted.RunNId, CancellationToken.None))!, CancellationToken.None);
        using var server = new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 1, Activator = new TestActivator(new(_store, _administration, _transaction)) }, _engine.Storage);
        var running = await WaitAsync(accepted.RunNId, item => item.Status == "Running", TimeSpan.FromSeconds(20));
        var requested = await _administration.CancelAsync("tenant-a", "admin", "trace", running.NId, new(running.StateVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), CancellationToken.None);
        Assert.Equal("CancelRequested", requested.Status);
        var cancelled = await WaitAsync(running.NId, item => item.Status == "Cancelled", TimeSpan.FromSeconds(20));
        Assert.Null(cancelled.ActiveJobId); Assert.Equal(1, _handler.Calls);
    }
    [Fact]
    public async Task Failed_audit_rolls_back_job_and_outbox_acceptance()
    {
        await InitializeAsync(); _audit.FailWrites = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync());
        Assert.Null(await _store.GetJobAsync("tenant-a", "test.job", CancellationToken.None));
    }
    [Fact]
    public async Task Queued_cancellation_prevents_handler_call_and_consumes_no_attempt()
    {
        await InitializeAsync(); var job = await CreateAsync();
        var accepted = await _administration.TriggerAsync("tenant-a", "admin", "trace", job.NId, new(job.OptimisticVersion, job.ConcurrencyVersion), "queued-cancel", CancellationToken.None);
        var queued = (await _store.GetRunAsync("tenant-a", accepted.RunNId, CancellationToken.None))!;
        var cancelled = await _administration.CancelAsync("tenant-a", "admin", "trace", queued.NId, new("0"), CancellationToken.None);
        Assert.Equal("Cancelled", cancelled.Status); Assert.Equal(0, cancelled.AttemptsUsed);
        Assert.Equal(string.Empty, await _engine.EnqueueAsync((await _store.GetRunAsync("tenant-a", queued.NId, CancellationToken.None))!, CancellationToken.None));
        Assert.Equal(0, _handler.Calls);
    }
    [Fact]
    public async Task Unknown_effect_is_not_retried_and_stopped_execution_can_be_verified()
    {
        await InitializeAsync(); var job = await CreateAsync(3); _handler.ThrowUnknown = true;
        var accepted = await _administration.TriggerAsync("tenant-a", "admin", "trace", job.NId, new(job.OptimisticVersion, job.ConcurrencyVersion), "unknown-effect", CancellationToken.None);
        await _engine.EnqueueAsync((await _store.GetRunAsync("tenant-a", accepted.RunNId, CancellationToken.None))!, CancellationToken.None);
        using var server = new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 1, Activator = new TestActivator(new(_store, _administration, _transaction)) }, _engine.Storage);
        var unknown = await WaitAsync(accepted.RunNId, item => item.Status == "Unknown", TimeSpan.FromSeconds(20));
        Assert.Equal(1, _handler.Calls); Assert.Equal(1, unknown.AttemptsUsed); Assert.NotNull(unknown.ActiveJobId);
        var version = unknown.StateVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rejected = await Assert.ThrowsAsync<SystemDataOperationException>(() => _administration.RetryAsync("tenant-a", "admin", "trace", unknown.NId, new(version), "unsafe-retry", CancellationToken.None));
        Assert.Equal(409, rejected.StatusCode);
        var resolution = await _administration.ResolveAsync("tenant-a", "admin", "trace", unknown.NId, new(version, "ConfirmedNoEffect", "人工核对账务未产生任何业务影响", "evidence:stopped-and-reviewed"), CancellationToken.None);
        Assert.Equal("Failed", resolution.Status); Assert.Null((await _store.GetRunAsync("tenant-a", unknown.NId, CancellationToken.None))!.ActiveJobId);
        var retry = await _administration.RetryAsync("tenant-a", "admin", "trace", unknown.NId, new(resolution.StateVersion), "reviewed-retry", CancellationToken.None);
        var replay = (await _store.GetRunAsync("tenant-a", retry.RunNId, CancellationToken.None))!;
        Assert.Equal(unknown.OperationKey, replay.OperationKey); Assert.Equal(unknown.ParametersJson, replay.ParametersJson); Assert.Equal(0, replay.AttemptsUsed);
    }
    [Fact]
    public async Task Stale_configuration_and_excessive_time_window_are_rejected()
    {
        await InitializeAsync(); var job = await CreateAsync();
        var stale = await Assert.ThrowsAsync<SystemDataOperationException>(() => _administration.SetStateAsync("tenant-a", "admin", "trace", job.NId, "disable", new("999", job.ConcurrencyVersion), CancellationToken.None));
        Assert.Equal(409, stale.StatusCode);
        var range = await Assert.ThrowsAsync<SystemDataOperationException>(() => _administration.RunsAsync("tenant-a", null, null, null, DateTimeOffset.UtcNow.AddDays(-40), null, 1, 20, CancellationToken.None));
        Assert.Equal("SCHEDULER_TIME_RANGE_INVALID", range.Code);
    }
    [Theory]
    [InlineData("0 0 0 * * *", "Etc/UTC")]
    [InlineData("61 * * * *", "Etc/UTC")]
    [InlineData("* * * * *", "Invalid/Zone")]
    public void Cron_preview_rejects_invalid_or_six_field_configuration(string cron, string zone) =>
        Assert.Throws<SystemDataOperationException>(() => _engine.Preview(new(cron, zone)));
    [Fact]
    public async Task Database_rejects_cross_tenant_parent_and_history_cleanup_preserves_unknown()
    {
        await InitializeAsync(); var job = await CreateAsync();
        var accepted = await _administration.TriggerAsync("tenant-a", "admin", "trace", job.NId, new(job.OptimisticVersion, job.ConcurrencyVersion), "history-policy", CancellationToken.None);
        var run = (await _store.GetRunAsync("tenant-a", accepted.RunNId, CancellationToken.None))!;
        await Assert.ThrowsAnyAsync<Exception>(() => _store.AddRunAsync(run with { Id = Guid.NewGuid(), NId = "foreign", TenantNId = "tenant-b" }, CancellationToken.None));
        var old = DateTimeOffset.UtcNow.AddDays(-100);
        Assert.True(await _store.SaveRunAsync(run with { Status = "Unknown", CompletedOn = old, StateVersion = 1 }, 0, CancellationToken.None));
        Assert.Equal(0, await _store.CleanupHistoryAsync(DateTimeOffset.UtcNow.AddDays(-90), CancellationToken.None));
        run = (await _store.GetRunAsync("tenant-a", run.NId, CancellationToken.None))!;
        Assert.True(await _store.SaveRunAsync(run with { Status = "Cancelled", StateVersion = 2 }, 1, CancellationToken.None));
        Assert.Equal(1, await _store.CleanupHistoryAsync(DateTimeOffset.UtcNow.AddDays(-90), CancellationToken.None));
    }
    [Fact]
    public async Task Runtime_rejects_removed_native_index_without_repairing_it()
    {
        await InitializeAsync();
        await using var native = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + _file + ".scheduler.db");
        await native.OpenAsync();
        await using var command = native.CreateCommand(); command.CommandText = "DROP INDEX IX_JobParameter_Name"; await command.ExecuteNonQueryAsync();
        Assert.Throws<InvalidOperationException>(() => SchedulerStorageSchema.OpenExisting(DbType.Sqlite, "Data Source=" + _file));
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='IX_JobParameter_Name'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }
    [Fact]
    public async Task Database_paging_sorts_full_result_and_rejects_sql_like_sort_fields()
    {
        await InitializeAsync(); var job = await CreateAsync();
        await _store.AddJobAsync(new("tenant-a", "other", "AAA", null, "tests.actual", "{}", "*/15 * * * *", "Etc/UTC", 300, 1, "Idempotent"), CancellationToken.None);
        var first = await _store.QueryJobsAsync("tenant-a", null, null, 1, 1, CancellationToken.None, "nId", "asc");
        Assert.Equal(2, first.Total); Assert.Equal("other", Assert.Single(first.Items).NId);
        var second = await _store.QueryJobsAsync("tenant-a", null, null, 2, 1, CancellationToken.None, "nId", "asc");
        Assert.Equal(job.NId, Assert.Single(second.Items).NId);
        var invalid = await Assert.ThrowsAsync<SystemDataOperationException>(() => _store.QueryJobsAsync("tenant-a", null, null, 1, 1, CancellationToken.None, "name;DROP TABLE scheduler_job", "asc"));
        Assert.Equal("SCHEDULER_SORT_INVALID", invalid.Code);
    }
    private async Task<SchedulerRun> WaitAsync(string nId, Func<SchedulerRun, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        SchedulerRun? latest = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            latest = await _store.GetRunAsync("tenant-a", nId, CancellationToken.None);
            if (latest is not null && predicate(latest)) return latest;
            await Task.Delay(100);
        }
        throw new TimeoutException("实际执行未进入期望状态:" + latest?.Status + "/" + latest?.ResultCode);
    }
    public void Dispose()
    {
        _engine.Dispose(); _database.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(_file)!, Path.GetFileName(_file) + "*"))
            try { File.Delete(file); } catch (IOException) { }
    }
    private sealed class TestActivator(HangfireScheduledTask task) : JobActivator
    {
        public override object ActivateJob(Type jobType) => task;
    }
    private sealed class TestHandler : IScheduledTaskHandler
    {
        public int Calls;
        public bool FailFirst { get; set; }
        public bool WaitForCancellation { get; set; }
        public bool ThrowUnknown { get; set; }
        public SchedulerHandlerDto Definition { get; } = new("tests.actual", "Actual test handler", null, "Idempotent", true, 3600, true);
        public JsonElement ValidateParameters(JsonElement parameters) => parameters.Clone();
        public async Task<ScheduledTaskResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref Calls);
            if (ThrowUnknown) throw new InvalidOperationException("Side effect is unknown");
            if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return FailFirst && attempt == 1 ? new("Failed", "SAFE_TRANSIENT_FAILURE", Retryable: true) : new("Succeeded", "ACTUAL_HANDLER_DONE");
        }
    }
    private sealed class RecordingAudit : IAuditService
    {
        public List<AuditFactIngestRequest> Facts { get; } = [];
        public bool FailWrites { get; set; }
        public Task<AuditFactV1> IngestAsync(string tenantNId, AuditFactIngestRequest request, CancellationToken cancellationToken)
        {
            if (FailWrites) throw new InvalidOperationException("Audit unavailable");
            Facts.Add(request); return Task.FromResult(new AuditFactV1());
        }
        public Task<AuditFactPageV1> QueryAsync(string tenantNId, string actorUserNId, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(new AuditFactPageV1());
        public Task<AuditFactV1?> GetAsync(string tenantNId, string actorUserNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken) => Task.FromResult<AuditFactV1?>(null);
        public Task<string> ExportCsvAsync(string tenantNId, string actorUserNId, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateLifecycleAsync(string tenantNId, string actorUserNId, string producerServiceKey, string auditEventNId, AuditLifecycleRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RecoverOutboxAsync(string tenantNId, string actorUserNId, Guid eventId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
