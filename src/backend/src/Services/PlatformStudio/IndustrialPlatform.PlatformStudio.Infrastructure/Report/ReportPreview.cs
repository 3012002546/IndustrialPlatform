using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

public sealed record ReportPreviewPage(int PageNo, string Html);
public sealed record ReportPreviewDownload(byte[] Content, string ContentType, string FileName);
public sealed record ReportPreviewResult(string Status, IReadOnlyList<ReportPreviewPage> Pages, JsonNode AssemblyPlan, JsonArray Diagnostics);

/// <summary>Interactive preview reuses the production planner and renderer without jobs or archived files.</summary>
public sealed class ReportPreview(ReportDesign design, IStudioIdentity identity, ReportCommandRenderer renderer, ReportResources resources, IStudioAuthorization authorization)
{
    private static readonly SemaphoreSlim Slots = new(2, 2);
    public Task<ReportPreviewResult> RenderAsync(StudioPrincipal principal, ReportPreviewInput input, CancellationToken token)
        => GenerateAsync(principal, input, "Html", false, async (rendered, assemblyPlan, token) =>
        {
            var pages = new List<ReportPreviewPage>();
            long bytes = 0;
            foreach (var page in (rendered.Result["pages"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var number = page["pageNo"]?.GetValue<int>() ?? 0;
                var name = StudioJson.RequiredString(page, "htmlFile");
                if (number != pages.Count + 1 || Path.GetFileName(name) != name) throw new StudioException("REPORT_OUTPUT_INVALID", "预览分页依据无效。", 422);
                var path = Path.Combine(rendered.Directory, name);
                bytes += new FileInfo(path).Length;
                if (bytes > 50 * 1024 * 1024) throw new StudioException("STUDIO_LIMIT", "预览内容超过50MiB。", 429);
                pages.Add(new(number, await File.ReadAllTextAsync(path, Encoding.UTF8, token)));
            }
            if (pages.Count != rendered.PageCount) throw new StudioException("REPORT_OUTPUT_INVALID", "预览分页不完整。", 422);
            return new ReportPreviewResult("Succeeded", pages, assemblyPlan, rendered.Result["diagnostics"]?.DeepClone() as JsonArray ?? []);
        }, token);

    /// <summary>草稿下载只返回本次文件，不创建任务、正式产物或文件归档。</summary>
    public Task<ReportPreviewDownload> ExportAsync(StudioPrincipal principal, ReportPreviewInput input, string format, CancellationToken token)
    {
        if (!ReportFormats.Supports(format)) throw new StudioException("REPORT_FORMAT_UNSUPPORTED", "输出格式未登记。", 422);
        return GenerateAsync(principal, input, format, true, async (rendered, _, token) =>
        {
            var path = await ReportExporter.WriteAsync(rendered, format, token, preview: true);
            if (new FileInfo(path).Length > 50 * 1024 * 1024) throw new StudioException("STUDIO_LIMIT", "草稿下载内容超过50MiB。", 429);
            var type = ReportFormats.ContentType(format);
            return new ReportPreviewDownload(await File.ReadAllBytesAsync(path, token), type, "preview-" + format.ToLowerInvariant() + ReportFormats.Extension(type));
        }, token);
    }

    private async Task<T> GenerateAsync<T>(StudioPrincipal principal, ReportPreviewInput input, string format, bool export,
        Func<ReportRenderedOutput, JsonNode, CancellationToken, Task<T>> read, CancellationToken token)
    {
        if (!await Slots.WaitAsync(0, token)) throw new StudioException("STUDIO_LIMIT", "预览正在生成，请稍后再试。", 429);
        string? workspace = null;
        var safeToClean = true;
        try
        {
            principal = await identity.RefreshAsync(principal, token);
            if (export) authorization.RequirePermission(principal, "studio.report.export");
            var frozen = await design.FreezeAsync(principal, input, token);
            if (Encoding.UTF8.GetByteCount(frozen.Frozen.ToJsonString()) > 8 * 1024 * 1024 || Encoding.UTF8.GetByteCount(frozen.Parameters.ToJsonString()) > 65536)
                throw new StudioException("STUDIO_LIMIT", "预览定义或参数超过预算。", 429);
            var evidence = await identity.CaptureExecutionEvidenceAsync(principal, token);
            var trusted = new JsonObject { ["tenant"] = principal.TenantNId, ["user"] = principal.SubjectNId, ["authorizedFactoryIds"] = new JsonArray(principal.FactoryNIds.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()) };
            var resolver = design.Resolver(principal);
            var definition = frozen.Definition;
            if (definition.Kind == "Report") definition = definition with { Document = ReportDesign.ApplyQuerySort(definition.Document, null) };
            var plan = await new ReportPlanner(resolver, trusted, (d, p) => DatasetValues.Parameters(principal, d, p)).PlanAsync(definition, frozen.Parameters, token);
            var flow = ReportExpander.Expand(plan);
            if (flow.Blocks.Count == 0) throw new StudioException("DATA_REQUIRED", "没有实际正文内容，不生成空白预览。", 422);
            await resources.FreezeAsync(flow, evidence, token);
            var paper = frozen.Definition.Document["paper"] as JsonObject ?? frozen.Definition.Dependencies.Values.FirstOrDefault(d => d.Kind == "Report")?.Document["paper"] as JsonObject ?? throw new StudioException("REPORT_LAYOUT_UNSUPPORTED", "报告纸张定义缺失。", 422);
            var previewId = "preview-" + Guid.NewGuid().ToString("N");
            workspace = renderer.CreateWorkspace(previewId);
            var rendered = await renderer.RenderAsync(previewId, flow, paper, true, token, format, workspace, confirmedStopped: () => safeToClean = true, started: () => safeToClean = false);
            var result = await read(rendered, JsonSerializer.SerializeToNode(plan.Instances.Select(i => new { i.InstanceKey, i.Decision, i.Reason, i.RowCount }), StudioCommands.JsonOptions)!, token);
            // Rendering can outlive a permission change; refresh authority before returning data.
            var current = await identity.RefreshExecutionAsync(evidence, token);
            if (current.TenantNId != principal.TenantNId || current.SubjectNId != principal.SubjectNId || principal.FactoryNIds.Except(current.FactoryNIds, StringComparer.Ordinal).Any())
                throw new StudioException("STUDIO_FORBIDDEN", "原预览访问范围已被撤回。", 403);
            await design.AuthorizeFrozenAsync(current, frozen.Frozen, token);
            if (export) authorization.RequirePermission(current, "studio.report.export");
            await resources.FreezeAsync(flow, evidence, token);
            return result;
        }
        catch (ReportRendererUnconfirmedException)
        {
            throw new StudioException("RENDERER_STOP_UNCONFIRMED", "预览引擎停止状态未确认。", 503);
        }
        finally
        {
            if (safeToClean && workspace is not null)
            {
                try { Directory.Delete(workspace, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            // An unconfirmed child retains both the working directory and the occupied slot.
            if (safeToClean) Slots.Release();
        }
    }
}
