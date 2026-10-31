using System.Text.Json;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.RuntimeSupport.Jobs;
using IndustrialPlatform.SystemData.Application.Operations;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Application.Scheduler;
using IndustrialPlatform.SystemData.Contracts.Scheduler;
using Hangfire;
namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

public sealed partial class Pf07SchedulerTests
{
    private sealed class ReportHandler : IScheduledTaskHandler
    {
        public int Calls { get; private set; }
        public SchedulerHandlerDto Definition => new("studio.report.render", "Report", null, "NoAutomaticRecovery", true, 3600, false);
        public JsonElement ValidateParameters(JsonElement input) => input.Clone();
        public Task<ScheduledTaskResult> ExecuteAsync(TaskExecutionContext context, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new ScheduledTaskResult("Failed", "STUDIO_RENDERER_UNAVAILABLE"));
        }
    }
    [Fact]
    public async Task Once_native_worker_executes_only_one_attempt_after_operation_replay()
    {
        await InitializeAsync();
        var handler = new ReportHandler();
        var service = new SchedulerAdministration(_store, _engine, [handler], _audit, new InMemoryControlPlaneOutbox(), _transaction, TimeProvider.System);
        using var json = JsonDocument.Parse("{\"runNId\":\"report-worker\"}");
        var trusted = new TrustedServiceContext("tenant-a", "platform-studio", "actor");
        var request = new EnqueueOnceV1("studio.report.render", "operation-worker", json.RootElement.Clone(), "actor", DateTimeOffset.UtcNow.AddMinutes(5).ToOffset(TimeSpan.FromHours(8)));
        var accepted = await service.EnqueueOnceAsync(trusted, request, default);
        await _engine.EnqueueAsync((await _store.GetRunAsync("tenant-a", accepted.RunNId, default))!, default);
        using var server = new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 2, Activator = new TestActivator(new(_store, service, _transaction)) }, _engine.Storage);
        var finished = await WaitAsync(accepted.RunNId, item => item.Status == "Failed", TimeSpan.FromSeconds(30));
        Assert.Equal("STUDIO_RENDERER_UNAVAILABLE", finished.ResultCode);
        Assert.Equal(1, handler.Calls); Assert.Equal(1, finished.AttemptsUsed); Assert.Null(finished.ActiveJobId);
        Assert.Equal(accepted.RunNId, (await service.EnqueueOnceAsync(trusted, request, default)).RunNId);
        Assert.Single(await _store.ListRunsAsync("tenant-a", default));
    }
    [Fact]
    public async Task Once_submission_replays_protects_ownership_and_never_registers_cron()
    {
        await InitializeAsync();
        var service = new SchedulerAdministration(_store, _engine, [new ReportHandler()], _audit, new InMemoryControlPlaneOutbox(), _transaction, TimeProvider.System);
        var trusted = new TrustedServiceContext("tenant-a", "platform-studio", "actor");
        using var json = JsonDocument.Parse("{\"runNId\":\"report-one\"}");
        var request = new EnqueueOnceV1("studio.report.render", "operation-one", json.RootElement.Clone(), "actor", DateTimeOffset.UtcNow.AddMinutes(5));
        var first = await service.EnqueueOnceAsync(trusted, request, default);
        Assert.Equal(first, await service.EnqueueOnceAsync(trusted, request, default));
        Assert.Equal(first, await service.GetByOperationAsync(trusted, request.OperationKey, default));
        Assert.Null(await service.GetByOperationAsync(trusted with { TenantNId = "tenant-b" }, request.OperationKey, default));
        Assert.Single(await _store.ListRunsAsync("tenant-a", default));
        Assert.Equal(request.DeadlineOn, (await _store.GetRunAsync("tenant-a", first.RunNId, default))!.DeadlineOn);
        var job = (await _store.GetJobAsync("tenant-a", first.JobNId, default))!;
        Assert.Equal("OneShot", job.JobKind); Assert.Equal("Disabled", job.Status); Assert.Equal(1, job.MaxAttempts);
        Assert.Equal("NoAutomaticRecovery", job.RetrySafety);
        await _engine.SynchronizeAsync(job, default);
        Assert.Null((await _engine.GetPlanAsync(job, default)).NextFireOn);
        Assert.Throws<IndustrialPlatform.SharedKernel.Exceptions.BusinessException>(() => job.SetEnabled(true));
        await Assert.ThrowsAsync<SystemDataOperationException>(() => service.EnqueueOnceAsync(trusted, request with { RequestedBy = "other" }, default));
        await Assert.ThrowsAsync<SystemDataOperationException>(() => service.EnqueueOnceAsync(trusted, request with { HandlerKey = "systemdata.reconcile" }, default));
        await Assert.ThrowsAsync<SystemDataOperationException>(() => service.EnqueueOnceAsync(trusted, request with { DeadlineOn = request.DeadlineOn.AddSeconds(1) }, default));
        await Assert.ThrowsAsync<SystemDataOperationException>(() => service.GetByOperationAsync(trusted with { ActorUserNId = "other" }, request.OperationKey, default));
    }
}
