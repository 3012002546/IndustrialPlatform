using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.RuntimeSupport.Files;
namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

/// <summary>报告文件消费边界独立于StudioAsset；按运行、发起人及当前授权签发File读取票据。</summary>
public sealed class ReportOwnedFiles(ReportRuns execution, IStudioAuthorization authorization, IStudioResourceStore resources, IOwnedFileService files)
{
    public async Task<Stream> ReadPreviewPageAsync(StudioPrincipal principal, string runNId, int pageNo, CancellationToken token)
    {
        var authorized = await execution.RequireAsync(principal, runNId, token);
        var run = authorized.Run;
        if (run.ExecutionKind != "Preview" || run.Status != "Succeeded" || run.PreviewManifestJson is null) throw new StudioException("STUDIO_REPORT_OUTPUT_UNAVAILABLE", "预览尚无确认内容。", 409);
        var page = (System.Text.Json.Nodes.JsonNode.Parse(run.PreviewManifestJson)?["pages"] as System.Text.Json.Nodes.JsonArray ?? []).OfType<System.Text.Json.Nodes.JsonObject>().SingleOrDefault(p => p["pageNo"]?.GetValue<int>() == pageNo) ?? throw new StudioException("STUDIO_NOT_FOUND", "预览页不存在。", 404);
        var reference = StudioJson.RequiredString(page, "referenceNId");
        var context = new TrustedServiceContext(authorized.Principal.TenantNId, "platform-studio", authorized.Principal.SubjectNId);
        return await ReadAuthorizedAsync(context, run, reference, token);
    }
    internal async Task<(TrustedServiceContext Context, StudioReportRun Run)> AuthorizeAsync(StudioPrincipal principal, string runNId, CancellationToken token, bool retrySnapshot = false, string? executionEvidence = null)
    {
        var authorized = executionEvidence is null ? await execution.RequireAsync(principal, runNId, token) : await execution.RequireExecutionAsync(executionEvidence, runNId, token);
        principal = authorized.Principal;
        var run = authorized.Run;
        if (run.ExecutionKind == "Preview")
        {
            authorization.RequirePermission(principal, "studio.report.design");
            if (run.Status != "Succeeded" || run.PreviewManifestJson is null) throw new StudioException("STUDIO_REPORT_OUTPUT_UNAVAILABLE", "预览尚无确认内容。", 409);
            return (new(principal.TenantNId, "platform-studio", principal.SubjectNId), run);
        }
        var revision = run.TemplateRevisionId is { } id ? await resources.FindRevisionByIdAsync(principal.TenantNId, id, token) : null;
        if (run.Status != "Succeeded" && !(retrySnapshot && run.Status is "Failed" or "Cancelled")) throw new StudioException("STUDIO_REPORT_OUTPUT_UNAVAILABLE", "报告尚无已确认成功产物。", 409);
        var resource = revision is null ? null : await resources.FindResourceByIdAsync(principal.TenantNId, revision.ResourceId, token);
        if (resource is null || resource.IsDeleted || resource.Kind is not ("Report" or "ReportBundle")) throw new StudioException("STUDIO_NOT_FOUND", "报告资源不可用。", 404);
        await authorization.RequireAsync(principal, resource, "studio.report.view", "View", token);
        await authorization.RequireAsync(principal, resource, "studio.report.export", "Export", token);
        return (new(principal.TenantNId, "platform-studio", principal.SubjectNId), run);
    }
    public async Task<Stream> ReadRetrySnapshotAsync(StudioPrincipal principal, string runNId, string referenceNId, CancellationToken token, string? executionEvidence = null)
    {
        var (context, run) = await AuthorizeAsync(principal, runNId, token, retrySnapshot: true, executionEvidence);
        var snapshot = System.Text.Json.Nodes.JsonNode.Parse(run.ProgressJson)?["dataSnapshot"];
        if (StudioJson.Text(snapshot?["referenceNId"]) != referenceNId) throw new StudioException("REPORT_SNAPSHOT_MISSING", "快照引用不属于该次采集。", 409);
        return await ReadAuthorizedAsync(context, run, referenceNId, token);
    }
    internal async Task<Stream> ReadAuthorizedAsync(TrustedServiceContext context, StudioReportRun run, string referenceNId, CancellationToken token)
    {
        var ticket = await TicketAsync(context, run, run.NId, referenceNId, token);
        return await files.ReadAsync(context, new(referenceNId, context.ActorUserNId, ticket.AuthorizationTicket), token);
    }
    private async Task<OwnedFileReadTicketV1> TicketAsync(TrustedServiceContext context, StudioReportRun run, string runNId, string referenceNId, CancellationToken token)
    {
        string? cursor = null;
        do
        {
            var page = await files.ListByOwnerAsync(context, run.ExecutionKind == "Preview" ? "ReportPreview" : "ReportArtifact", runNId, cursor, token);
            if (page.Items.Any(item => item.ReferenceNId == referenceNId)) return await files.GetReadTicketAsync(context, referenceNId, token);
            cursor = page.NextCursor;
        } while (cursor is not null);
        throw new StudioException("STUDIO_NOT_FOUND", "该运行不存在此文件引用。", 404);
    }
}
