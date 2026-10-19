using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Scheduler;
using IndustrialPlatform.SystemData.Domain.Scheduler;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Scheduler;

/// <summary>两张自有表的租户仓储；数据库唯一活动槽才是业务执行资格。</summary>
public sealed class SqlSchedulerStore(SqlSugarDbContext context) : ISchedulerStore
{
    public async Task<SchedulerJob?> GetJobAsync(string tenant, string nId, CancellationToken token) =>
        Job(await context.SqlSugar.Queryable<SchedulerJobTable>().Where(row => row.TenantNId == tenant && row.NId == nId).FirstAsync(token));
    public async Task<SchedulerJob?> GetJobByIdAsync(string tenant, Guid id, CancellationToken token) =>
        Job(await context.SqlSugar.Queryable<SchedulerJobTable>().Where(row => row.TenantNId == tenant && row.Id == id).FirstAsync(token));
    public async Task<IReadOnlyList<SchedulerJob>> ListJobsAsync(string tenant, CancellationToken token) =>
        (await context.SqlSugar.Queryable<SchedulerJobTable>().Where(row => row.TenantNId == tenant).OrderBy(row => row.Name).ToListAsync(token)).Select(row => Job(row)!).ToArray();
    public async Task<(IReadOnlyList<SchedulerJob> Items, long Total)> QueryJobsAsync(string tenant, string? keyword, string? status, int page, int pageSize, CancellationToken token, string? sortBy = null, string? sortOrder = null)
    {
        var query = context.SqlSugar.Queryable<SchedulerJobTable>().Where(row => row.TenantNId == tenant)
            .WhereIF(status != "Deleted", row => !row.IsDeleted)
            .WhereIF(status == "Deleted", row => row.IsDeleted)
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), row => row.Name.Contains(keyword!))
            .WhereIF(!string.IsNullOrWhiteSpace(status) && status != "Deleted", row => row.Status == status);
        var total = await query.CountAsync(token);
        var rows = await query.OrderBy(Order(sortBy, sortOrder, false)).ToPageListAsync(page, pageSize, token);
        return (rows.Select(row => Job(row)!).ToArray(), total);
    }
    public async Task<bool> LockJobAsync(SchedulerJob job, CancellationToken token) =>
        await context.SqlSugar.Updateable<SchedulerJobTable>().SetColumns(row => row.OptimisticVersion == row.OptimisticVersion)
            .Where(row => row.Id == job.Id && row.TenantNId == job.TenantNId && row.OptimisticVersion == job.OptimisticVersion && row.ConcurrencyVersion == job.ConcurrencyVersion).ExecuteCommandAsync(token) == 1;
    public async Task AddJobAsync(SchedulerJob job, CancellationToken token)
    {
        try { await context.SqlSugar.Insertable(Table(job)).ExecuteCommandAsync(token); }
        catch (Exception exception) when (IsUniqueConflict(exception)) { throw new SchedulerDuplicateException(exception); }
    }
    public async Task<bool> SaveJobAsync(SchedulerJob job, long expectedVersion, Guid expectedToken, CancellationToken token) =>
        await context.SqlSugar.Updateable(Table(job)).Where(row => row.Id == job.Id && row.TenantNId == job.TenantNId
            && row.OptimisticVersion == expectedVersion && row.ConcurrencyVersion == expectedToken).ExecuteCommandAsync(token) == 1;
    public async Task<SchedulerRun?> GetRunAsync(string tenant, string nId, CancellationToken token) =>
        Run(await context.SqlSugar.Queryable<SchedulerRunTable>().Where(row => row.TenantNId == tenant && row.NId == nId).FirstAsync(token));
    public async Task<SchedulerRun?> GetInvocationAsync(string tenant, string key, CancellationToken token) =>
        Run(await context.SqlSugar.Queryable<SchedulerRunTable>().Where(row => row.TenantNId == tenant && row.InvocationKey == key).FirstAsync(token));
    public async Task<IReadOnlyList<SchedulerRun>> ListRunsAsync(string tenant, CancellationToken token) =>
        (await context.SqlSugar.Queryable<SchedulerRunTable>().Where(row => row.TenantNId == tenant)
            .OrderBy(row => row.AcceptedOn, OrderByType.Desc).ToListAsync(token)).Select(row => Run(row)!).ToArray();
    public async Task<(IReadOnlyList<SchedulerRun> Items, long Total)> QueryRunsAsync(string tenant, Guid? jobId, string? status, string? source, DateTimeOffset from, DateTimeOffset until, int page, int pageSize, CancellationToken token, string? sortBy = null, string? sortOrder = null)
    {
        var query = context.SqlSugar.Queryable<SchedulerRunTable>().Where(row => row.TenantNId == tenant && row.AcceptedOn >= from && row.AcceptedOn < until)
            .WhereIF(jobId != null, row => row.JobId == jobId)
            .WhereIF(!string.IsNullOrWhiteSpace(status), row => row.Status == status)
            .WhereIF(!string.IsNullOrWhiteSpace(source), row => row.Source == source);
        var total = await query.CountAsync(token);
        var rows = await query.OrderBy(Order(sortBy, sortOrder, true)).ToPageListAsync(page, pageSize, token);
        return (rows.Select(row => Run(row)!).ToArray(), total);
    }
    public Task<bool> HasUnfinishedAsync(string tenant, Guid jobId, CancellationToken token) =>
        context.SqlSugar.Queryable<SchedulerRunTable>().Where(row => row.TenantNId == tenant && row.JobId == jobId
            && row.Status != "Succeeded" && row.Status != "Failed" && row.Status != "TimedOut" && row.Status != "Cancelled").AnyAsync(token);
    public async Task<IReadOnlyList<SchedulerRun>> GetPendingExecutionsAsync(CancellationToken token) =>
        (await context.SqlSugar.Queryable<SchedulerRunTable>().Where(row => row.Status == "Queued" && row.EngineReference == null)
            .OrderBy(row => row.AcceptedOn).Take(100).ToListAsync(token)).Select(row => Run(row)!).ToArray();
    public async Task AddRunAsync(SchedulerRun run, CancellationToken token)
    {
        try { await context.SqlSugar.Insertable(Table(run)).ExecuteCommandAsync(token); }
        catch (Exception exception) when (IsUniqueConflict(exception)) { throw new SchedulerDuplicateException(exception); }
    }
    public async Task<bool> SaveRunAsync(SchedulerRun run, long expectedVersion, CancellationToken token) =>
        await context.SqlSugar.Updateable(Table(run)).Where(row => row.Id == run.Id && row.TenantNId == run.TenantNId && row.StateVersion == expectedVersion)
            .ExecuteCommandAsync(token) == 1;

    public async Task<bool> TryStartAsync(SchedulerRun run, Guid executionToken, string worker, string engineReference, DateTimeOffset now, CancellationToken token)
    {
        try
        {
            return await context.SqlSugar.Updateable<SchedulerRunTable>().SetColumns(row => new SchedulerRunTable
            {
                ActiveJobId = run.JobId, ExecutionToken = executionToken, WorkerReference = worker, EngineReference = engineReference,
                Status = "Running", AttemptsUsed = run.AttemptsUsed + 1, StartedOn = run.StartedOn ?? now,
                DeadlineOn = now.AddSeconds(run.TimeoutSeconds), CompletedOn = null, StateVersion = run.StateVersion + 1,
            }).Where(row => row.Id == run.Id && row.TenantNId == run.TenantNId && row.StateVersion == run.StateVersion
                && (row.Status == "Queued" || row.Status == "RetryWaiting") && row.ActiveJobId == null
                && (row.EngineReference == null || row.EngineReference == engineReference)
                && row.AttemptsUsed < row.MaxAttempts).ExecuteCommandAsync(token) == 1;
        }
        catch (Exception exception) when (IsUniqueConflict(exception)) { return false; }
    }

    public async Task<IReadOnlyList<SchedulerRun>> GetActiveAsync(CancellationToken token) =>
        (await context.SqlSugar.Queryable<SchedulerRunTable>().Where(row => row.ActiveJobId != null)
            .OrderBy(row => row.StartedOn).Take(1000).ToListAsync(token)).Select(row => Run(row)!).ToArray();
    public async Task<int> CleanupHistoryAsync(DateTimeOffset before, CancellationToken token)
    {
        var rows = await context.SqlSugar.Queryable<SchedulerRunTable>().Where(row => row.ActiveJobId == null && row.CompletedOn < before
            && (row.Status == "Succeeded" || row.Status == "Failed" || row.Status == "Cancelled" || row.Status == "TimedOut")).OrderBy(row => row.CompletedOn).Take(200).ToListAsync(token);
        return rows.Count == 0 ? 0 : await context.SqlSugar.Deleteable<SchedulerRunTable>().In(rows.Select(row => row.Id).ToArray()).ExecuteCommandAsync(token);
    }

    private static string Order(string? sortBy, string? sortOrder, bool run)
    {
        var columns = run ? new Dictionary<string,string>(StringComparer.Ordinal)
        { ["nId"]="n_id", ["jobName"]="job_name_snapshot", ["source"]="source", ["status"]="status", ["acceptedOn"]="accepted_on", ["startedOn"]="started_on", ["completedOn"]="completed_on", ["attemptsUsed"]="attempts_used", ["resultCode"]="result_code" }
            : new Dictionary<string,string>(StringComparer.Ordinal)
        { ["nId"]="n_id", ["name"]="name", ["handlerKey"]="handler_key", ["status"]="status", ["cronExpression"]="cron_expression", ["timeZoneId"]="time_zone_id" };
        if (string.IsNullOrEmpty(sortBy)) return run ? "accepted_on DESC,n_id ASC" : "name ASC,n_id ASC";
        if (!columns.TryGetValue(sortBy, out var column) || sortOrder is not ("asc" or "desc")) throw SchedulerAdministration.Error("SCHEDULER_SORT_INVALID", "排序字段或方向无效。");
        return column + (sortOrder == "desc" ? " DESC" : " ASC") + (column == "n_id" ? string.Empty : ",n_id ASC");
    }

    public static bool IsUniqueConflict(Exception exception) => exception is not OperationCanceledException &&
        (exception.Message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase)
         || exception.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
         || exception.Message.Contains("23505", StringComparison.Ordinal));

    private static SchedulerJob? Job(SchedulerJobTable? row)
    {
        if (row is null) return null;
        var job = new SchedulerJob(row.TenantNId, row.NId, row.Name, row.Description, row.HandlerKey, row.ParametersJson,
            row.CronExpression, row.TimeZoneId, row.TimeoutSeconds, row.MaxAttempts, row.RetrySafety, row.IsSystemOwned);
        job.LoadState(row.Id, row.IsFrozen, row.IsLocked, row.IsDeleted, row.Status, row.CreatedOn, row.LastUpdatedOn, row.OptimisticVersion, row.ConcurrencyVersion);
        return job;
    }
    private static SchedulerJobTable Table(SchedulerJob job) => new()
    {
        Id = job.Id, TenantNId = job.TenantNId, NId = job.NId, Name = job.Name, Description = job.Description,
        HandlerKey = job.HandlerKey, ParametersJson = job.ParametersJson, Status = job.Status, CronExpression = job.CronExpression,
        TimeZoneId = job.TimeZoneId, TimeoutSeconds = job.TimeoutSeconds, MaxAttempts = job.MaxAttempts, RetrySafety = job.RetrySafety,
        IsSystemOwned = job.IsSystemOwned, EntityType = job.EntityType, IsFrozen = job.IsFrozen, IsLocked = job.IsLocked,
        IsDeleted = job.IsDeleted, CreatedOn = job.CreatedOn, LastUpdatedOn = job.LastUpdatedOn,
        OptimisticVersion = job.OptimisticVersion, ConcurrencyVersion = job.ConcurrencyVersion,
    };
    private static SchedulerRun? Run(SchedulerRunTable? r) => r is null ? null : new()
    {
        Id = r.Id, NId = r.NId, TenantNId = r.TenantNId, CreatedOn = r.CreatedOn, JobId = r.JobId, Source = r.Source,
        InvocationKey = r.InvocationKey, RequestHash = r.RequestHash, OriginalRunNId = r.OriginalRunNId, OperationKey = r.OperationKey,
        RequestedByUserNId = r.RequestedByUserNId, AcceptedOn = r.AcceptedOn, Status = r.Status, JobNameSnapshot = r.JobNameSnapshot,
        HandlerKey = r.HandlerKey, ParametersJson = r.ParametersJson, TimeoutSeconds = r.TimeoutSeconds, MaxAttempts = r.MaxAttempts,
        RetrySafety = r.RetrySafety, ActiveJobId = r.ActiveJobId, ExecutionToken = r.ExecutionToken, WorkerReference = r.WorkerReference,
        AttemptsUsed = r.AttemptsUsed, StartedOn = r.StartedOn, DeadlineOn = r.DeadlineOn, CompletedOn = r.CompletedOn,
        CancelRequestedOn = r.CancelRequestedOn, ResultCode = r.ResultCode, ResultSummaryJson = r.ResultSummaryJson,
        EngineReference = r.EngineReference, TraceId = r.TraceId, StateVersion = r.StateVersion,
    };
    private static SchedulerRunTable Table(SchedulerRun r) => new()
    {
        Id = r.Id, NId = r.NId, TenantNId = r.TenantNId, CreatedOn = r.CreatedOn, JobId = r.JobId, Source = r.Source,
        InvocationKey = r.InvocationKey, RequestHash = r.RequestHash, OriginalRunNId = r.OriginalRunNId, OperationKey = r.OperationKey,
        RequestedByUserNId = r.RequestedByUserNId, AcceptedOn = r.AcceptedOn, Status = r.Status, JobNameSnapshot = r.JobNameSnapshot,
        HandlerKey = r.HandlerKey, ParametersJson = r.ParametersJson, TimeoutSeconds = r.TimeoutSeconds, MaxAttempts = r.MaxAttempts,
        RetrySafety = r.RetrySafety, ActiveJobId = r.ActiveJobId, ExecutionToken = r.ExecutionToken, WorkerReference = r.WorkerReference,
        AttemptsUsed = r.AttemptsUsed, StartedOn = r.StartedOn, DeadlineOn = r.DeadlineOn, CompletedOn = r.CompletedOn,
        CancelRequestedOn = r.CancelRequestedOn, ResultCode = r.ResultCode, ResultSummaryJson = r.ResultSummaryJson,
        EngineReference = r.EngineReference, TraceId = r.TraceId, StateVersion = r.StateVersion,
    };
}
