using IndustrialPlatform.SystemData.Contracts.Scheduler;
using IndustrialPlatform.SystemData.Domain.Scheduler;

namespace IndustrialPlatform.SystemData.Application.Scheduler;

/// <summary>任务/运行持久化；全部方法必须按租户约束，运行状态写使用CAS。</summary>
public interface ISchedulerStore
{
    Task<SchedulerJob?> GetJobAsync(string tenant, string nId, CancellationToken token);
    Task<SchedulerJob?> GetJobByIdAsync(string tenant, Guid id, CancellationToken token);
    Task<IReadOnlyList<SchedulerJob>> ListJobsAsync(string tenant, CancellationToken token);
    Task<(IReadOnlyList<SchedulerJob> Items, long Total)> QueryJobsAsync(string tenant, string? keyword, string? status, int page, int pageSize, CancellationToken token, string? sortBy = null, string? sortOrder = null);
    Task<bool> LockJobAsync(SchedulerJob job, CancellationToken token);
    Task AddJobAsync(SchedulerJob job, CancellationToken token);
    Task<bool> SaveJobAsync(SchedulerJob job, long expectedVersion, Guid expectedToken, CancellationToken token);
    Task<SchedulerRun?> GetRunAsync(string tenant, string nId, CancellationToken token);
    Task<SchedulerRun?> GetInvocationAsync(string tenant, string key, CancellationToken token);
    Task<IReadOnlyList<SchedulerRun>> ListRunsAsync(string tenant, CancellationToken token);
    Task<(IReadOnlyList<SchedulerRun> Items, long Total)> QueryRunsAsync(string tenant, Guid? jobId, string? status, string? source, DateTimeOffset from, DateTimeOffset until, int page, int pageSize, CancellationToken token, string? sortBy = null, string? sortOrder = null);
    Task<bool> HasUnfinishedAsync(string tenant, Guid jobId, CancellationToken token);
    Task<IReadOnlyList<SchedulerRun>> GetPendingExecutionsAsync(CancellationToken token);
    Task AddRunAsync(SchedulerRun run, CancellationToken token);
    Task<bool> SaveRunAsync(SchedulerRun run, long expectedVersion, CancellationToken token);
    Task<bool> TryStartAsync(SchedulerRun run, Guid executionToken, string worker, string engineReference, DateTimeOffset now, CancellationToken token);
    Task<IReadOnlyList<SchedulerRun>> GetActiveAsync(CancellationToken token);
    Task<int> CleanupHistoryAsync(DateTimeOffset before, CancellationToken token);
}

/// <summary>原生引擎适配，不拥有业务状态和副作用。</summary>
public interface ISchedulerEngine
{
    bool Enabled { get; }
    SchedulerPreview Preview(SchedulerScheduleInput input);
    Task SynchronizeAsync(SchedulerJob job, CancellationToken token);
    Task<string> EnqueueAsync(SchedulerRun run, CancellationToken token);
    Task<SchedulerEnginePlan> GetPlanAsync(SchedulerJob job, CancellationToken token);
    Task<SchedulerAttemptHistory> GetAttemptsAsync(string? engineReference, CancellationToken token);
    Task<bool?> IsWorkerAliveAsync(string? workerReference, CancellationToken token);
}

public sealed record SchedulerEnginePlan(string SyncState, DateTimeOffset? NextFireOn, string? ErrorCode = null);

/// <summary>既有outbox对调度内部命令的本地消费入口。</summary>
public interface ISchedulerCommandDispatcher
{
    Task DispatchAsync(string eventType, string tenantNId, string payload, CancellationToken token);
}

/// <summary>对账所属业务的一次租户执行公开端口。</summary>
public interface ISystemDataReconciliation
{
    Task ReconcileTenantAsync(string tenant, CancellationToken cancellationToken);
}

public sealed class SchedulerDuplicateException(Exception innerException) : Exception("调度记录已存在。", innerException);

/// <summary>部署级调度参数；模式切换只允许排空后的部署操作。</summary>
public sealed class SchedulerOptions
{
    public const string SectionName = "SystemData:Scheduler";
    public bool Enabled { get; set; }
    public int WorkerConcurrency { get; set; } = 4;
    public int HistoryDays { get; set; } = 90;
    public string ReconciliationMode { get; set; } = "Legacy";
}
