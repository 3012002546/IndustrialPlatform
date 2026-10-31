using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Operations;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Contracts.Auditing;
using IndustrialPlatform.SystemData.Contracts.Scheduler;
using IndustrialPlatform.SystemData.Domain.Scheduler;

namespace IndustrialPlatform.SystemData.Application.Scheduler;

/// <summary>任务管理、人工防重和受控运行操作；实际执行由原生引擎承担。</summary>
public sealed partial class SchedulerAdministration(ISchedulerStore store, ISchedulerEngine engine,
    IEnumerable<IScheduledTaskHandler> handlers, IAuditService audit, IControlPlaneOutbox outbox,
    ISystemDataWriteTransaction transaction, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, IScheduledTaskHandler> _handlers = handlers.ToDictionary(handler => handler.Definition.HandlerKey, StringComparer.Ordinal);

    public IReadOnlyList<SchedulerHandlerDto> Handlers => _handlers.Values.Where(handler => handler.Definition.HandlerKey != "studio.report.render").Select(handler => handler.Definition).ToArray();
    public IScheduledTaskHandler Handler(string key) => !string.IsNullOrWhiteSpace(key) && _handlers.TryGetValue(key, out var handler) ? handler
        : throw Error("SCHEDULER_HANDLER_UNKNOWN", "处理器未注册。", 422);
    public SchedulerPreview Preview(SchedulerScheduleInput input) => engine.Preview(input);

    public async Task<SchedulerPage<SchedulerJobDto>> ListAsync(string tenant, string? keyword, string? status,
        int page, int pageSize, CancellationToken token, string? sortBy = null, string? sortOrder = null)
    {
        Page(page, pageSize);
        var all = await store.QueryJobsAsync(tenant, keyword, status, page, pageSize, token, sortBy, sortOrder);
        var items = new List<SchedulerJobDto>();
        foreach (var job in all.Items) items.Add(await DtoAsync(job, token));
        return new(items, Number(all.Total), page, pageSize);
    }

    public async Task<SchedulerJobDto> GetAsync(string tenant, string nId, CancellationToken token) =>
        await DtoAsync(await JobAsync(tenant, nId, token, true), token);

    public async Task<SchedulerJobDto> CreateAsync(string tenant, string actor, string trace,
        SchedulerJobCreate request, CancellationToken token)
    {
        Enabled();
        Key(request.NId, "任务标识", 128);
        if (request.HandlerKey == "studio.report.render") throw Error("SCHEDULER_ONESHOT_REQUIRED", "报表处理器仅允许受信一次性受理。", 403);
        var parameters = Validate(request, Handler(request.HandlerKey));
        var existing = await store.GetJobAsync(tenant, request.NId, token);
        if (existing is not null)
        {
            if (existing.IsDeleted || existing.HandlerKey != request.HandlerKey || !Same(existing, request, parameters)) throw Conflict();
            return await DtoAsync(existing, token);
        }
        var job = new SchedulerJob(tenant, request.NId, request.Name.Trim(), request.Description?.Trim(), request.HandlerKey,
            parameters, request.CronExpression, request.TimeZoneId, request.TimeoutSeconds, request.MaxAttempts, Handler(request.HandlerKey).Definition.RetrySafety);
        try { await transaction.ExecuteAsync(async () =>
        {
            await store.AddJobAsync(job, token);
            await AuditAsync(tenant, actor, trace, "scheduler.job.create", "SchedulerJob", job.NId, new { job.Name, job.HandlerKey }, token);
            await SyncAsync(job, token);
        }, token); }
        catch (SchedulerDuplicateException)
        {
            existing = await store.GetJobAsync(tenant, request.NId, token);
            if (existing is null || existing.IsDeleted || existing.HandlerKey != request.HandlerKey || !Same(existing, request, parameters)) throw Conflict();
            return await DtoAsync(existing, token);
        }
        return await DtoAsync(job, token);
    }

    public async Task<SchedulerJobDto> UpdateAsync(string tenant, string actor, string trace, string nId,
        SchedulerJobUpdate request, CancellationToken token)
    {
        Enabled(); var job = await JobAsync(tenant, nId, token);
        if (job.JobKind == "OneShot") throw Conflict("一次性任务不可编辑。");
        Version(job, request.OptimisticVersion, request.ConcurrencyVersion);
        var parameters = Validate(request, Handler(job.HandlerKey));
        if (Same(job, request, parameters)) return await DtoAsync(job, token);
        var oldVersion = job.OptimisticVersion; var oldToken = job.ConcurrencyVersion;
        try { job.Update(request.Name.Trim(), request.Description?.Trim(), parameters, request.CronExpression, request.TimeZoneId, request.TimeoutSeconds, request.MaxAttempts); }
        catch (BusinessException) { throw Conflict("请先停用任务，并确认实体未冻结或锁定。"); }
        await transaction.ExecuteAsync(async () =>
        {
            if (!await store.SaveJobAsync(job, oldVersion, oldToken, token)) throw Conflict();
            await AuditAsync(tenant, actor, trace, "scheduler.job.update", "SchedulerJob", nId, new { job.Name, job.CronExpression, job.TimeZoneId }, token);
            await SyncAsync(job, token);
        }, token);
        return await DtoAsync(job, token);
    }

    public async Task<SchedulerJobDto> SetStateAsync(string tenant, string actor, string trace, string nId,
        string action, SchedulerVersionCommand command, CancellationToken token)
    {
        Enabled(); var job = await JobAsync(tenant, nId, token, includeDeleted: action == "restore");
        if (job.JobKind == "OneShot") throw Conflict("一次性任务不允许管理操作。");
        if ((action == "enable" && job.Status == "Enabled") || (action == "disable" && job.Status == "Disabled")) return await DtoAsync(job, token);
        Version(job, command.OptimisticVersion, command.ConcurrencyVersion);
        var oldVersion = job.OptimisticVersion; var oldToken = job.ConcurrencyVersion;
        try
        {
            switch (action)
            {
                case "enable": job.SetEnabled(true); break;
                case "disable": job.SetEnabled(false); break;
                case "delete":
                    if (job.IsSystemOwned || job.Status != "Disabled") throw Conflict("请先停用并处理未结束运行；内建任务不能删除。");
                    job.MarkDeleted(); break;
                case "restore": job.Restore(); break;
                default: throw Error("SCHEDULER_ACTION_INVALID", "不支持此操作。");
            }
        }
        catch (BusinessException) { throw Conflict("任务状态或实体保护条件不允许此操作。"); }
        await transaction.ExecuteAsync(async () =>
        {
            if (action == "delete" && await store.HasUnfinishedAsync(tenant, job.Id, token)) throw Conflict("仍有未结束运行，不能删除任务。");
            if (!await store.SaveJobAsync(job, oldVersion, oldToken, token)) throw Conflict();
            await AuditAsync(tenant, actor, trace, "scheduler.job." + action, "SchedulerJob", nId, new { job.Status, job.IsDeleted }, token);
            await SyncAsync(job, token);
        }, token);
        return await DtoAsync(job, token);
    }

    public async Task<SchedulerRunAccepted> TriggerAsync(string tenant, string actor, string trace, string jobNId,
        SchedulerVersionCommand command, string requestKey, CancellationToken token)
    {
        Enabled(); Key(requestKey, "请求标识", 128);
        var key = Invocation(actor, "manual", requestKey);
        var hash = Hash(JsonSerializer.Serialize(new { jobNId, command }, Json));
        if (await ReplayAsync(tenant, key, hash, token) is { } accepted) return accepted;
        var job = await JobAsync(tenant, jobNId, token);
        Version(job, command.OptimisticVersion, command.ConcurrencyVersion);
        if (job.Status != "Enabled") throw Conflict("请先启用任务。");
        return await AcceptAsync(job, actor, trace, key, hash, "Manual", null, null, token);
    }

    public async Task<SchedulerRunAccepted> RetryAsync(string tenant, string actor, string trace, string nId,
        SchedulerRunVersionCommand command, string requestKey, CancellationToken token)
    {
        Enabled(); Key(requestKey, "请求标识", 128);
        var key = Invocation(actor, "retry", requestKey);
        var hash = Hash(JsonSerializer.Serialize(new { nId, command }, Json));
        if (await ReplayAsync(tenant, key, hash, token) is { } accepted) return accepted;
        var original = await RunAsync(tenant, nId, token);
        RunVersion(original, command.StateVersion);
        if (original.Status is not ("Failed" or "TimedOut") || original.RetrySafety != "Idempotent" || Handler(original.HandlerKey).Definition.RetrySafety != "Idempotent" || original.ActiveJobId is not null) throw Conflict("仅已停止且可安全重放的失败运行允许重试。");
        var job = await store.GetJobByIdAsync(tenant, original.JobId, token) ?? throw NotFound();
        if (job.IsDeleted || job.Status != "Enabled") throw Conflict("原任务不可运行。");
        return await AcceptAsync(job, actor, trace, key, hash, "Retry", original, original.OperationKey, token);
    }

    public async Task<SchedulerPage<SchedulerRunDto>> RunsAsync(string tenant, string? jobNId, string? status,
        string? source, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken token, string? sortBy = null, string? sortOrder = null)
    {
        Page(page, pageSize);
        to ??= clock.GetUtcNow().AddSeconds(1);
        from ??= to.Value.AddDays(-7);
        if (from >= to || to - from > TimeSpan.FromDays(31)) throw Error("SCHEDULER_TIME_RANGE_INVALID", "时间范围不能超过31天。");
        Guid? jobId = jobNId is null ? null : (await JobAsync(tenant, jobNId, token, true)).Id;
        var all = await store.QueryRunsAsync(tenant, jobId, status, source, from.Value, to.Value, page, pageSize, token, sortBy, sortOrder);
        var items = new List<SchedulerRunDto>();
        foreach (var run in all.Items) items.Add(await RunDtoAsync(run, token));
        return new(items, Number(all.Total), page, pageSize);
    }

    public async Task<SchedulerRunDetail> DetailAsync(string tenant, string actor, string nId, CancellationToken token)
    {
        var run = await RunAsync(tenant, nId, token);
        SchedulerResolutionDto? resolution = null;
        var fact = run.ResultCode is "RESULT_VERIFIED" or "RESULT_VERIFIED_NO_EFFECT"
            ? await audit.GetAsync(tenant, actor, "systemdata", "scheduler-resolution:" + Hash(nId), token) : null;
        if (fact is not null)
        {
            using var payload = JsonDocument.Parse(fact.PayloadJson);
            var value = payload.RootElement;
            resolution = new(value.GetProperty("decision").GetString()!, value.GetProperty("reason").GetString()!,
                value.GetProperty("evidenceReference").GetString()!, fact.ActorUserNId ?? string.Empty, fact.OccurredOn, fact.TraceId);
        }
        return new(await RunDtoAsync(run, token), run.HandlerKey, Parse(run.ParametersJson), run.OperationKey,
            await engine.GetAttemptsAsync(run.EngineReference, token), resolution);
    }

    public async Task<SchedulerRunDto> CancelAsync(string tenant, string actor, string trace, string nId,
        SchedulerRunVersionCommand command, CancellationToken token)
    {
        var run = await RunAsync(tenant, nId, token);
        if (run.IsTerminal || run.Status == "CancelRequested") return await RunDtoAsync(run, token);
        RunVersion(run, command.StateVersion);
        if (run.Status == "Unknown" || (run.Status == "Running" && !Handler(run.HandlerKey).Definition.SupportsCancellation)) throw Conflict("当前执行不支持合作取消。");
        var now = clock.GetUtcNow();
        var updated = run with { Status = run.ActiveJobId is null ? "Cancelled" : "CancelRequested",
            CancelRequestedOn = now, CompletedOn = run.ActiveJobId is null ? now : null, StateVersion = run.StateVersion + 1 };
        await transaction.ExecuteAsync(async () =>
        {
            if (!await store.SaveRunAsync(updated, run.StateVersion, token)) throw Conflict();
            await AuditAsync(tenant, actor, trace, "scheduler.run.cancel", "SchedulerRun", nId, new { runNId = nId }, token);
        }, token);
        return await RunDtoAsync(updated, token);
    }

    public async Task<SchedulerRunDto> ResolveAsync(string tenant, string actor, string trace, string nId,
        SchedulerRunResolution command, CancellationToken token)
    {
        var run = await RunAsync(tenant, nId, token);
        RunVersion(run, command.StateVersion);
        if (run.Status != "Unknown") throw Conflict("只允许核对结果未知的运行。");
        if (command.Decision is not ("ConfirmedSucceeded" or "ConfirmedFailed" or "ConfirmedNoEffect")) throw Error("SCHEDULER_RESOLUTION_INVALID", "核对结论无效。");
        if (string.IsNullOrWhiteSpace(command.Reason) || string.IsNullOrWhiteSpace(command.EvidenceReference) || command.Reason.Trim().Length is < 10 or > 1000 || command.EvidenceReference.Trim().Length is < 1 or > 256) throw Error("SCHEDULER_EVIDENCE_REQUIRED", "请填写核对原因及旧执行已停止的证据引用。");
        var history = await engine.GetAttemptsAsync(run.EngineReference, token);
        var stopped = history.Availability == "Available" && history.Items.Count > 0 && history.Items[^1].CompletedOn is not null;
        if (!stopped && await engine.IsWorkerAliveAsync(run.WorkerReference, token) != false) throw Conflict("尚无法确认旧执行已停止，不能释放占用。");
        var resolutionSummary = new { resolution = new { decision = command.Decision, evidenceReference = command.EvidenceReference }, previousSummary = run.ResultSummaryJson };
        var updated = run with { Status = command.Decision == "ConfirmedSucceeded" ? "Succeeded" : "Failed", ResultSummaryJson = JsonSerializer.Serialize(resolutionSummary, Json),
            ActiveJobId = null, CompletedOn = clock.GetUtcNow(), StateVersion = run.StateVersion + 1,
            ResultCode = command.Decision == "ConfirmedNoEffect" ? "RESULT_VERIFIED_NO_EFFECT" : "RESULT_VERIFIED" };
        await transaction.ExecuteAsync(async () =>
        {
            if (!await store.SaveRunAsync(updated, run.StateVersion, token)) throw Conflict();
            await AuditAsync(tenant, actor, trace, "scheduler.run.resolve", "SchedulerRun", nId,
                new { command.Decision, command.Reason, command.EvidenceReference }, token, "scheduler-resolution:" + Hash(nId));
        }, token);
        return await RunDtoAsync(updated, token);
    }

    public async Task AuditAsync(string tenant, string actor, string trace, string action, string objectType,
        string objectNId, object payload, CancellationToken token, string? eventNId = null) =>
        await audit.IngestAsync(tenant, new AuditFactIngestRequest { ProducerServiceKey = "systemdata",
            AuditEventNId = eventNId ?? Guid.NewGuid().ToString("N"), OccurredOn = clock.GetUtcNow(), ActorUserNId = actor,
            Action = action, ObjectType = objectType, ObjectNId = objectNId, PayloadJson = JsonSerializer.Serialize(payload, Json),
            TraceId = trace, Severity = "Info" }, token);

    private async Task<SchedulerRunAccepted> AcceptAsync(SchedulerJob job, string actor, string trace, string key,
        string hash, string source, SchedulerRun? original, string? operationKey, CancellationToken token, DateTimeOffset? deadlineOn = null)
    {
        var id = Guid.NewGuid().ToString("N"); var now = clock.GetUtcNow();
        var run = new SchedulerRun { NId = id, TenantNId = job.TenantNId, JobId = job.Id, CreatedOn = now,
            AcceptedOn = now, InvocationKey = key, RequestHash = hash, OperationKey = operationKey ?? id,
            Source = source, OriginalRunNId = original?.NId, RequestedByUserNId = actor, TraceId = trace,
            JobNameSnapshot = original?.JobNameSnapshot ?? job.Name, HandlerKey = original?.HandlerKey ?? job.HandlerKey,
            ParametersJson = original?.ParametersJson ?? job.ParametersJson, TimeoutSeconds = original?.TimeoutSeconds ?? job.TimeoutSeconds,
            MaxAttempts = original?.MaxAttempts ?? job.MaxAttempts, RetrySafety = original?.RetrySafety ?? job.RetrySafety, DeadlineOn = deadlineOn };
        try { await transaction.ExecuteAsync(async () =>
        {
            if (!await store.LockJobAsync(job, token)) throw Conflict();
            await store.AddRunAsync(run, token);
            await AuditAsync(job.TenantNId, actor, trace, "scheduler.run." + source.ToLowerInvariant(), "SchedulerRun", id,
                new { jobNId = job.NId, runNId = id, run.OperationKey }, token);
            await outbox.EnqueueAsync(new(Guid.NewGuid(), "Scheduler.RunRequested.v1", "v1", job.TenantNId,
                JsonSerializer.Serialize(new { runNId = id }, Json), now), token);
        }, token); }
        catch (SchedulerDuplicateException) { return await ReplayAsync(job.TenantNId, key, hash, token) ?? throw Conflict(); }
        return new(run.NId, run.Status, run.AcceptedOn);
    }

    private async Task<SchedulerRunAccepted?> ReplayAsync(string tenant, string key, string hash, CancellationToken token)
    {
        var existing = await store.GetInvocationAsync(tenant, key, token);
        if (existing is null) return null;
        if (existing.RequestHash != hash) throw Conflict("同一请求标识的命令不同。");
        return new(existing.NId, existing.Status, existing.AcceptedOn);
    }
    private Task SyncAsync(SchedulerJob job, CancellationToken token) => outbox.EnqueueAsync(new(Guid.NewGuid(),
        "Scheduler.JobChanged.v1", "v1", job.TenantNId, JsonSerializer.Serialize(new { jobNId = job.NId }, Json), clock.GetUtcNow()), token);
    private async Task<SchedulerJob> JobAsync(string tenant, string nId, CancellationToken token, bool includeDeleted = false)
    {
        var job = await store.GetJobAsync(tenant, nId, token);
        return job is null || (job.IsDeleted && !includeDeleted) ? throw NotFound() : job;
    }
    private async Task<SchedulerRun> RunAsync(string tenant, string nId, CancellationToken token) => await store.GetRunAsync(tenant, nId, token) ?? throw NotFound();
    private string Validate(SchedulerJobValues values, IScheduledTaskHandler handler)
    {
        Key(values.Name ?? string.Empty, "名称", 128);
        if (values.Description?.Length > 1000 || values.TimeoutSeconds < 5 || values.TimeoutSeconds > handler.Definition.MaxTimeoutSeconds
            || values.MaxAttempts is < 1 or > 5 || (handler.Definition.RetrySafety != "Idempotent" && values.MaxAttempts != 1)) throw Error("SCHEDULER_CONFIGURATION_INVALID", "参数范围或尝试上限无效。", 422);
        engine.Preview(new(values.CronExpression, values.TimeZoneId));
        var parameters = handler.ValidateParameters(values.Parameters).GetRawText();
        if (Encoding.UTF8.GetByteCount(parameters) > 16384) throw Error("SCHEDULER_PARAMETERS_TOO_LARGE", "参数超过16KiB。", 422);
        return parameters;
    }
    private static bool Same(SchedulerJob job, SchedulerJobValues values, string parameters) => job.Name == values.Name.Trim()
        && job.Description == values.Description?.Trim() && job.ParametersJson == parameters && job.CronExpression == values.CronExpression
        && job.TimeZoneId == values.TimeZoneId && job.TimeoutSeconds == values.TimeoutSeconds && job.MaxAttempts == values.MaxAttempts;
    private async Task<SchedulerJobDto> DtoAsync(SchedulerJob job, CancellationToken token)
    {
        SchedulerEnginePlan plan;
        try { plan = await engine.GetPlanAsync(job, token); }
        catch (Exception exception) when (exception is not OperationCanceledException) { plan = new("Unavailable", null, "SCHEDULER_UNAVAILABLE"); }
        return new() { JobKind = job.JobKind, OwnerService = job.OwnerService, OwnerObjectNId = job.OwnerObjectNId, NId = job.NId, Name = job.Name, Description = job.Description, HandlerKey = job.HandlerKey,
            Parameters = Parse(job.ParametersJson), Status = job.Status, CronExpression = job.CronExpression, TimeZoneId = job.TimeZoneId,
            TimeoutSeconds = job.TimeoutSeconds, MaxAttempts = job.MaxAttempts, RetrySafety = job.RetrySafety, IsSystemOwned = job.IsSystemOwned,
            CreatedOn = job.CreatedOn, LastUpdatedOn = job.LastUpdatedOn, OptimisticVersion = Number(job.OptimisticVersion),
            ConcurrencyVersion = job.ConcurrencyVersion, SyncState = plan.SyncState, SyncErrorCode = plan.ErrorCode, NextFireOn = plan.NextFireOn };
    }
    public async Task<SchedulerRunDto> RunDtoAsync(SchedulerRun run, CancellationToken token)
    {
        var job = await store.GetJobByIdAsync(run.TenantNId, run.JobId, token);
        return new() { NId = run.NId, JobNId = job?.NId ?? string.Empty, JobName = run.JobNameSnapshot, Source = run.Source,
            OriginalRunNId = run.OriginalRunNId, Status = run.Status, AcceptedOn = run.AcceptedOn, StartedOn = run.StartedOn,
            DeadlineOn = run.DeadlineOn, CompletedOn = run.CompletedOn, CancelRequestedOn = run.CancelRequestedOn,
            AttemptsUsed = run.AttemptsUsed, MaxAttempts = run.MaxAttempts, RetrySafety = run.RetrySafety, ResultCode = run.ResultCode,
            ResultSummary = run.ResultSummaryJson is null ? null : JsonSerializer.Deserialize<ScheduledTaskResult>(run.ResultSummaryJson, Json),
            TraceId = run.TraceId, StateVersion = Number(run.StateVersion),
            Actions = new(run.Status is "Queued" or "RetryWaiting" || (run.Status == "Running" && _handlers.TryGetValue(run.HandlerKey, out var handler) && handler.Definition.SupportsCancellation),
                run.Status is "Failed" or "TimedOut" && run.RetrySafety == "Idempotent" && run.ActiveJobId is null, run.Status == "Unknown") };
    }
    private void Enabled() { if (!engine.Enabled) throw Error("SCHEDULER_DISABLED", "调度模块未启用。", 503); }
    private static void Version(SchedulerJob job, string version, Guid concurrency) { if (Number(job.OptimisticVersion) != version || job.ConcurrencyVersion != concurrency) throw Conflict(); }
    private static void RunVersion(SchedulerRun run, string version) { if (Number(run.StateVersion) != version) throw Conflict(); }
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static JsonElement Parse(string value) { using var json = JsonDocument.Parse(value); return json.RootElement.Clone(); }
    private static string Invocation(string actor, string action, string key) => "request:" + Hash(JsonSerializer.Serialize(new { actor, action, key }, Json));
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static void Key(string text, string label, int maximum) { if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text != text.Trim()) throw Error("SCHEDULER_INPUT_INVALID", label + "无效。"); }
    private static void Page(int page, int pageSize) { if (page is < 1 or > 1000 || pageSize is < 1 or > 100) throw Error("SCHEDULER_PAGE_INVALID", "分页范围无效。"); }
    public static SystemDataOperationException Error(string code, string message, int status = 400) => new(code, message, status);
    private static SystemDataOperationException Conflict(string message = "状态或并发版本已改变，请重新读取。") => Error("SCHEDULER_CONFLICT", message, 409);
    private static SystemDataOperationException NotFound() => Error("SCHEDULER_NOT_FOUND", "任务或运行不存在。", 404);
}
