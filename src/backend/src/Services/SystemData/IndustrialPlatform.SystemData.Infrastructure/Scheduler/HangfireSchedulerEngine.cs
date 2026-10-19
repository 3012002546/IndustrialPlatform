using System.Globalization;
using System.Text.Json;
using Cronos;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Scheduler;
using IndustrialPlatform.SystemData.Contracts.Scheduler;
using IndustrialPlatform.SystemData.Domain.Scheduler;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Infrastructure.Scheduler;

/// <summary>唯一原生调度引擎；存储懒加载，未初始化时不会由普通查询建库。</summary>
public sealed class HangfireSchedulerEngine : ISchedulerEngine, IDisposable
{
    private JobStorage? _storage;
    private readonly object _storageGate = new();
    private readonly SqlSugar.DbType _provider;
    private readonly string _connectionString;
    private readonly ISchedulerStore _store;
    private readonly SchedulerOptions _options;
    public bool Enabled => _options.Enabled;
    public JobStorage Storage
    {
        get { lock (_storageGate) return _storage ??= SchedulerStorageSchema.OpenExisting(_provider, _connectionString); }
    }
    public HangfireSchedulerEngine(IOptions<SchedulerOptions> options, SqlSugarDbContext database,
        IOptions<SqlSugarOptions> databaseOptions, ISchedulerStore store)
    {
        _options = options.Value; _store = store;
        _provider = database.DbType; _connectionString = databaseOptions.Value.ConnectionString!;
    }

