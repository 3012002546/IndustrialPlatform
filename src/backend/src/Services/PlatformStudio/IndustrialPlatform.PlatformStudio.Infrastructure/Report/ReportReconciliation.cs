using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.RuntimeSupport.Files;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

/// <summary>Only server-owned stopped receipts and confirmed owned-file bytes may resolve Unknown. Browser input supplies a reason, never proof or process IDs.</summary>
public sealed class ReportReconciliation(ReportRuns execution, IReportRunStore runs, IStudioJobs jobs, IStudioAuthorization authorization,
    IStudioResourceStore resources, IOwnedFileService files, StudioDeploymentProfile profile, TimeProvider clock)
{
    public async Task<ReportRunState> ResolveAsync(StudioPrincipal principal, string runNId, string stateVersion, string reason, CancellationToken token)
    {
        var authorized = await execution.RequireAsync(principal, runNId, token); principal = authorized.Principal; var run = authorized.Run;
        authorization.RequirePermission(principal, "studio.report.reconcile");
        if (run.ExecutionKind == "Production")
        {
            var revision = run.TemplateRevisionId is { } revisionId ? await resources.FindRevisionByIdAsync(principal.TenantNId, revisionId, token) : null;
            var resource = revision is null ? null : await resources.FindResourceByIdAsync(principal.TenantNId, revision.ResourceId, token);
            if (resource is null || resource.IsDeleted) throw Invalid();
            await authorization.RequireAsync(principal, resource, "studio.report.view", "View", token);
            await authorization.RequireAsync(principal, resource, "studio.report.export", "Export", token);
        }
        if (run.Status != "Unknown" || run.StateVersion != StudioJson.Version(stateVersion) || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 1000) throw Invalid();
        var progress = JsonNode.Parse(run.ProgressJson)!.AsObject();
        var evidence = progress["rendererEvidence"] as JsonObject ?? throw Invalid(); var directory = StudioJson.RequiredString(evidence, "directory", 4096);
        if (!Path.IsPathFullyQualified(directory) || !string.Equals(Path.GetFullPath(Path.GetDirectoryName(directory)!), Path.GetFullPath(profile.Renderer.WorkingRoot ?? ""), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(directory).StartsWith("report-" + StudioJson.Hash(run.NId)[..24] + "-", StringComparison.Ordinal) || !Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw Invalid();
        async Task<byte[]> Read(string name, int limit)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path) || new FileInfo(path).Length > limit || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Invalid();
            return await File.ReadAllBytesAsync(path, token);
        }
        var inputHash = Convert.ToHexStringLower(SHA256.HashData(await Read("input.json", 50 * 1024 * 1024)));
        var receipt = JsonNode.Parse(await Read("renderer-stopped.json", 4096))!.AsObject();
        if (inputHash != StudioJson.Text(evidence["inputSha256"]) || StudioJson.Text(receipt["inputSha256"]) != inputHash || StudioJson.Text(receipt["engineManifestHash"]) != StudioJson.Text(evidence["engineManifestHash"]) || StudioJson.Text(receipt["reason"]) is not ("Completed" or "Failed" or "Cancelled" or "TimedOut")) throw Invalid();
        var proof = new JsonObject { ["stoppedReceipt"] = receipt.DeepClone(), ["inputSha256"] = inputHash };
        var artifacts = new List<StudioReportArtifact>(); var succeeded = false;
        var context = new TrustedServiceContext(principal.TenantNId, "platform-studio", principal.SubjectNId);
        async Task VerifyFile(string reference, string fileNId, string hash)
        {
            var status = await files.GetStatusAsync(context, reference, token);
            if (status.Status != "Active" || status.FileNId != fileNId) throw Invalid();
            string? cursor = null; var belongs = false;
            do { var page = await files.ListByOwnerAsync(context, run.ExecutionKind == "Preview" ? "ReportPreview" : "ReportArtifact", run.NId, cursor, token); belongs |= page.Items.Any(item => item.ReferenceNId == reference); cursor = page.NextCursor; } while (!belongs && cursor is not null);
            if (!belongs) throw Invalid();
            var ticket = await files.GetReadTicketAsync(context, reference, token); await using var content = await files.ReadAsync(context, new(reference, principal.SubjectNId, ticket.AuthorizationTicket), token);
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(content, token)) != hash) throw Invalid();
        }
        if (StudioJson.Text(receipt["reason"]) == "Completed" && File.Exists(Path.Combine(directory, "completion-evidence.json")))
        {
            var bytes = await Read("completion-evidence.json", 4 * 1024 * 1024); var completed = JsonNode.Parse(bytes)!.AsObject();
            if (StudioJson.Text(completed["runNId"]) != run.NId || StudioJson.Version(StudioJson.RequiredString(completed, "executionEpoch")) != run.ExecutionEpoch || StudioJson.Text(completed["rendererEvidence"]?["inputSha256"]) != inputHash || StudioJson.Text(completed["rendererEvidence"]?["engineManifestHash"]) != StudioJson.Text(evidence["engineManifestHash"])) throw Invalid();
            artifacts = completed["artifacts"]!.Deserialize<List<StudioReportArtifact>>(StudioCommands.JsonOptions) ?? throw Invalid();
            foreach (var artifact in artifacts) { if (artifact.RunId != run.Id || artifact.TenantNId != run.TenantNId) throw Invalid(); await VerifyFile(artifact.FileReferenceNId, artifact.FileNId, artifact.Sha256); }
            if (run.ExecutionKind == "Preview")
            {
                if (artifacts.Count != 0 || completed["previewManifest"]?["pages"] is not JsonArray pages || pages.Count is < 1 or > 2000) throw Invalid();
                foreach (var page in pages.OfType<JsonObject>()) await VerifyFile(StudioJson.RequiredString(page, "referenceNId"), StudioJson.RequiredString(page, "fileNId"), StudioJson.RequiredString(page, "sha256"));
            }
            else if (artifacts.Count != 2 || artifacts.Count(a => a.Purpose == "Output" && a.Format == run.Format) != 1 || artifacts.Count(a => a.Purpose == "DataSnapshot") != 1) throw Invalid();
            run.SourceManifestJson = completed["sourceManifest"]?.ToJsonString() ?? throw Invalid(); run.PreviewManifestJson = completed["previewManifest"]?.ToJsonString(); progress = completed["progress"]!.AsObject();
            proof["completionSha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)); succeeded = true;
        }
        var proofHash = StudioJson.Hash(StudioJson.Canonicalize(proof)); var scheduler = await jobs.GetByOperationAsync(run, token) ?? throw Invalid();
        // PF07 independently checks native attempt completion / worker liveness before releasing its active slot.
        await jobs.ResolveAsync(run, scheduler.StateVersion, succeeded ? "ConfirmedSucceeded" : "ConfirmedFailed", reason.Trim(), "studio-report-proof:" + proofHash, token);
        progress["resolution"] = new JsonObject { ["evidenceSha256"] = proofHash, ["decision"] = succeeded ? "ConfirmedSucceeded" : "ConfirmedFailed", ["resolvedOn"] = clock.GetUtcNow(), ["resolvedBy"] = principal.SubjectNId };
        run.ProgressJson = progress.ToJsonString(); run.Status = succeeded ? "Succeeded" : "Failed"; run.ErrorCode = succeeded ? null : "REPORT_RESULT_VERIFIED_FAILED"; run.CompletedOn = clock.GetUtcNow();
        await resources.TransactionAsync(async () => { if (!await runs.ResolveUnknownAsync(run, artifacts, token)) throw Invalid(); await resources.AppendAuditAsync(principal, "studio.report.reconcile", run.NId, new() { ["decision"] = run.Status, ["reason"] = reason.Trim(), ["evidenceSha256"] = proofHash }, token); return true; }, token);
        return await execution.GetAsync(principal, run.NId, token);
    }
    private static StudioException Invalid() => new("REPORT_RESOLUTION_UNCONFIRMED", "运行状态或服务端停止、文件证据未能核实；保持Unknown及原占用。", 409);
}
