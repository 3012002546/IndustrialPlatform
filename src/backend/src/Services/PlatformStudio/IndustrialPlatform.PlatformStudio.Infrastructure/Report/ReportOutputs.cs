using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

public sealed class ReportOutputs(ReportRuns runs, ReportOwnedFiles authorization, StudioDatabase database, IStudioResourceStore resources)
{
    public async Task<object> ListAsync(StudioPrincipal principal, int page, int pageSize, CancellationToken token, string? resourceNId = null)
    {
        if (page < 1 || pageSize is < 1 or > 200) throw new StudioException("STUDIO_VALIDATION", "分页无效。");
        var query = database.Client.Queryable<StudioReportRun>().Where(r => r.TenantNId == principal.TenantNId && r.RequestedBy == principal.SubjectNId && r.ExecutionKind == "Production");
        if (resourceNId is not null)
        {
            var resource = await resources.FindResourceAsync(principal.TenantNId, resourceNId, token) ?? throw new StudioException("STUDIO_NOT_FOUND", "报告不存在。", 404);
            var revisions = await database.Client.Queryable<StudioRevision>().Where(r => r.TenantNId == principal.TenantNId && r.ResourceId == resource.Id).Select(r => r.Id).ToListAsync(token);
            query = query.Where(r => r.TemplateRevisionId != null && revisions.Contains(r.TemplateRevisionId.Value));
        }
        var entries = await query.OrderByDescending(r => r.CreatedOn).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token); var output = new List<ReportRunState>();
        foreach (var entry in entries) { try { output.Add(await runs.GetAsync(principal, entry.NId, token)); } catch (StudioException e) when (e.StatusCode is 403 or 404) { } }
        return new { items = output, page, pageSize, totalCount = (await query.CountAsync(token)).ToString(System.Globalization.CultureInfo.InvariantCulture) };
    }
    public async Task<object> DetailAsync(StudioPrincipal principal, string runNId, CancellationToken token)
    {
        var run = await runs.GetAuthorizedRunAsync(principal, runNId, token); var state = ReportRuns.State(run);
        var artifacts = run.Status == "Succeeded" ? await database.Client.Queryable<StudioReportArtifact>().Where(a => a.TenantNId == principal.TenantNId && a.RunId == run.Id).OrderBy(a => a.CreatedOn).ToListAsync(token) : [];
        return new { state.RunNId, state.ExecutionKind, state.Status, state.StateVersion, state.ExecutionEpoch, state.ExpiresOn, state.ErrorCode, run.CreatedOn, run.StartedOn, run.CompletedOn, run.Format, run.ErrorLocation, progress = new { stage = StudioJson.Text(JsonNode.Parse(run.ProgressJson)?["stage"]), pages = JsonNode.Parse(run.ProgressJson)?["pages"] }, hasDataSnapshot = JsonNode.Parse(run.ProgressJson)?["dataSnapshot"] is not null, sourceManifest = run.SourceManifestJson is null ? null : JsonNode.Parse(run.SourceManifestJson), artifacts = artifacts.Select(a => new { a.NId, a.Purpose, a.Format, a.Sha256, contentLength = a.ContentLength.ToString(System.Globalization.CultureInfo.InvariantCulture), a.PageCount, contentUrl = "/api/v1/studio/report-runs/" + Uri.EscapeDataString(runNId) + "/artifacts/" + Uri.EscapeDataString(a.NId) + "/content" }) };
    }
    public async Task<object> PreviewAsync(StudioPrincipal principal, string nId, CancellationToken token)
    {
        var run = await runs.GetAuthorizedRunAsync(principal, nId, token); var state = ReportRuns.State(run);
        if (run.ExecutionKind != "Preview") throw new StudioException("STUDIO_NOT_FOUND", "预览不存在。", 404);
        var manifest = run.PreviewManifestJson is null ? new JsonObject() : JsonNode.Parse(run.PreviewManifestJson)!.AsObject();
        var pages = new JsonArray((manifest["pages"] as JsonArray ?? []).OfType<JsonObject>().Select(p => (JsonNode)new JsonObject { ["pageNo"] = p["pageNo"]?.DeepClone(), ["contentUrl"] = "/api/v1/studio/reports/previews/" + Uri.EscapeDataString(nId) + "/pages/" + p["pageNo"] + "/content" }).ToArray());
        return new { state.Status, state.StateVersion, state.ExpiresOn, state.ErrorCode, pages, assemblyPlan = manifest["assemblyPlan"], diagnostics = new JsonArray() };
    }
    public async Task<Stream> PreviewPageAsync(StudioPrincipal principal, string nId, int pageNo, CancellationToken token)
        => await authorization.ReadPreviewPageAsync(principal, nId, pageNo, token);
    public async Task<(Stream Content, string ContentType)> ArtifactAsync(StudioPrincipal principal, string runNId, string artifactNId, CancellationToken token)
    {
        var (context, run) = await authorization.AuthorizeAsync(principal, runNId, token);
        var artifact = await database.Client.Queryable<StudioReportArtifact>().Where(a => a.TenantNId == principal.TenantNId && a.RunId == run.Id && a.NId == artifactNId).FirstUtcAsync(database.Provider, token) ?? throw new StudioException("STUDIO_NOT_FOUND", "产物不存在。", 404);
        return (await authorization.ReadAuthorizedAsync(context, run, artifact.FileReferenceNId, token), artifact.Format switch { "Pdf" => "application/pdf", "Html" => "text/html", "Xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", _ => "application/json" });
    }
    public async Task<Stream> OpenWorkbookSourceAsync(StudioPrincipal principal, string artifactNId, CancellationToken token)
    {
        var artifact = await database.Client.Queryable<StudioReportArtifact>().Where(a => a.TenantNId == principal.TenantNId && a.NId == artifactNId && a.Format == "Xlsx" && a.Purpose == "Output").FirstUtcAsync(database.Provider, token) ?? throw new StudioException("STUDIO_NOT_FOUND", "xlsx报告产物不存在。", 404);
        var run = await database.Client.Queryable<StudioReportRun>().Where(r => r.TenantNId == principal.TenantNId && r.Id == artifact.RunId).FirstUtcAsync(database.Provider, token) ?? throw new StudioException("STUDIO_NOT_FOUND", "报告运行不存在。", 404);
        return (await ArtifactAsync(principal, run.NId, artifact.NId, token)).Content;
    }
}