    public SchedulerPreview Preview(SchedulerScheduleInput input)
    {
        if (string.IsNullOrWhiteSpace(input.CronExpression) || input.CronExpression.Length > 120 || input.CronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 5
            || input.CronExpression.Any(character => !char.IsDigit(character) && character is not (' ' or '*' or ',' or '-' or '/')))
            throw SchedulerAdministration.Error("SCHEDULER_CRON_INVALID", "请输入有效的五字段Cron。", 422);
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
            var cron = CronExpression.Parse(input.CronExpression);
            var now = DateTimeOffset.UtcNow; var cursor = now; var result = new List<SchedulerOccurrence>();
            for (var index = 0; index < 10; index++)
            {
                var next = cron.GetNextOccurrence(cursor, zone);
                if (next is null) break;
                cursor = next.Value;
                result.Add(new(cursor, TimeZoneInfo.ConvertTime(cursor, zone).ToString("O", CultureInfo.InvariantCulture)));
            }
            if (result.Count == 0) throw SchedulerAdministration.Error("SCHEDULER_CRON_NO_FUTURE", "计划没有未来运行时点。", 422);
            return new(input.TimeZoneId, now, result);
        }
        catch (Exception exception) when (exception is CronFormatException or TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw SchedulerAdministration.Error("SCHEDULER_SCHEDULE_INVALID", "Cron或时区无效。", 422); }
    }

    public static string PlanKey(string tenant, string job) => "scheduler:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { tenant, job })))).ToLowerInvariant();

    public async Task SynchronizeAsync(SchedulerJob job, CancellationToken token)
    {
        if (!Enabled) throw SchedulerAdministration.Error("SCHEDULER_DISABLED", "调度未启用。", 503);
        var key = PlanKey(job.TenantNId, job.NId);
        using var connection = Storage.GetConnection();
        using var lease = connection.AcquireDistributedLock("scheduler-sync:" + key, TimeSpan.FromSeconds(10));
        var latest = await _store.GetJobAsync(job.TenantNId, job.NId, token) ?? job;
        var manager = new RecurringJobManager(Storage);
        if (latest.IsDeleted || latest.Status == "Disabled") manager.RemoveIfExists(key);
        else manager.AddOrUpdate(key, Job.FromExpression<HangfireScheduledTask>(task => task.ExecuteAsync(latest.TenantNId, latest.NId, null, null, CancellationToken.None)),
            latest.CronExpression, new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(latest.TimeZoneId), MisfireHandling = MisfireHandlingMode.Ignorable });
        using var write = connection.CreateWriteTransaction();
        write.SetRangeInHash("scheduler-applied:" + key, new Dictionary<string,string>
        { ["version"] = latest.OptimisticVersion.ToString(CultureInfo.InvariantCulture), ["token"] = latest.ConcurrencyVersion.ToString("D") });
        write.Commit();
    }

    public async Task<string> EnqueueAsync(SchedulerRun run, CancellationToken token)
    {
        if (!Enabled) throw SchedulerAdministration.Error("SCHEDULER_DISABLED", "调度未启用。", 503);
        using var connection = Storage.GetConnection();
        using var lease = connection.AcquireDistributedLock("scheduler-enqueue:" + PlanKey(run.TenantNId, run.NId), TimeSpan.FromSeconds(10));
        var current = await _store.GetRunAsync(run.TenantNId, run.NId, token) ?? throw SchedulerAdministration.Error("SCHEDULER_NOT_FOUND", "运行不存在。", 404);
        if (current.EngineReference is not null) return current.EngineReference;
        if (current.Status != "Queued") return string.Empty;
        var job = await _store.GetJobByIdAsync(run.TenantNId, run.JobId, token) ?? throw SchedulerAdministration.Error("SCHEDULER_NOT_FOUND", "任务不存在。", 404);
        var reference = new BackgroundJobClient(Storage).Create(Job.FromExpression<HangfireScheduledTask>(task => task.ExecuteAsync(run.TenantNId, job.NId, run.NId, null, CancellationToken.None)), new EnqueuedState("default"));
        await _store.SaveRunAsync(current with { EngineReference = reference, StateVersion = current.StateVersion + 1 }, current.StateVersion, token);
        return reference;
    }

    public Task<SchedulerEnginePlan> GetPlanAsync(SchedulerJob job, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Enabled) return Task.FromResult(new SchedulerEnginePlan("Unavailable", null, "SCHEDULER_DISABLED"));
        using var connection = Storage.GetConnection();
        var key = PlanKey(job.TenantNId, job.NId);
        var applied = connection.GetAllEntriesFromHash("scheduler-applied:" + key);
        var synchronized = applied is not null && applied.GetValueOrDefault("version") == job.OptimisticVersion.ToString(CultureInfo.InvariantCulture)
            && applied.GetValueOrDefault("token") == job.ConcurrencyVersion.ToString("D");
        var plan = connection.GetAllEntriesFromHash("recurring-job:" + key);
        DateTimeOffset? next = plan is not null && plan.TryGetValue("NextExecution", out var text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToUniversalTime() : null;
        return Task.FromResult(new SchedulerEnginePlan(synchronized ? "Applied" : "Pending", next));
    }

    public Task<SchedulerAttemptHistory> GetAttemptsAsync(string? engineReference, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Enabled || string.IsNullOrEmpty(engineReference)) return Task.FromResult(new SchedulerAttemptHistory("Unavailable", [], "ENGINE_REFERENCE_UNAVAILABLE"));
        try
        {
            using var connection = Storage.GetConnection();
            if (connection.GetJobData(engineReference) is null) return Task.FromResult(new SchedulerAttemptHistory("Unavailable", [], "ENGINE_HISTORY_MISSING"));
            var text = connection.GetJobParameter(engineReference, "SchedulerAttempts");
            var items = text is null ? [] : Newtonsoft.Json.JsonConvert.DeserializeObject<List<SchedulerAttemptDto>>(text) ?? [];
            return Task.FromResult(new SchedulerAttemptHistory("Available", items));
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return Task.FromResult(new SchedulerAttemptHistory("Unavailable", [], "ENGINE_HISTORY_UNAVAILABLE")); }
    }

    public Task<bool?> IsWorkerAliveAsync(string? workerReference, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Enabled || string.IsNullOrEmpty(workerReference)) return Task.FromResult<bool?>(null);
        try
        {
            var now = DateTime.UtcNow;
            return Task.FromResult<bool?>(Storage.GetMonitoringApi().Servers().Any(server => server.Name == workerReference && server.Heartbeat > now.AddSeconds(-60)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return Task.FromResult<bool?>(null); }
    }
    public void Dispose() { lock (_storageGate) { if (_storage is IDisposable disposable) disposable.Dispose(); _storage = null; } }
}
