using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

public sealed record ReportRenderedOutput(string Directory, string PdfPath, int PageCount, JsonObject Result, JsonObject EngineManifest);
public sealed class ReportRendererUnconfirmedException() : Exception("Report renderer process termination is unconfirmed.");

/// <summary>Only an admitted, hash-pinned server command may render. No shell, browser print UI, download or fallback engine.</summary>
public sealed class ReportCommandRenderer(StudioDeploymentProfile profile)
{
    public bool Available => profile.Renderer.Admitted;
    public string CreateWorkspace(string runNId)
    {
        if (!Available) throw new StudioException("STUDIO_RENDERER_UNAVAILABLE", "服务端分页引擎尚未准入。", 503);
        profile.Renderer.Validate();
        var directory = Path.Combine(profile.Renderer.WorkingRoot!, "report-" + StudioJson.Hash(runNId)[..24] + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); return directory;
    }
    public async Task<ReportRenderedOutput> RenderAsync(string runNId, ReportContentFlow flow, JsonObject paper, bool preview, CancellationToken token, string format = "Pdf", string? workspace = null, Func<JsonObject, Task>? evidence = null, Action? confirmedStopped = null)
    {
        var options = profile.Renderer;
        if (!options.Admitted) throw new StudioException("STUDIO_RENDERER_UNAVAILABLE", "服务端分页引擎尚未准入。", 503);
        options.Validate();
        static async Task Verify(string file, string hash, CancellationToken ct)
        {
            await using var content = File.OpenRead(file);
            if (!string.Equals(Convert.ToHexStringLower(await SHA256.HashDataAsync(content, ct)), hash, StringComparison.OrdinalIgnoreCase)) throw new StudioException("RENDERER_UNAVAILABLE", "Renderer登记文件hash不匹配。", 503);
        }
        await Verify(options.Command!, options.CommandSha256!, token); await Verify(options.Script!, options.ScriptSha256!, token);
        await Verify(options.EngineManifestPath!, options.EngineManifestHash!, token);
        var directory = workspace ?? CreateWorkspace(runNId);
        if (!string.Equals(Path.GetFullPath(Path.GetDirectoryName(directory)!), Path.GetFullPath(options.WorkingRoot!), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(directory).StartsWith("report-" + StudioJson.Hash(runNId)[..24] + "-", StringComparison.Ordinal)) throw new StudioException("REPORT_OUTPUT_INVALID", "运行工作目录不匹配。", 422);
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "input.json");
        var html = ReportHtmlComposer.Compose(flow, preview);
        var parts = flow.Blocks.Select(block => block.Definition["paginationPart"]).OfType<JsonObject>().DistinctBy(part => StudioJson.RequiredString(part, "key"), StringComparer.Ordinal);
        await File.WriteAllTextAsync(input, new JsonObject { ["html"] = html, ["css"] = ReportHtmlComposer.Css, ["paper"] = paper.DeepClone(), ["parts"] = new JsonArray(parts.Select(part => part.DeepClone()).ToArray()), ["format"] = format, ["preview"] = preview, ["expectedMarkers"] = new JsonArray(flow.ExpectedMarkers.Select(m => (JsonNode)JsonValue.Create(m)!).ToArray()), ["engineManifestHash"] = options.EngineManifestHash }.ToJsonString(), token);
        using var process = new Process { StartInfo = new ProcessStartInfo(options.Command!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.ArgumentList.Add(options.Script!); process.StartInfo.ArgumentList.Add("--input"); process.StartInfo.ArgumentList.Add(input); process.StartInfo.ArgumentList.Add("--output"); process.StartInfo.ArgumentList.Add(directory);
        process.StartInfo.Environment["PF09_RENDERER_MANIFEST"] = options.EngineManifestPath!;
        var inputHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(input, token)));
        if (evidence is not null) await evidence(new JsonObject { ["directory"] = directory, ["inputSha256"] = inputHash, ["engineManifestHash"] = options.EngineManifestHash });
        if (!process.Start()) throw new StudioException("RENDERER_UNAVAILABLE", "无法启动Renderer进程。", 503);
        // Drain without retaining arbitrary engine output (which may include source data).
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None); var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try { await process.WaitForExitAsync(deadline.Token); await Task.WhenAll(stdout, stderr); }
        catch (OperationCanceledException)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "cancel.request"), "cancel", CancellationToken.None);
            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stopped = false;
            try
            {
                await process.WaitForExitAsync(grace.Token); await Task.WhenAll(stdout, stderr);
                _ = await ReadStoppedReceiptAsync(directory, options.EngineManifestHash!, inputHash, process.Id);
                // The engine may finish normally while cancellation is being delivered.
                // Any matching terminal receipt confirms child shutdown; the caller's cancellation still wins.
                stopped = true;
            }
            catch (OperationCanceledException) { }
            catch (ReportRendererUnconfirmedException) { }
            if (stopped)
            {
                confirmedStopped?.Invoke();
                token.ThrowIfCancellationRequested();
                throw new StudioException("REPORT_RENDER_TIMEOUT", "渲染超时且已确认引擎停止。", 504);
            }
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            // Killing the launcher alone is not machine evidence that every renderer child stopped. Preserve files/active slot for reconciliation.
            throw new ReportRendererUnconfirmedException();
        }
        var stoppedReceipt = await ReadStoppedReceiptAsync(directory, options.EngineManifestHash!, inputHash, process.Id);
        confirmedStopped?.Invoke();
        if (StudioJson.Text(stoppedReceipt["reason"]) == "Cancelled") throw new OperationCanceledException(token);
        if (StudioJson.Text(stoppedReceipt["reason"]) == "TimedOut") throw new StudioException("REPORT_RENDER_TIMEOUT", "渲染超时且已确认引擎停止。", 504);
        if (process.ExitCode != 0)
        {
            var code = StudioJson.Text(stoppedReceipt["errorCode"]);
            if (code is null || !System.Text.RegularExpressions.Regex.IsMatch(code, "^REPORT_[A-Z0-9_]{1,80}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) code = "REPORT_LAYOUT_UNSUPPORTED";
            throw new StudioException(code, "Renderer明确返回失败，未登记成功产物。", 422);
        }
        var resultPath = Path.Combine(directory, "result.json"); var pdfPath = Path.Combine(directory, "result.pdf");
        var needsPdf = !preview || format != "Html";
        if (!File.Exists(resultPath) || new FileInfo(resultPath).Length > 2 * 1024 * 1024 || needsPdf && (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length > 50 * 1024 * 1024)) throw new StudioException("REPORT_OUTPUT_INVALID", "Renderer输出缺失或超出预算。", 422);
        var result = JsonNode.Parse(await File.ReadAllTextAsync(resultPath, token))!.AsObject();
        var pages = result["pages"] is JsonArray array ? array.Count : result["pages"]?.GetValue<int>() ?? 0;
        if (pages is < 1 or > 2000 || StudioJson.Text(result["engineManifestHash"]) != options.EngineManifestHash) throw new StudioException("REPORT_OUTPUT_INVALID", "引擎版本或页数依据无效。", 422);
        if (needsPdf)
        {
            await Verify(pdfPath, StudioJson.RequiredString(result, "pdfSha256"), token);
            await using var pdf = File.OpenRead(pdfPath); var signature = new byte[5]; if (await pdf.ReadAsync(signature, token) != 5 || Encoding.ASCII.GetString(signature) != "%PDF-") throw new StudioException("REPORT_OUTPUT_INVALID", "Renderer没有输出PDF。", 422);
        }
        var ledger = result["markerLedger"] is JsonArray explicitLedger ? explicitLedger.OfType<JsonObject>().ToArray() : (result["pages"] as JsonArray ?? []).OfType<JsonObject>().SelectMany(page => (page["markers"] as JsonArray ?? []).OfType<JsonObject>()).ToArray();
        var expected = flow.ExpectedMarkers.ToHashSet(StringComparer.Ordinal);
        if (ledger.Length == 0 && expected.Count > 0 || ledger.Any(l => !expected.Contains(StudioJson.Text(l["marker"]) ?? "")) || !expected.SetEquals(ledger.Select(l => StudioJson.RequiredString(l, "marker")))) throw new StudioException("REPORT_MARKER_MISMATCH", "正文标记有遗漏或未知项。", 422);
        foreach (var group in ledger.GroupBy(l => StudioJson.RequiredString(l, "marker")))
        {
            var fragments = group.Select(l => l["fragmentIndex"]?.GetValue<int>() ?? -1).Order().ToArray();
            if (!fragments.SequenceEqual(Enumerable.Range(0, fragments.Length))) throw new StudioException("REPORT_MARKER_MISMATCH", "正文片段重复或不连续。", 422);
        }
        if (result["sourceCells"] is JsonArray sourceCells && result["cellFragmentLedger"] is JsonArray cellLedger)
        {
            var lengths = new Dictionary<string, int>(StringComparer.Ordinal); var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cell in sourceCells.OfType<JsonObject>())
            {
                var id = StudioJson.RequiredString(cell, "id"); var length = cell["length"]?.GetValue<int>() ?? -1;
                if (length < 0 || StudioJson.Text(cell["unit"]) != "UTF16" || !lengths.TryAdd(id, length)) throw new StudioException("REPORT_MARKER_MISMATCH", "单元格来源范围无效。", 422);
            }
            foreach (var fragment in cellLedger.OfType<JsonObject>())
            {
                var id = StudioJson.RequiredString(fragment, "sourceCell"); var start = fragment["start"]?.GetValue<int>() ?? -1; var end = fragment["end"]?.GetValue<int>() ?? -1; var page = fragment["pageNo"]?.GetValue<int>() ?? 0;
                if (!lengths.TryGetValue(id, out var length) || page < 1 || page > pages || StudioJson.Text(fragment["unit"]) != "UTF16" || start != offsets.GetValueOrDefault(id) || end < start || end > length) throw new StudioException("REPORT_MARKER_MISMATCH", "单元格字符片段重复、越界或不连续。", 422);
                offsets[id] = end;
            }
            if (lengths.Any(pair => !offsets.TryGetValue(pair.Key, out var end) || end != pair.Value)) throw new StudioException("REPORT_MARKER_MISMATCH", "单元格字符片段没有覆盖完整正文。", 422);
        }
        if ((result["diagnostics"] as JsonArray ?? []).OfType<JsonObject>().Any(diagnostic => StudioJson.Text(diagnostic["severity"]) == "Error")) throw new StudioException("REPORT_LAYOUT_UNSUPPORTED", "分页诊断包含错误，不能确认产物。", 422);
        var registeredBytes = await File.ReadAllBytesAsync(options.EngineManifestPath!, token);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(registeredBytes)), options.EngineManifestHash, StringComparison.OrdinalIgnoreCase)) throw new StudioException("RENDERER_UNAVAILABLE", "归档前引擎清单已变化。", 503);
        var registered = JsonNode.Parse(registeredBytes)!.AsObject();
        var engineEvidence = new JsonObject { ["manifestHash"] = options.EngineManifestHash, ["commandSha256"] = options.CommandSha256, ["scriptSha256"] = options.ScriptSha256, ["inputSha256"] = inputHash, ["normalizationVersion"] = "pf09-inert-v1", ["pages"] = result["pages"]?.DeepClone(), ["sourceCells"] = result["sourceCells"]?.DeepClone(), ["cellFragmentLedger"] = result["cellFragmentLedger"]?.DeepClone(), ["diagnostics"] = result["diagnostics"]?.DeepClone() };
        foreach (var key in new[] { "bundleSha256", "browserSha256", "rowFragmentSha256", "layoutSha256" }) engineEvidence[key] = registered[key]?.DeepClone();
        return new(directory, pdfPath, pages, result, engineEvidence);
    }

    private static async Task<JsonObject> ReadStoppedReceiptAsync(string directory, string engineManifestHash, string inputHash, int processId)
    {
        try
        {
            var path = Path.Combine(directory, "renderer-stopped.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) throw new ReportRendererUnconfirmedException();
            var receipt = JsonNode.Parse(await File.ReadAllTextAsync(path, CancellationToken.None)) as JsonObject;
            if (receipt is null || StudioJson.Text(receipt["engineManifestHash"]) != engineManifestHash || StudioJson.Text(receipt["inputSha256"]) != inputHash || receipt["processId"]?.GetValue<int>() != processId || StudioJson.Text(receipt["reason"]) is not ("Completed" or "Cancelled" or "TimedOut" or "Failed")) throw new ReportRendererUnconfirmedException();
            return receipt;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            throw new ReportRendererUnconfirmedException();
        }
    }
}
