using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Report;

/// <summary>报告受理、operation核对及取消；不执行取数/布局，不将调度成功当作产物成功。</summary>
public sealed class ReportRuns(IReportRunStore runs, IStudioResourceStore resources, IStudioIdentity identity,
    IStudioAuthorization authorization, IStudioJobs jobs, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ReportRunState> AcceptFrozenAsync(StudioPrincipal principal, FrozenReportInput input, string operationKey, CancellationToken token)
    {
        principal = await identity.RefreshAsync(principal, token);
        StudioCommands.ValidateKey(operationKey);
        if (input.ExecutionKind is not ("Production" or "Preview") || input.Format is not ("Pdf" or "Html" or "Xlsx")
            || Encoding.UTF8.GetByteCount(input.ParametersJson) > 65536 || JsonNode.Parse(input.ParametersJson) is not JsonObject || input.SortJson.Length > 8192 || JsonNode.Parse(input.SortJson) is not JsonArray)
            throw new StudioException("STUDIO_REPORT_ACCEPT_INVALID", "报告受理输入无效。", 422);
        if ((input.RetryOfRunId is null) != (input.RetryMode is null) || input.RetryMode is not (null or "RenderSnapshot" or "NewData") || input.ExecutionKind == "Preview" && input.RetryMode is not null) throw new StudioException("STUDIO_REPORT_ACCEPT_INVALID", "重试依据无效。", 422);
        var hash = StudioJson.Hash(StudioJson.Canonicalize(JsonSerializer.SerializeToNode(input with { TraceId = "" }, Json)));
        var existing = await runs.FindOperationAsync(principal.TenantNId, operationKey, token);
        if (existing is not null) return await ReplayAsync(principal, existing, hash, token);
        var now = clock.GetUtcNow();
        var run = new StudioReportRun
        {
            NId = "report-" + Guid.NewGuid().ToString("N"), TenantNId = principal.TenantNId, CreatedOn = now,
            ExecutionKind = input.ExecutionKind, TemplateRevisionId = input.TemplateRevisionId,
            PreviewSnapshotJson = input.PreviewSnapshotJson,
            // The applied STU-004 CHECK requires a value for Preview; it no longer controls lifetime.
            PreviewExpiresOn = input.ExecutionKind == "Preview" ? DateTimeOffset.MaxValue : null,
            OperationKey = operationKey, RequestedBy = principal.SubjectNId, ParametersJson = input.ParametersJson,
            AccessSnapshotJson = JsonSerializer.Serialize(new { principal, identityEvidence = await identity.CaptureExecutionEvidenceAsync(principal, token), executionSort = JsonNode.Parse(input.SortJson) }, Json), Format = input.Format, TraceId = input.TraceId,
            RetryOfRunId = input.RetryOfRunId, RetryMode = input.RetryMode,
        };
        await AuthorizeAsync(principal, run, token);
        var receipt = new StudioOperationReceipt
        {
            NId = "receipt-" + Guid.NewGuid().ToString("N"), TenantNId = run.TenantNId, CreatedOn = now,
            OperationKind = run.ExecutionKind == "Preview" ? "ReportPreview" : "ReportRun", OperationKey = operationKey,
            ResourceNId = run.NId, ActorUserNId = principal.SubjectNId, RequestHash = hash,
            Status = "Succeeded", CompletedOn = now, ResponseJson = JsonSerializer.Serialize(State(run), Json),
        };
        try { await runs.AcceptAsync(run, receipt, token); }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            var replay = await runs.FindOperationAsync(principal.TenantNId, operationKey, token);
            if (replay is null) throw;
            return await ReplayAsync(principal, replay, hash, token);
        }
        return State(await DispatchAsync(run, token));
    }

    private async Task<ReportRunState> ReplayAsync(StudioPrincipal principal, StudioReportRun run, string hash, CancellationToken token)
    {
        var receipt = await resources.FindReceiptAsync(run.TenantNId, run.ExecutionKind == "Preview" ? "ReportPreview" : "ReportRun", run.OperationKey, token);
        if (run.RequestedBy != principal.SubjectNId || receipt?.ActorUserNId != principal.SubjectNId || receipt.RequestHash != hash || receipt.ResourceNId != run.NId)
            throw new StudioException("STUDIO_IDEMPOTENCY_CONFLICT", "操作键已用于不同报告请求。", 409);
        await AuthorizeAsync(principal, run, token);
        if (run.Status == "PendingDispatch") run = await DispatchAsync(run, token);
        return State(run);
    }

    public async Task<ReportRunState> GetAsync(StudioPrincipal principal, string runNId, CancellationToken token)
        => State(await GetAuthorizedRunAsync(principal, runNId, token));

    public async Task<StudioReportRun> GetAuthorizedRunAsync(StudioPrincipal principal, string runNId, CancellationToken token)
    {
        var (_, run) = await RequireAsync(principal, runNId, token);
        if (run.Status is "Succeeded" or "Failed" or "Cancelled") return run;
        // 预览与正式运行都以任务状态和当前授权为准，不按固定TTL失效。
        var state = await jobs.GetByOperationAsync(run, token);
        return state is null ? run : await SynchronizeAsync(run, state, token);
    }

    public async Task<ReportRunState> CancelAsync(StudioPrincipal principal, string runNId, string expectedVersion, CancellationToken token)
    {
        var (_, run) = await RequireAsync(principal, runNId, token);
        if (run.StateVersion != StudioJson.Version(expectedVersion, "stateVersion")) throw Conflict();
        if (run.Status is "Succeeded" or "Failed" or "Cancelled" or "CancelRequested" or "Unknown") return State(run);
        var state = await jobs.GetByOperationAsync(run, token);
        if (state is null)
        {
            // 无受理并不证明其他请求未在提交：不能本地取消后放任并发提交。
            if (run.Status == "PendingDispatch") throw new StudioException("STUDIO_REPORT_DISPATCH_UNCONFIRMED", "尚未确认调度受理；先核对同一operation，不能假定执行已停止。", 409);
            throw new StudioException("STUDIO_JOBS_UNAVAILABLE", "调度运行尚无法核对。", 503);
        }
        return State(await SynchronizeAsync(run, await jobs.RequestCancelAsync(run, state.StateVersion, token), token));
    }

    private async Task<StudioReportRun> DispatchAsync(StudioReportRun run, CancellationToken token)
    {
        var state = await jobs.GetByOperationAsync(run, token) ?? await jobs.SubmitOnceAsync(run, token);
        return await SynchronizeAsync(run, state, token);
    }

    private async Task<StudioReportRun> SynchronizeAsync(StudioReportRun run, ReportJobState state, CancellationToken token)
    {
        if (state.OperationKey != run.OperationKey || run.SchedulerRunNId is not null && run.SchedulerRunNId != state.RunNId) throw Conflict();
        if (run.Status == "PendingDispatch")
        {
            run.SchedulerRunNId = state.RunNId;
            run.Status = "Queued";
            run = await AdvanceAsync(run, token);
        }
        if (run.Status is "Unknown" or "Succeeded" or "Failed" or "Cancelled") return run;
        var next = state.Status switch
        {
            "Unknown" or "Succeeded" => "Unknown", // Succeeded尚须产物确认；前置绝不生成成功产物。
            "Failed" or "TimedOut" => "Failed",
            "Cancelled" => "Cancelled",
            "CancelRequested" => "CancelRequested",
            _ => run.Status,
        };
        if (next == run.Status) return run;
        run.Status = next; run.ErrorCode = state.Status == "Succeeded" ? "STUDIO_REPORT_RESULT_UNCONFIRMED" : state.ReasonCode;
        run.CompletedOn = next is "Unknown" or "Failed" or "Cancelled" ? state.CompletedOn ?? clock.GetUtcNow() : null;
        return await AdvanceAsync(run, token);
    }

    /// <summary>只有PF07实际执行上下文可领取；重投不重复推进执行世代。</summary>
    public async Task<StudioReportRun?> BeginExecutionAsync(string tenant, string runNId, string schedulerRunNId, string operationKey, CancellationToken token)
    {
        var run = await FindAsync(tenant, runNId, token);
        var access = JsonNode.Parse(run.AccessSnapshotJson)!.AsObject();
        var evidence = StudioJson.Text(access["identityEvidence"]) ?? throw Conflict();
        await AuthorizeAsync(await identity.RefreshExecutionAsync(evidence, token), run, token);
        var state = await jobs.GetByOperationAsync(run, token);
        if (state is null || state.RunNId != schedulerRunNId || state.OperationKey != operationKey || run.OperationKey != operationKey || state.Status != "Running") return null;
        run = await SynchronizeAsync(run, state, token);
        if (run.Status != "Queued") return null;
        run.Status = "Running"; run.StartedOn = state.StartedOn ?? clock.GetUtcNow();
        if (!await runs.AdvanceAsync(run, run.StateVersion, run.ExecutionEpoch, token)) return null;
        return await FindAsync(tenant, runNId, token);
    }

    public async Task<bool> FinishUnavailableAsync(StudioReportRun run, bool cancelled, CancellationToken token)
    {
        var current = await FindAsync(run.TenantNId, run.NId, token);
        if (current.ExecutionEpoch != run.ExecutionEpoch || current.Status is not ("Running" or "CancelRequested")) return false;
        current.Status = cancelled ? "Cancelled" : "Failed"; current.CompletedOn = clock.GetUtcNow();
        current.ErrorCode = cancelled ? "STUDIO_REPORT_CANCELLED" : "STUDIO_RENDERER_UNAVAILABLE";
        return await runs.AdvanceAsync(current, current.StateVersion, run.ExecutionEpoch, token);
    }

    /// <summary>元数据、取消及所属文件共用当前身份、原范围和资源授权，不复制较弱的文件权限分支。</summary>
    public async Task<(StudioPrincipal Principal, StudioReportRun Run)> RequireAsync(StudioPrincipal principal, string runNId, CancellationToken token)
    {
        principal = await identity.RefreshAsync(principal, token);
        var run = await FindAsync(principal.TenantNId, runNId, token);
        await AuthorizeAsync(principal, run, token);
        return (principal, run);
    }
    public async Task<(StudioPrincipal Principal, StudioReportRun Run)> RequireExecutionAsync(string evidence, string runNId, CancellationToken token)
    {
        var principal = await identity.RefreshExecutionAsync(evidence, token);
        var run = await FindAsync(principal.TenantNId, runNId, token);
        await AuthorizeAsync(principal, run, token);
        return (principal, run);
    }
    private async Task AuthorizeAsync(StudioPrincipal principal, StudioReportRun run, CancellationToken token)
    {
        if (principal.TenantNId != run.TenantNId || principal.SubjectNId != run.RequestedBy) throw new StudioException("STUDIO_FORBIDDEN", "仅原发起人可操作报告运行。", 403);
        var original = JsonNode.Parse(run.AccessSnapshotJson)?["principal"]?.Deserialize<StudioPrincipal>(Json) ?? throw Conflict();
        if (original.FactoryNIds.Except(principal.FactoryNIds, StringComparer.Ordinal).Any()) throw new StudioException("STUDIO_FORBIDDEN", "原报告工厂范围已被撤回。", 403);
        if (run.ExecutionKind == "Production")
        {
            if (run.TemplateRevisionId is null || run.PreviewSnapshotJson is not null) throw Conflict();
            var revision = await resources.FindRevisionByIdAsync(run.TenantNId, run.TemplateRevisionId.Value, token) ?? throw Conflict();
            await ResourceAsync(principal, revision.ResourceId, "studio.report.run", "Run", token);
        }
        else
        {
            authorization.RequirePermission(principal, "studio.report.design");
            if (run.TemplateRevisionId is not null || run.Format != "Html" || run.PreviewSnapshotJson is null || Encoding.UTF8.GetByteCount(run.PreviewSnapshotJson) > 8 * 1024 * 1024
                || JsonNode.Parse(run.PreviewSnapshotJson) is not JsonObject snapshot || snapshot["document"] is not JsonObject || StudioJson.Text(snapshot["kind"]) is not ("Report" or "ReportBundle")) throw Conflict();
            if (StudioJson.Text(snapshot["resourceNId"]) is { } nId)
            {
                var resource = await resources.FindResourceAsync(run.TenantNId, nId, token) ?? throw Conflict();
                await ResourceAsync(principal, resource, "studio.report.design", "Design", token);
            }
            if (snapshot["dependencySnapshots"] is JsonArray dependencies)
            {
                if (dependencies.Count > 200) throw Conflict();
                foreach (var dependency in dependencies.OfType<JsonObject>())
                {
                    var resource = await resources.FindResourceAsync(run.TenantNId, StudioJson.Text(dependency["resourceNId"]) ?? "", token) ?? throw Conflict();
                    if (resource.Kind == "Dataset") await authorization.RequireAsync(principal, resource, "studio.dataset.run", "Run", token);
                    else await ResourceAsync(principal, resource, "studio.report.design", "Design", token);
                }
            }
        }
    }
    private async Task ResourceAsync(StudioPrincipal principal, Guid id, string permission, string capability, CancellationToken token)
    {
        var resource = await resources.FindResourceByIdAsync(principal.TenantNId, id, token);
        await ResourceAsync(principal, resource, permission, capability, token);
    }
    private async Task ResourceAsync(StudioPrincipal principal, StudioResource? resource, string permission, string capability, CancellationToken token)
    {
        if (resource is null || resource.IsDeleted || resource.Kind is not ("Report" or "ReportBundle")) throw Conflict();
        await authorization.RequireAsync(principal, resource, permission, capability, token);
    }
    private async Task<StudioReportRun> FindAsync(string tenant, string nId, CancellationToken token) => await runs.FindAsync(tenant, nId, token) ?? throw new StudioException("STUDIO_NOT_FOUND", "报告运行不存在。", 404);
    private async Task<StudioReportRun> AdvanceAsync(StudioReportRun run, CancellationToken token)
    {
        // 并发worker可能先完成映射/领取；重新读当前记录，不覆盖其较新状态。
        await runs.AdvanceAsync(run, run.StateVersion, run.ExecutionEpoch, token);
        return await FindAsync(run.TenantNId, run.NId, token);
    }
    public static ReportRunState State(StudioReportRun run) => new(run.NId, run.ExecutionKind, run.Status, run.StateVersion.ToString(CultureInfo.InvariantCulture), run.ExecutionEpoch.ToString(CultureInfo.InvariantCulture), null, run.ErrorCode);
    private static StudioException Conflict() => new("STUDIO_REPORT_STATE_CONFLICT", "报告状态、快照或执行映射已改变，请重新核对。", 409);
}
