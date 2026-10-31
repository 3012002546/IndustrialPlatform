using System.Diagnostics;
using System.Text.Json;
using Hangfire;
using Hangfire.Storage;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using IndustrialPlatform.SystemData.Application.Scheduler;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Contracts.Scheduler;
using IndustrialPlatform.SystemData.Domain.Scheduler;

namespace IndustrialPlatform.SystemData.Infrastructure.Scheduler;

/// <summary>固定白名单包装入口，原生重复领取仍须通过业务活动槽。</summary>
public sealed class HangfireScheduledTask(ISchedulerStore store, SchedulerAdministration administration, ISystemDataWriteTransaction transaction)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [AutomaticRetry(Attempts = 0)]
    [SchedulerRetryPolicy]
    public async Task<ScheduledTaskResult> ExecuteAsync(string tenantNId, string jobNId, string? runNId,
        PerformContext? context, CancellationToken cancellationToken)
    {
        if (context is null) throw new InvalidOperationException("缺少原生执行上下文。");
        var job = await store.GetJobAsync(tenantNId, jobNId, cancellationToken);
        if (job is null) return new("Failed", "JOB_MISSING");
        var run = runNId is null ? await store.GetInvocationAsync(tenantNId, "engine:" + context.BackgroundJob.Id, cancellationToken)
            : await store.GetRunAsync(tenantNId, runNId, cancellationToken);
        if (run is null && runNId is null)
        {
            if (job.JobKind == "OneShot" || job.IsDeleted || job.Status != "Enabled") return new("Cancelled", "JOB_DISABLED");
            var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid().ToString("N");
            run = new() { NId = id, TenantNId = tenantNId, CreatedOn = now, AcceptedOn = now, JobId = job.Id,
                Source = "Schedule", InvocationKey = "engine:" + context.BackgroundJob.Id, OperationKey = id,
                JobNameSnapshot = job.Name, HandlerKey = job.HandlerKey, ParametersJson = job.ParametersJson,
                TimeoutSeconds = job.TimeoutSeconds, MaxAttempts = job.MaxAttempts, RetrySafety = job.RetrySafety,
                EngineReference = context.BackgroundJob.Id, TraceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N") };
            try
            {
                await transaction.ExecuteAsync(async () =>
                {
                    if (!await store.LockJobAsync(job, cancellationToken)) throw new SchedulerBusyException();
                    await store.AddRunAsync(run, cancellationToken);
                }, cancellationToken);
            }
            catch (SchedulerDuplicateException)
            { run = await store.GetInvocationAsync(tenantNId, run.InvocationKey, cancellationToken) ?? throw new InvalidOperationException("运行防重记录无法读取。"); }
        }
        if (run is null || run.IsTerminal || run.Status == "Unknown" || (run.EngineReference is not null && run.EngineReference != context.BackgroundJob.Id))
            return new("Cancelled", "DELIVERY_NOT_ELIGIBLE");
        if (run.ActiveJobId is not null) throw new SchedulerBusyException();
        var token = Guid.NewGuid(); var started = DateTimeOffset.UtcNow;
        if (job.JobKind == "OneShot" && run.DeadlineOn <= started)
        {
            if (!await store.SaveRunAsync(run with { Status = "Failed", CompletedOn = started, ResultCode = "SCHEDULER_DEADLINE_EXPIRED", StateVersion = run.StateVersion + 1 }, run.StateVersion, cancellationToken)) throw new SchedulerBusyException();
            return new("Failed", "SCHEDULER_DEADLINE_EXPIRED");
        }
        if (!await store.TryStartAsync(run, token, context.ServerId, context.BackgroundJob.Id, started, cancellationToken)) throw new SchedulerBusyException();
        run = await store.GetRunAsync(tenantNId, run.NId, cancellationToken) ?? throw new InvalidOperationException("运行提交后丢失。");
        context.SetJobParameter("SchedulerMaxAttempts", run.MaxAttempts);
        var attempts = context.GetJobParameter<List<SchedulerAttemptDto>>("SchedulerAttempts") ?? [];
        attempts.Add(new(run.AttemptsUsed, "Running", started, null, null, null, run.TraceId));
        context.SetJobParameter("SchedulerAttempts", attempts);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(job.JobKind == "OneShot" ? run.DeadlineOn!.Value - started : TimeSpan.FromSeconds(run.TimeoutSeconds));
        using var watcher = new CancellationTokenSource();
        var watch = WatchCancellationAsync(run, timeout, watcher.Token);
        ScheduledTaskResult result;
        var invoked = false;
        var permitsRetry = false;
        try
        {
            var handler = administration.Handler(run.HandlerKey);
            permitsRetry = handler.Definition.RetrySafety == "Idempotent";
            using var parameters = JsonDocument.Parse(run.ParametersJson);
            var validated = handler.ValidateParameters(parameters.RootElement);
            invoked = true;
            result = await handler.ExecuteAsync(new(tenantNId, jobNId, run.NId, run.OperationKey,
                validated, run.DeadlineOn!.Value, run.TraceId), timeout.Token);
        }
        catch (OperationCanceledException)
        {
            var current = await store.GetRunAsync(tenantNId, run.NId, CancellationToken.None);
            result = new(current?.CancelRequestedOn is not null ? "Cancelled" : cancellationToken.IsCancellationRequested ? "Unknown" : "TimedOut", "EXECUTION_STOPPED");
        }
        catch (Exception exception)
        {
            result = invoked ? new("Unknown", "HANDLER_RESULT_UNKNOWN", "处理器未返回可信结果:" + exception.GetType().Name)
                : new("Failed", "HANDLER_UNAVAILABLE", "处理器或参数不符合当前部署合同。");
        }
        finally
        {
            await watcher.CancelAsync();
            try { await watch; } catch (OperationCanceledException) { }
            catch (Exception) { timeout.Cancel(); }
        }
        if (result.Outcome is not ("Succeeded" or "Failed" or "Cancelled" or "TimedOut" or "Unknown")) result = new("Unknown", "HANDLER_RESULT_INVALID");
        if (string.IsNullOrWhiteSpace(result.ResultCode) || result.ResultCode.Length > 64 || result.AffectedCount?.Length > 20) result = new("Unknown", "HANDLER_RESULT_INVALID");
        if (result.Message?.Length > 1000 || System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result, Json)) > 4096) result = new("Unknown", "HANDLER_RESULT_TOO_LARGE");
        attempts[^1] = attempts[^1] with { Status = result.Outcome, CompletedOn = DateTimeOffset.UtcNow, ResultCode = result.ResultCode, ErrorSummary = result.Outcome == "Succeeded" ? null : result.Message };
        context.SetJobParameter("SchedulerAttempts", attempts);
        var latest = await store.GetRunAsync(tenantNId, run.NId, CancellationToken.None);
        if (latest is null || latest.ExecutionToken != token || latest.Status == "Unknown" || latest.IsTerminal)
            return new("Unknown", "LATE_RESULT_REQUIRES_REVIEW");
        var retry = result.Outcome == "Failed" && result.Retryable && permitsRetry && run.RetrySafety == "Idempotent" && run.AttemptsUsed < run.MaxAttempts;
        var state = retry ? "RetryWaiting" : result.Outcome;
        var updated = latest with { Status = state, ActiveJobId = result.Outcome == "Unknown" ? latest.JobId : null,
            CompletedOn = retry ? null : DateTimeOffset.UtcNow, ResultCode = result.ResultCode,
            ResultSummaryJson = JsonSerializer.Serialize(result, Json), StateVersion = latest.StateVersion + 1 };
        if (!await store.SaveRunAsync(updated, latest.StateVersion, CancellationToken.None))
        {
            var concurrent = await store.GetRunAsync(tenantNId, run.NId, CancellationToken.None);
            if (concurrent is null || concurrent.ExecutionToken != token || concurrent.Status != "CancelRequested") return new("Unknown", "RESULT_COMMIT_CONFLICT");
            if (!await store.SaveRunAsync(updated with { CancelRequestedOn = concurrent.CancelRequestedOn, StateVersion = concurrent.StateVersion + 1 }, concurrent.StateVersion, CancellationToken.None))
                return new("Unknown", "RESULT_COMMIT_CONFLICT");
        }
        if (retry) throw new SchedulerRetryableException();
        return result;
    }

    private async Task WatchCancellationAsync(SchedulerRun run, CancellationTokenSource execution, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(500, token);
            var latest = await store.GetRunAsync(run.TenantNId, run.NId, token);
            if (latest?.CancelRequestedOn is not null) { await execution.CancelAsync(); return; }
        }
    }
}

public sealed class SchedulerBusyException : Exception;
public sealed class SchedulerRetryableException : Exception;

/// <summary>复用原生重试过滤器；争用等待不消耗业务尝试次数。</summary>
public sealed class SchedulerRetryPolicyAttribute : JobFilterAttribute, IElectStateFilter, IApplyStateFilter
{
    public SchedulerRetryPolicyAttribute() => Order = 100;
    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is not FailedState failed) return;
        if (failed.Exception is SchedulerBusyException) { context.CandidateState = new ScheduledState(TimeSpan.FromSeconds(5)); return; }
        if (failed.Exception is SchedulerRetryableException)
        {
            var maximum = context.GetJobParameter<int>("SchedulerMaxAttempts");
            new AutomaticRetryAttribute { Attempts = Math.Clamp(maximum - 1, 0, 4), LogEvents = false }.OnStateElection(context);
        }
    }
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction) => context.JobExpirationTimeout = TimeSpan.FromDays(90);
    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction) { }
}
