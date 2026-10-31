using System.Security.Cryptography;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

public sealed class ReportRetries(ReportRuns runs, ReportOwnedFiles files, StudioDatabase database)
{
    public async Task<ReportRunState> RetryAsync(StudioPrincipal principal, string runNId, string expectedVersion, string mode, string operationKey, string traceId, CancellationToken token)
    {
        var original = await runs.GetAuthorizedRunAsync(principal, runNId, token);
        if (original.ExecutionKind != "Production" || original.Status is not ("Succeeded" or "Failed" or "Cancelled") || original.StateVersion != StudioJson.Version(expectedVersion) || mode is not ("RenderSnapshot" or "NewData")) throw new StudioException("REPORT_RETRY_INVALID", "只允许对安全终态的正式运行选择快照重排或重新取数；Unknown必须先核对。", 409);
        if (mode == "RenderSnapshot") _ = await LoadSnapshotAsync(principal, original, token);
        var access = JsonNode.Parse(original.AccessSnapshotJson)!.AsObject();
        return await runs.AcceptFrozenAsync(principal, new("Production", original.TemplateRevisionId, null, original.ParametersJson, original.Format, traceId, access["executionSort"]?.ToJsonString() ?? "[]", original.Id, mode), operationKey, token);
    }
    public async Task<JsonArray> LoadSnapshotAsync(StudioPrincipal principal, StudioReportRun original, CancellationToken token, string? executionEvidence = null)
    {
        var metadata = JsonNode.Parse(original.ProgressJson)?["dataSnapshot"] as JsonObject ?? throw new StudioException("REPORT_SNAPSHOT_MISSING", "原运行没有完整、已确认的采集快照。", 409);
        await using var source = await files.ReadRetrySnapshotAsync(principal, original.NId, StudioJson.RequiredString(metadata, "referenceNId"), token, executionEvidence);
        using var content = await StudioWorkbooks.ReadBoundedAsync(source, token);
        if (Convert.ToHexStringLower(SHA256.HashData(content.ToArray())) != StudioJson.RequiredString(metadata, "sha256")) throw new StudioException("REPORT_SNAPSHOT_INVALID", "原采集快照校验失败。", 409);
        return JsonNode.Parse(content.ToArray()) as JsonArray ?? throw new StudioException("REPORT_SNAPSHOT_INVALID", "原采集快照格式无效。", 422);
    }
    public async Task<JsonArray> LoadOriginalSnapshotAsync(StudioPrincipal principal, StudioReportRun retry, CancellationToken token)
    {
        var original = retry.RetryOfRunId is { } id ? await database.Client.Queryable<StudioReportRun>().Where(run => run.TenantNId == retry.TenantNId && run.Id == id).FirstUtcAsync(database.Provider, token) : null;
        if (original is null || original.TemplateRevisionId != retry.TemplateRevisionId || original.RequestedBy != retry.RequestedBy || original.ParametersJson != retry.ParametersJson) throw new StudioException("REPORT_SNAPSHOT_INVALID", "快照重试依据不匹配。", 409);
        var evidence = StudioJson.Text(JsonNode.Parse(retry.AccessSnapshotJson)!["identityEvidence"]) ?? throw new StudioException("STUDIO_UNAUTHENTICATED", "执行身份依据缺失。", 401);
        var (_, current) = await runs.RequireExecutionAsync(evidence, original.NId, token);
        return await LoadSnapshotAsync(principal, current, token, evidence);
    }
}
