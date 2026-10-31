using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

/// <summary>receipt与run同事务受理；只更新执行列，冻结身份及定义永不随状态回写。</summary>
public sealed class ReportRunStore(StudioDatabase database, IStudioResourceStore resources) : IReportRunStore
{
    public async Task<StudioReportRun?> FindAsync(string tenant, string runNId, CancellationToken token) => await database.Client.Queryable<StudioReportRun>().Where(row => row.TenantNId == tenant && row.NId == runNId).FirstUtcAsync(database.Provider, token);
    public async Task<StudioReportRun?> FindOperationAsync(string tenant, string operationKey, CancellationToken token) => await database.Client.Queryable<StudioReportRun>().Where(row => row.TenantNId == tenant && row.OperationKey == operationKey).FirstUtcAsync(database.Provider, token);
    public async Task AcceptAsync(StudioReportRun run, StudioOperationReceipt receipt, CancellationToken token)
    {
        if (run.Status != "PendingDispatch" || run.StateVersion != 0 || run.ExecutionEpoch != 0 || run.SchedulerRunNId is not null
            || receipt.TenantNId != run.TenantNId || receipt.ActorUserNId != run.RequestedBy || receipt.OperationKey != run.OperationKey || receipt.ResourceNId != run.NId
            || receipt.OperationKind != (run.ExecutionKind == "Preview" ? "ReportPreview" : "ReportRun")) throw new StudioException("STUDIO_REPORT_ACCEPT_INVALID", "报告受理快照不一致。", 422);
        if (run.ExecutionKind == "Preview" && (run.TemplateRevisionId is not null || run.PreviewSnapshotJson is null || run.PreviewExpiresOn != DateTimeOffset.MaxValue || run.Format != "Html")
            || run.ExecutionKind == "Production" && (run.TemplateRevisionId is null || run.PreviewSnapshotJson is not null || run.PreviewExpiresOn is not null)
            || run.ExecutionKind is not ("Production" or "Preview")) throw new StudioException("STUDIO_REPORT_SNAPSHOT_INVALID", "正式/预览快照身份无效。", 422);
        await resources.TransactionAsync(async () =>
        {
            await database.Client.Insertable(run).ExecuteCommandAsync(token);
            await resources.InsertReceiptAsync(receipt, token);
            return true;
        }, token);
    }
    public async Task<bool> AdvanceAsync(StudioReportRun run, long expectedVersion, long expectedEpoch, CancellationToken token)
    {
        var current = await FindAsync(run.TenantNId, run.NId, token);
        if (current is null || current.StateVersion != expectedVersion || current.ExecutionEpoch != expectedEpoch || !ReportRunTransitions.Allows(current.Status, run.Status)) return false;
        if (run.Status == "Succeeded") throw new StudioException("STUDIO_RENDERER_UNAVAILABLE", "产物确认尚未准入，不允许标记生成成功。", 503);
        var epoch = expectedEpoch + (run.Status == "Running" ? 1 : 0);
        return await database.Client.Updateable<StudioReportRun>().SetColumns(row => new StudioReportRun
        {
            Status = run.Status, StateVersion = expectedVersion + 1, ExecutionEpoch = epoch,
            SchedulerRunNId = run.SchedulerRunNId,
            ErrorCode = run.ErrorCode, ErrorLocation = run.ErrorLocation, ProgressJson = run.ProgressJson,
        })
        // Captured nullable dates become untyped text parameters when null in SqlSugar.
        // Emit SQL NULL explicitly; retain UTC timestamp parameters for actual times.
        .SetColumnsIF(run.StartedOn is null, row => row.StartedOn == null)
        .SetColumnsIF(run.StartedOn is not null, row => row.StartedOn == run.StartedOn.GetValueOrDefault())
        .SetColumnsIF(run.CompletedOn is null, row => row.CompletedOn == null)
        .SetColumnsIF(run.CompletedOn is not null, row => row.CompletedOn == run.CompletedOn.GetValueOrDefault())
        .Where(row => row.TenantNId == run.TenantNId && row.NId == run.NId && row.StateVersion == expectedVersion && row.ExecutionEpoch == expectedEpoch).ExecuteCommandAsync(token) == 1;
    }
    public async Task<bool> CompleteAsync(StudioReportRun run, IReadOnlyList<StudioReportArtifact> confirmedArtifacts, CancellationToken token)
        => await CompleteFromAsync(run, confirmedArtifacts, "Running", token);
    public async Task<bool> ResolveUnknownAsync(StudioReportRun run, IReadOnlyList<StudioReportArtifact> confirmedArtifacts, CancellationToken token)
    {
        if (run.Status == "Succeeded") return await CompleteFromAsync(run, confirmedArtifacts, "Unknown", token);
        if (run.Status != "Failed" || confirmedArtifacts.Count != 0) throw new StudioException("REPORT_RESOLUTION_INVALID", "核对终态无效。", 422);
        return await database.Client.Updateable<StudioReportRun>().SetColumns(row => new StudioReportRun { Status = "Failed", StateVersion = run.StateVersion + 1, CompletedOn = run.CompletedOn, ErrorCode = run.ErrorCode, ProgressJson = run.ProgressJson }).Where(row => row.TenantNId == run.TenantNId && row.NId == run.NId && row.StateVersion == run.StateVersion && row.ExecutionEpoch == run.ExecutionEpoch && row.Status == "Unknown").ExecuteCommandAsync(token) == 1;
    }
    private async Task<bool> CompleteFromAsync(StudioReportRun run, IReadOnlyList<StudioReportArtifact> confirmedArtifacts, string before, CancellationToken token)
    {
        if (run.Status != "Succeeded" || run.SourceManifestJson is null || run.ExecutionKind == "Preview" && (run.PreviewManifestJson is null || confirmedArtifacts.Count != 0) || run.ExecutionKind == "Production" && (!confirmedArtifacts.Any(a => a.Purpose == "Output") || confirmedArtifacts.Any(a => a.RunId != run.Id || a.TenantNId != run.TenantNId || string.IsNullOrEmpty(a.FileReferenceNId)))) throw new StudioException("REPORT_OUTPUT_INVALID", "成功必须有完整文件确认依据。", 422);
        return await resources.TransactionAsync(async () =>
        {
            var updated = await database.Client.Updateable<StudioReportRun>().SetColumns(row => new StudioReportRun { Status = "Succeeded", StateVersion = run.StateVersion + 1, CompletedOn = run.CompletedOn, SourceManifestJson = run.SourceManifestJson, PreviewManifestJson = run.PreviewManifestJson, ProgressJson = run.ProgressJson, ErrorCode = null, ErrorLocation = null }).Where(row => row.TenantNId == run.TenantNId && row.NId == run.NId && row.ExecutionEpoch == run.ExecutionEpoch && row.StateVersion == run.StateVersion && row.Status == before).ExecuteCommandAsync(token);
            if (updated != 1) return false;
            if (confirmedArtifacts.Count > 0) await database.Client.Insertable(confirmedArtifacts.ToArray()).ExecuteCommandAsync(token);
            return true;
        }, token);
    }
    public async Task<bool> RecordProgressAsync(StudioReportRun claim, CancellationToken token) => await database.Client.Updateable<StudioReportRun>().SetColumns(row => new StudioReportRun { ProgressJson = claim.ProgressJson, SourceManifestJson = claim.SourceManifestJson }).Where(row => row.TenantNId == claim.TenantNId && row.Id == claim.Id && row.ExecutionEpoch == claim.ExecutionEpoch && row.Status == "Running").ExecuteCommandAsync(token) == 1;
}
