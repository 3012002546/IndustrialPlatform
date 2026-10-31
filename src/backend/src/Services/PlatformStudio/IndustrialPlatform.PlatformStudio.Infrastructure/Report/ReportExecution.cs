using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Contracts.Scheduler;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

/// <summary>The shared real worker pipeline. Unknown and any incomplete/scanning output can never become success.</summary>
public sealed class ReportExecution(IReportRunStore runs, IStudioResourceStore resources, ReportRuns execution, IStudioAuthorization authorization,
    StudioPublishing publishing, ReportDesign design, ReportCommandRenderer renderer, IOwnedFileService owned, IFileService files, TimeProvider clock, ReportRetries retries, ReportResources imageResources)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<ScheduledTaskResult> ExecuteAsync(StudioReportRun claim, CancellationToken token)
    {
        try
        {
            if (!renderer.Available) throw new StudioException("STUDIO_RENDERER_UNAVAILABLE", "服务端分页引擎尚未准入。", 503);
            var access = JsonNode.Parse(claim.AccessSnapshotJson)!.AsObject();
            // Identity evidence is provider-owned structured content, not a 128-character identifier.
            var identityEvidence = StudioJson.Text(access["identityEvidence"]) ?? throw new StudioException("STUDIO_UNAUTHENTICATED", "执行身份依据缺失。", 401);
            var (principal, _) = await execution.RequireExecutionAsync(identityEvidence, claim.NId, token);
            var snapshot = await DefinitionAsync(principal, claim, token);
            if (snapshot.Kind == "Report") snapshot = snapshot with { Document = ReportDesign.ApplyQuerySort(snapshot.Document, access["executionSort"] as JsonArray) };
            var parameters = DatasetValues.Parameters(principal, snapshot.Document, JsonNode.Parse(claim.ParametersJson)!.AsObject());
            var trusted = new JsonObject { ["tenant"] = principal.TenantNId, ["user"] = principal.SubjectNId, ["authorizedFactoryIds"] = new JsonArray(principal.FactoryNIds.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()) };
            var resolver = design.Resolver(principal);
            if (claim.RetryMode == "RenderSnapshot") resolver.RestoreSnapshot(await retries.LoadOriginalSnapshotAsync(principal, claim, token));
            var plan = await new ReportPlanner(resolver, trusted, (d, p) => DatasetValues.Parameters(principal, d, p)).PlanAsync(snapshot, parameters, token); var flow = ReportExpander.Expand(plan);
            if (flow.Blocks.Count == 0) throw new StudioException("DATA_REQUIRED", "没有实际正文内容，不生成空白产物。", 422);
            var sourceManifest = resolver.Manifest;
            foreach (var asset in await imageResources.FreezeAsync(flow, identityEvidence, token)) sourceManifest.Add(asset?.DeepClone());
            var paper = snapshot.Document["paper"] as JsonObject ?? snapshot.Dependencies.Values.FirstOrDefault(d => d.Kind == "Report")?.Document["paper"] as JsonObject ?? throw new StudioException("REPORT_LAYOUT_UNSUPPORTED", "报告纸张定义缺失。", 422);
            var workspace = renderer.CreateWorkspace(claim.NId);
            var dataPath = Path.Combine(workspace, "data-snapshot.json"); await File.WriteAllTextAsync(dataPath, resolver.SnapshotDocument.ToJsonString(), token);
            // Preview cannot be retried from a persisted data snapshot. Keep the
            // local renderer input, and archive source data only for production.
            Confirmed? collected = null;
            var progress = new JsonObject { ["stage"] = "Collected" };
            if (claim.ExecutionKind != "Preview")
            {
                collected = await PersistAsync(principal, claim, "data-snapshot", dataPath, "application/json", token);
                progress["dataSnapshot"] = new JsonObject { ["referenceNId"] = collected.ReferenceNId, ["fileNId"] = collected.FileNId, ["sha256"] = collected.Hash, ["length"] = collected.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            }
            claim.SourceManifestJson = sourceManifest.ToJsonString(); claim.ProgressJson = progress.ToJsonString();
            if (!await runs.RecordProgressAsync(claim, token))
            {
                if ((await runs.FindAsync(claim.TenantNId, claim.NId, token))?.Status == "CancelRequested") throw new OperationCanceledException(token);
                return new("Unknown", "STUDIO_REPORT_RESULT_UNCONFIRMED");
            }
            var rendered = await renderer.RenderAsync(claim.NId, flow, paper, claim.ExecutionKind == "Preview", token, claim.Format, workspace, async evidence =>
            {
                progress["rendererEvidence"] = evidence; progress["stage"] = "Rendering"; claim.ProgressJson = progress.ToJsonString();
                if (!await runs.RecordProgressAsync(claim, token))
                {
                    if ((await runs.FindAsync(claim.TenantNId, claim.NId, token))?.Status == "CancelRequested") throw new OperationCanceledException(token);
                    throw new StudioException("STUDIO_REPORT_RESULT_UNCONFIRMED", "执行世代已变化，拒绝启动旧渲染。", 409);
                }
            });
            var authorized = await execution.RequireExecutionAsync(identityEvidence, claim.NId, token);
            principal = authorized.Principal;
            _ = await DefinitionAsync(principal, claim, token); // Recheck original source session, grants and pins before file confirmation.
            await imageResources.FreezeAsync(flow, identityEvidence, token);
            var current = authorized.Run;
            if (current.ExecutionEpoch == claim.ExecutionEpoch && current.Status == "CancelRequested") throw new OperationCanceledException(token);
            if (current.ExecutionEpoch != claim.ExecutionEpoch || current.Status != "Running") return new("Unknown", "STUDIO_REPORT_RESULT_UNCONFIRMED");
            var artifacts = new List<StudioReportArtifact>(); var previewPages = new JsonArray();
            if (claim.ExecutionKind == "Preview")
            {
                foreach (var page in (rendered.Result["pages"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    var pageNo = page["pageNo"]?.GetValue<int>() ?? 0; var name = StudioJson.RequiredString(page, "htmlFile");
                    if (pageNo < 1 || pageNo > rendered.PageCount || Path.GetFileName(name) != name) throw new StudioException("REPORT_OUTPUT_INVALID", "分页HTML路径无效。", 422);
                    var path = Path.Combine(rendered.Directory, name); var output = await PersistAsync(principal, claim, "page-" + pageNo, path, "text/html", token);
                    previewPages.Add(new JsonObject { ["pageNo"] = pageNo, ["referenceNId"] = output.ReferenceNId, ["fileNId"] = output.FileNId, ["sha256"] = output.Hash });
                }
                if (previewPages.Count != rendered.PageCount || previewPages.OfType<JsonObject>().Select(p => p["pageNo"]!.GetValue<int>()).Distinct().Count() != rendered.PageCount) throw new StudioException("REPORT_OUTPUT_INVALID", "预览分页HTML不完整。", 422);
                current.PreviewManifestJson = new JsonObject { ["pages"] = previewPages, ["assemblyPlan"] = JsonSerializer.SerializeToNode(plan.Instances.Select(i => new { i.InstanceKey, i.Decision, i.Reason, i.RowCount }), Json), ["engineManifest"] = rendered.EngineManifest.DeepClone() }.ToJsonString();
            }
            else
            {
                var outputPath = rendered.PdfPath; var contentType = "application/pdf";
                if (claim.Format == "Html") { outputPath = Path.Combine(rendered.Directory, "result.html"); contentType = "text/html"; }
                else if (claim.Format == "Xlsx")
                {
                    outputPath = Path.Combine(rendered.Directory, "result.xlsx"); contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                    await using var stream = File.Create(outputPath);
                    await using var writer = await IndustrialPlatform.RuntimeSupport.Exports.StreamingXlsxWriter.CreateAsync(stream, token);
                    try { foreach (var row in (rendered.Result["workbookRows"] as JsonArray ?? throw new StudioException("REPORT_OUTPUT_INVALID", "工作簿正文行缺失。", 422)).OfType<JsonArray>()) await writer.WriteRowAsync(row.Select(StudioJson.Text), token); }
                    catch (Exception error) when (error is ArgumentOutOfRangeException or System.Xml.XmlException) { throw new StudioException("REPORT_XLSX_CELL_UNSUPPORTED", "工作簿单元格内容超限或含无效字符，未截断内容。", 422); }
                }
                else if (claim.Format != "Pdf") throw new StudioException("REPORT_FORMAT_UNSUPPORTED", "输出格式未登记。", 422);
                var output = await PersistAsync(principal, claim, "output-" + claim.Format.ToLowerInvariant(), outputPath, contentType, token);
                artifacts.Add(Artifact(claim, output, "Output", claim.Format, rendered));
                artifacts.Add(Artifact(claim, collected!, "DataSnapshot", "Json", rendered));
            }
            current.SourceManifestJson = sourceManifest.ToJsonString(); progress["stage"] = "FileConfirmed"; progress["pages"] = rendered.PageCount; current.ProgressJson = progress.ToJsonString(); current.Status = "Succeeded"; current.CompletedOn = clock.GetUtcNow();
            var completion = new JsonObject { ["runNId"] = claim.NId, ["executionEpoch"] = claim.ExecutionEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture), ["rendererEvidence"] = progress["rendererEvidence"]?.DeepClone(), ["sourceManifest"] = JsonNode.Parse(current.SourceManifestJson), ["previewManifest"] = current.PreviewManifestJson is null ? null : JsonNode.Parse(current.PreviewManifestJson), ["progress"] = progress.DeepClone(), ["artifacts"] = JsonSerializer.SerializeToNode(artifacts, Json) };
            await File.WriteAllTextAsync(Path.Combine(workspace, "completion-evidence.json"), completion.ToJsonString(), token);
            if (!await runs.CompleteAsync(current, artifacts, token)) return new("Unknown", "STUDIO_REPORT_RESULT_UNCONFIRMED");
            return new("Succeeded", "REPORT_GENERATED");
        }
        catch (ReportRendererUnconfirmedException) { await FinishAsync(claim, "Unknown", "RENDERER_STOP_UNCONFIRMED", null); return new("Unknown", "RENDERER_STOP_UNCONFIRMED"); }
        catch (OperationCanceledException) { await FinishAsync(claim, "Cancelled", "STUDIO_REPORT_CANCELLED", null); return new("Cancelled", "STUDIO_REPORT_CANCELLED"); }
        catch (StudioException e) { await FinishAsync(claim, "Failed", e.Code, e.Path); return new("Failed", e.Code, e.Message); }
        catch (Exception) { await FinishAsync(claim, "Unknown", "STUDIO_REPORT_RESULT_UNCONFIRMED", null); return new("Unknown", "STUDIO_REPORT_RESULT_UNCONFIRMED"); }
    }
    private async Task FinishAsync(StudioReportRun claim, string status, string code, string? path)
    {
        var current = await runs.FindAsync(claim.TenantNId, claim.NId, CancellationToken.None);
        if (current is null || current.ExecutionEpoch != claim.ExecutionEpoch || current.Status is not ("Running" or "CancelRequested")) return;
        current.Status = status; current.ErrorCode = code; current.ErrorLocation = path; current.CompletedOn = clock.GetUtcNow();
        await runs.AdvanceAsync(current, current.StateVersion, current.ExecutionEpoch, CancellationToken.None);
    }
    private async Task<ReportDefinitionSnapshot> DefinitionAsync(StudioPrincipal principal, StudioReportRun run, CancellationToken token)
    {
        if (principal.TenantNId != run.TenantNId || principal.SubjectNId != run.RequestedBy) throw new StudioException("STUDIO_FORBIDDEN", "报告主体已失效。", 403);
        JsonObject document; string kind; string hash; string? resourceNId = null; JsonArray references; var dependencies = new Dictionary<string, ReportDefinitionSnapshot>();
        if (run.ExecutionKind == "Preview")
        {
            authorization.RequirePermission(principal, "studio.report.design"); var frozen = JsonNode.Parse(run.PreviewSnapshotJson!)!.AsObject(); resourceNId = StudioJson.Text(frozen["resourceNId"]); kind = StudioJson.RequiredString(frozen, "kind"); document = frozen["document"]!.AsObject(); hash = StudioJson.Hash(StudioJson.Canonicalize(frozen)); references = frozen["dependencySnapshots"] as JsonArray ?? [];
            if (StudioJson.Text(frozen["resourceNId"]) is { } rootNId) _ = await publishing.RequireResourceAsync(principal, rootNId, "design", "Design", token);
            foreach (var child in references.OfType<JsonObject>())
            {
                var nId = StudioJson.RequiredString(child, "resourceNId"); var resource = await publishing.RequireResourceAsync(principal, nId, StudioJson.Text(child["kind"]) == "Dataset" ? "run" : "design", StudioJson.Text(child["kind"]) == "Dataset" ? "Run" : "Design", token);
                dependencies[nId + "/" + StudioJson.RequiredString(child, "revisionNId")] = new(resource.Kind, child["document"]!.AsObject(), new Dictionary<string, ReportDefinitionSnapshot>(), StudioJson.Text(child["contentHash"]) ?? StudioJson.Hash(StudioJson.Canonicalize(child["document"])), nId);
            }
        }
        else
        {
            var revision = await resources.FindRevisionByIdAsync(principal.TenantNId, run.TemplateRevisionId!.Value, token) ?? throw new StudioException("DEPENDENCY_CHANGED", "固定报告修订不存在。", 409);
            var resource = await resources.FindResourceByIdAsync(principal.TenantNId, revision.ResourceId, token) ?? throw new StudioException("DEPENDENCY_CHANGED", "报告不存在。", 409);
            _ = await publishing.RequireRuntimeAsync(principal, resource.NId, revision.NId, token); resourceNId = resource.NId; kind = resource.Kind; document = StudioPublishing.Parse(revision.DocumentJson); hash = revision.ContentHash; references = JsonNode.Parse(revision.DependenciesJson)!.AsArray();
            foreach (var child in references.OfType<JsonObject>())
            {
                var nId = StudioJson.RequiredString(child, "resourceNId"); var revisionNId = StudioJson.RequiredString(child, "revisionNId"); var (target, fixedRevision) = await publishing.RequireRuntimeAsync(principal, nId, revisionNId, token);
                dependencies[nId + "/" + revisionNId] = new(target.Kind, StudioPublishing.Parse(fixedRevision.DocumentJson), new Dictionary<string, ReportDefinitionSnapshot>(), fixedRevision.ContentHash, nId);
            }
        }
        return new(kind, document, dependencies, hash, resourceNId);
    }
    private sealed record Confirmed(string FileNId, string ReferenceNId, string Hash, long Length);
    private async Task<Confirmed> PersistAsync(StudioPrincipal principal, StudioReportRun run, string name, string path, string contentType, CancellationToken token)
    {
        await using var content = File.OpenRead(path);
        if (content.Length > 50 * 1024 * 1024) throw new StudioException("STUDIO_LIMIT", "报告产物超过50MiB。", 429);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, token)); content.Position = 0;
        var context = new TrustedServiceContext(principal.TenantNId, "platform-studio", principal.SubjectNId);
        var upload = await OwnedFileUploads.CommitAsync(owned, files, context, new("render-" + StudioJson.Hash(run.NId + "/" + name), "platform-studio", run.ExecutionKind == "Preview" ? "ReportPreview" : "ReportArtifact", run.NId, Path.GetFileName(path), contentType, content.Length, hash), content, token);
        var prepared = upload.Prepared; var file = upload.File;
        var confirmed = await OwnedFileUploads.WaitForConfirmationAsync(owned, context, upload, TimeSpan.FromSeconds(30), token);
        if (confirmed.Status != "Active") throw new StudioException("REPORT_FILE_UNCONFIRMED", "文件扫描或引用未确认，报告未成功。", 409);
        return new(file.FileNId, prepared.ReferenceNId, hash, content.Length);
    }
    private StudioReportArtifact Artifact(StudioReportRun run, Confirmed file, string purpose, string format, ReportRenderedOutput rendered) => new() { NId = "artifact-" + Guid.NewGuid().ToString("N"), TenantNId = run.TenantNId, RunId = run.Id, CreatedOn = clock.GetUtcNow(), Purpose = purpose, Format = format, FileNId = file.FileNId, FileReferenceNId = file.ReferenceNId, Sha256 = file.Hash, ContentLength = file.Length, PageCount = purpose == "Output" ? rendered.PageCount : null, EngineManifestJson = rendered.EngineManifest.ToJsonString() };
}
