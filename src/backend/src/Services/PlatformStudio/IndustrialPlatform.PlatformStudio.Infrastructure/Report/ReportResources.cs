using System.Security.Cryptography;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Application.Report;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.SystemData.Application.Files;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

/// <summary>Personal uploads are copied into resource-owned immutable assets. Rendering only reads authorized, hash-pinned raster content.</summary>
public sealed class ReportResources(IStudioIdentity identity, StudioPublishing publishing, IStudioFiles personal,
    IStudioOwnedFiles assets, IFileService files, IOwnedFileService owned)
{
    private const int MaxImageBytes = 5 * 1024 * 1024;
    public async Task<JsonObject> ImportAsync(StudioPrincipal principal, string resourceNId, string fileNId, string referenceNId, string key, CancellationToken token)
    {
        principal = await identity.RefreshAsync(principal, token); StudioCommands.ValidateKey(key);
        var resource = await publishing.RequireResourceAsync(principal, resourceNId, "design", "Design", token);
        if (resource.Kind != "Report") throw new StudioException("TYPE_MISMATCH", "图片资产必须归属单报表。", 422);
        await personal.ValidateReferenceAsync(principal, fileNId, referenceNId, token);
        var file = await files.GetFileAsync(principal.TenantNId, fileNId, token) ?? throw new StudioException("STUDIO_NOT_FOUND", "图片不存在。", 404);
        await using var input = await files.OpenFileContentAsync(principal.TenantNId, principal.SubjectNId, fileNId, referenceNId, token);
        using var content = await ReadAsync(input, token);
        var type = RasterType(content.GetBuffer().AsSpan(0, (int)content.Length));
        var hash = Convert.ToHexStringLower(SHA256.HashData(content.GetBuffer().AsSpan(0, (int)content.Length)));
        if (hash != file.Sha256) throw new StudioException("REPORT_RESOURCE_INVALID", "图片内容哈希不匹配。", 422);
        var context = new TrustedServiceContext(principal.TenantNId, "platform-studio", principal.SubjectNId);
        var result = await OwnedFileUploads.CommitAsync(owned, files, context, new("asset-" + StudioJson.Hash(resourceNId + "/" + key), "platform-studio", "StudioAsset", resourceNId, file.FileName, type, content.Length, hash), content, token);
        if (result.Reference.Status != "Active") throw new StudioException("REPORT_RESOURCE_PENDING", "图片扫描尚未确认，请保留当前上传并重试。", 409);
        return new() { ["id"] = "image-" + hash[..24], ["fileNId"] = result.File.FileNId, ["referenceNId"] = result.Prepared.ReferenceNId, ["sha256"] = hash, ["mediaType"] = type };
    }
    public async Task<JsonArray> FreezeAsync(ReportContentFlow flow, string identityEvidence, CancellationToken token)
    {
        var cache = new Dictionary<string, string>(StringComparer.Ordinal); var manifest = new JsonArray(); long total = 0;
        async Task<string> Freeze(JsonObject asset, string owner)
        {
            var reference = StudioJson.RequiredString(asset, "referenceNId");
            var hash = StudioJson.RequiredString(asset, "sha256"); var key = owner + "/" + reference + "/" + hash;
            if (!cache.TryGetValue(key, out var data))
            {
                var principal = await identity.RefreshExecutionAsync(identityEvidence, token);
                var status = await owned.GetStatusAsync(new(principal.TenantNId, "platform-studio", principal.SubjectNId), reference, token);
                if (status.Status != "Active" || status.FileNId != StudioJson.RequiredString(asset, "fileNId")) throw new StudioException("REPORT_RESOURCE_UNAVAILABLE", "图片引用或文件版本不匹配。", 422);
                await using var source = await assets.OpenReadForExecutionAsync(identityEvidence, owner, reference, token);
                using var content = await ReadAsync(source, token);
                total += content.Length; if (total > 20 * 1024 * 1024) throw new StudioException("STUDIO_LIMIT", "图片资产总量超过20MiB。", 429);
                var bytes = content.GetBuffer().AsSpan(0, (int)content.Length).ToArray(); var type = RasterType(bytes);
                if (type != StudioJson.RequiredString(asset, "mediaType") || Convert.ToHexStringLower(SHA256.HashData(bytes)) != hash) throw new StudioException("REPORT_RESOURCE_INVALID", "图片类型或哈希不匹配。", 422);
                data = "data:" + type + ";base64," + Convert.ToBase64String(bytes); cache[key] = data;
                manifest.Add(new JsonObject { ["kind"] = "ImageAsset", ["resourceNId"] = owner, ["referenceNId"] = reference, ["sha256"] = hash });
            }
            return data;
        }
        foreach (var block in flow.Blocks.Where(b => b.Type == "Image")) block.Definition["frozenImageData"] = await Freeze(block.Definition["frozenAsset"]!.AsObject(), StudioJson.RequiredString(block.Definition, "assetResourceNId"));
        foreach (var block in flow.Blocks.Where(b => b.Type == "Html" && b.Value is JsonObject))
        {
            var record = block.Value!.AsObject(); var resources = new JsonObject();
            foreach (var asset in (record["resourceRefs"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var source = StudioJson.RequiredString(asset, "src", 2048); if (resources.ContainsKey(source) || source.Length > 2048) throw new StudioException("REPORT_RESOURCE_INVALID", "HTML资源标识重复或超限。", 422);
                resources[source] = await Freeze(asset, StudioJson.RequiredString(block.Definition, "assetResourceNId"));
            }
            block.Definition["frozenHtmlResources"] = resources;
            manifest.Add(new JsonObject { ["kind"] = "HtmlRecord", ["sourceRecordKey"] = record["sourceRecordKey"]?.DeepClone(), ["sourceVersion"] = record["sourceVersion"]?.DeepClone(), ["recordedOn"] = record["recordedOn"]?.DeepClone(), ["sourceSha256"] = StudioJson.Hash(StudioJson.RequiredString(record, "html", 8 * 1024 * 1024)), ["normalizationVersion"] = "pf09-inert-v1" });
        }
        return manifest;
    }
    private static async Task<MemoryStream> ReadAsync(Stream source, CancellationToken token)
    {
        var output = new MemoryStream(); var buffer = new byte[65536];
        try { int count; while ((count = await source.ReadAsync(buffer, token)) > 0) { if (output.Length + count > MaxImageBytes) throw new StudioException("STUDIO_LIMIT", "单张图片超过5MiB。", 429); await output.WriteAsync(buffer.AsMemory(0, count), token); } output.Position = 0; return output; }
        catch { output.Dispose(); throw; }
    }
    private static string RasterType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "image/jpeg";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return "image/gif";
        if (bytes.Length > 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        throw new StudioException("REPORT_RESOURCE_INVALID", "仅支持PNG、JPEG、GIF和WebP静态图片资产。", 422);
    }
}
