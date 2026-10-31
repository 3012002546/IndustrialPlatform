using System.Text.Json;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;
using IndustrialPlatform.RuntimeSupport.Jobs;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Application.Scheduler;
using IndustrialPlatform.SystemData.Contracts.Scheduler;
using IndustrialPlatform.SystemData.Infrastructure.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;

/// <summary>未准入渲染器时真实返回失败；后续渲染接入仍从同一个PF07活动槽进入。</summary>
public sealed class StudioReportTask(IServiceScopeFactory scopes) : IScheduledTaskHandler
{
    public SchedulerHandlerDto Definition => new("studio.report.render", "报告生成", "固定报告运行的一次性执行。", "NoAutomaticRecovery", true, 3600, false);
    public JsonElement ValidateParameters(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object || parameters.EnumerateObject().Count() != 1 || !parameters.TryGetProperty("runNId", out var run) || run.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(run.GetString()) || run.GetString()!.Length > 128) throw SchedulerAdministration.Error("SCHEDULER_PARAMETERS_INVALID", "仅允许runNId。", 422);
        return parameters.Clone();
    }
    public async Task<ScheduledTaskResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ReportRuns>();
        var run = await execution.BeginExecutionAsync(context.TenantNId, context.Parameters.GetProperty("runNId").GetString()!, context.RunNId, context.OperationKey, cancellationToken);
        if (run is null) return new("Failed", "STUDIO_REPORT_RUN_INVALID");
        return await scope.ServiceProvider.GetRequiredService<ReportExecution>().ExecuteAsync(run, cancellationToken);
    }
}

/// <summary>持久scheduler_run本身即提交恢复依据；共用工作器补投队列，不加载平台Outbox控制面。</summary>
internal sealed class EmbeddedSchedulerWakeup : IControlPlaneOutbox
{
    public Task EnqueueAsync(ControlPlaneEvent item, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyCollection<ControlPlaneEvent>> GetPendingAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ControlPlaneEvent>>([]);
    public Task MarkPublishedAsync(Guid eventId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<bool> RecordFailureAsync(Guid eventId, string failureMessage, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<OutboxEventStatus?> GetStatusAsync(Guid eventId, CancellationToken cancellationToken) => Task.FromResult<OutboxEventStatus?>(null);
}
public static class StudioJobsRegistration
{
    public static IServiceCollection AddStudioJobs(this IServiceCollection services, StudioDeploymentProfile profile)
    {
        if (!profile.ActiveModules.Contains("Report", StringComparer.Ordinal)) return services;
        services.AddSingleton<IScheduledTaskHandler, StudioReportTask>();
        services.AddScoped<IStudioJobs, StudioJobAdapter>();
        if (profile.DeploymentMode != "ExternalMes") return services;
        services.AddSingleton<ISchedulerStore>(sp => new SqlSchedulerStore(sp.GetRequiredService<StudioRuntimeDatabase>().Context));
        var options = Options.Create(new SchedulerOptions { Enabled = true, WorkerConcurrency = 2 });
        services.AddSingleton(options);
        services.AddSingleton<HangfireSchedulerEngine>(sp =>
        {
            var db = sp.GetRequiredService<StudioRuntimeDatabase>().Context;
            var studio = sp.GetRequiredService<IndustrialPlatform.PlatformStudio.Infrastructure.Persistence.StudioDatabase>();
            return new(options, db, Options.Create(new IndustrialPlatform.Infrastructure.Database.SqlSugarOptions { DbType = studio.Provider, ConnectionString = studio.ConnectionString }), sp.GetRequiredService<ISchedulerStore>());
        });
        services.AddSingleton<ISchedulerEngine>(sp => sp.GetRequiredService<HangfireSchedulerEngine>());
        services.AddSingleton<IControlPlaneOutbox, EmbeddedSchedulerWakeup>();
        services.AddSingleton<SchedulerAdministration>();
        services.AddSingleton<IScheduledTaskSubmission>(sp => sp.GetRequiredService<SchedulerAdministration>());
        services.AddSingleton<HangfireScheduledTask>();
        services.AddHostedService<SchedulerWorker>();
        return services;
    }
}
