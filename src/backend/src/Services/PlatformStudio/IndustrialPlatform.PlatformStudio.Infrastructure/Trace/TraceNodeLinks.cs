using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Trace;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.SystemData.Application.Files;
using Microsoft.AspNetCore.DataProtection;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Trace;

public sealed class TraceNodeLinks(StudioDeploymentProfile profile, IStudioOwnedFiles assets, StudioMesFiles mes,
    IDataProtectionProvider protection, TimeProvider clock, IFileService? files = null, IOwnedFileService? owned = null, TraceLinkTickets? tickets = null) : ITraceNodeLinks
{
    private readonly IDataProtector _tickets = protection.CreateProtector("Studio.Trace.NodeLink.v1");
    private readonly TraceLinkTickets _contexts = tickets ?? new(clock);
    public async Task<TraceNodeLinkResult> ResolveAsync(StudioPrincipal principal, string resourceNId, JsonObject link, JsonNode? value, JsonObject parameters, string? version, JsonObject context, CancellationToken cancellationToken)
    {
        RequirePrincipal(principal);
        if (StudioJson.Text(link["openMode"]) != "Modal") throw Invalid();
        var id = Scalar(value);
        if (StudioJson.Text(link["kind"]) == "External") return External(principal, link, id, parameters);
        if (StudioJson.Text(link["kind"]) != "File") throw Invalid();
        string? reference = null; string? fileNId = null; string? ownerNId = null; string? contentHash = null; string contentType;
        StudioMesFileMetadata? metadata = null;
        var source = link["fileSource"] as JsonObject;
        var mode = StudioJson.Text(source?["mode"]) ?? "Owned";
        if (mode == "Owned")
        {
            var status = await assets.GetStatusAsync(principal, resourceNId, id, cancellationToken);
            if (status.Status != "Active" || status.FileNId is null) throw new StudioException("STUDIO_FILE_SCAN_UNCONFIRMED", "文件引用尚未通过扫描或不可用。", 409);
            _ = await assets.GetReadTicketAsync(principal, resourceNId, id, cancellationToken);
            var file = files is null ? null : await files.GetFileAsync(principal.TenantNId, status.FileNId, cancellationToken);
            if (file is null) throw new StudioException("STUDIO_FILES_UNAVAILABLE", "文件元数据能力不可用。", 503);
            contentType = SafeType(file.ContentType);
            reference = id; fileNId = status.FileNId;
        }
        else if (mode == "Mes")
        {
            var key = StudioJson.RequiredString(source!, "sourceKey");
            metadata = await mes.GetMetadataAsync(principal, key, id, version, cancellationToken);
            contentType = SafeType(metadata.ContentType);
            if (profile.MesFileSources[key].ScanPolicy == "ImportAndScan")
            {
                if (profile.Files.Provider == "Disabled" || files is null || owned is null) throw new StudioException("STUDIO_FILES_UNAVAILABLE", "MES导入扫描需要启用文件与扫描能力。", 503);
                await using var bytes = await mes.OpenAsync(principal, key, metadata, cancellationToken);
                contentHash = Convert.ToHexStringLower(SHA256.HashData(bytes.GetBuffer().AsSpan(0, (int)bytes.Length)));
                // A retry of the same server-issued node context resumes its upload/scan instead of creating another preview.
                ownerNId = "trace-detail-" + StudioJson.Hash(JsonSerializer.Serialize(new { principal.TenantNId, principal.SubjectNId, principal.AuthVersion, resourceNId, context, input = InputHash(new(link, value, parameters, version)), metadata }));
                var imported = await OwnedFileUploads.CommitAsync(owned, files, Trusted(principal), new(ownerNId, "platform-studio", "DetailPreview", ownerNId, metadata.Name, metadata.ContentType, metadata.Length, contentHash), bytes, cancellationToken);
                reference = imported.Prepared.ReferenceNId; fileNId = imported.File.FileNId;
                if (imported.Reference.Status != "Active") throw new StudioException("STUDIO_FILE_SCAN_UNCONFIRMED", "文件扫描尚未确认Clean，请稍后重新打开；内容仍被隔离。", 409);
            }
        }
        else throw Invalid();
        // The standalone preview can refresh within this bounded window. Every read still checks
        // the current principal, node, revision, file mapping and scan/permission evidence.
        var expires = clock.GetUtcNow().AddMinutes(5);
        if (expires > principal.ExpiresOn) expires = principal.ExpiresOn;
        var claim = new LinkClaim(principal.TenantNId, principal.SubjectNId, principal.IssuerKey, principal.AuthVersion, principal.PolicyVersion, resourceNId,
            InputHash(new(link, value, parameters, version)), context.DeepClone().AsObject(), expires, mode, reference, fileNId, ownerNId, contentType, metadata, contentHash);
        var ticket = _tickets.Protect(_contexts.Add(JsonSerializer.Serialize(claim), expires));
        var title = StudioJson.Text(link["label"]);
        var preview = profile.PathBase + "/screen/trace-link?nId=" + Uri.EscapeDataString(resourceNId)
            + "&ticket=" + Uri.EscapeDataString(ticket) + "&expiresOn=" + Uri.EscapeDataString(expires.ToString("O"))
            + "&title=" + Uri.EscapeDataString(title ?? "");
        return new("File", profile.PathBase + "/api/v1/studio/trace/" + Uri.EscapeDataString(resourceNId) + "/link-content?ticket=" + Uri.EscapeDataString(ticket), expires, true, title, preview);
    }

    public JsonObject ReadTicketContext(StudioPrincipal principal, string resourceNId, string ticket) => ReadClaim(principal, resourceNId, ticket).Context.DeepClone().AsObject();

    public async Task<TraceLinkContent> OpenAsync(StudioPrincipal principal, string resourceNId, string ticket, TraceLinkInput current, CancellationToken cancellationToken)
    {
        var claim = ReadClaim(principal, resourceNId, ticket);
        if (claim.InputHash != InputHash(current)) throw new StudioException("STUDIO_TRACE_LINK_CHANGED", "节点附件映射已改变，请重新打开。", 409);
        if (claim.Mode == "Owned")
        {
            var status = await assets.GetStatusAsync(principal, resourceNId, claim.Reference!, cancellationToken);
            if (status.FileNId != claim.FileNId) throw Invalid();
            var read = await assets.GetReadTicketAsync(principal, resourceNId, claim.Reference!, cancellationToken);
            return new(await assets.OpenReadAsync(principal, resourceNId, new(claim.Reference!, principal.SubjectNId, read.AuthorizationTicket), cancellationToken), claim.ContentType);
        }
        var key = StudioJson.RequiredString(current.Link["fileSource"]!.AsObject(), "sourceKey");
        if (claim.Reference is null && (!profile.MesFileSources.TryGetValue(key, out var currentSource) || currentSource.ScanPolicy != "VerifySourceEvidence")) throw new StudioException("STUDIO_MES_FILE_CHANGED", "文件扫描策略已改变，请重新打开。", 409);
        var metadata = await mes.GetMetadataAsync(principal, key, claim.Metadata!.ExternalFileId, claim.Metadata.Version, cancellationToken);
        if (metadata != claim.Metadata) throw new StudioException("STUDIO_MES_FILE_CHANGED", "MES文件或权限依据已经改变。", 409);
        if (claim.Reference is null) return new(await mes.OpenAsync(principal, key, metadata, cancellationToken), claim.ContentType);
        if (owned is null || files is null || profile.Files.Provider == "Disabled") throw new StudioException("STUDIO_FILES_UNAVAILABLE", "文件能力不可用。", 503);
        var references = await owned.ListByOwnerAsync(Trusted(principal), "DetailPreview", claim.OwnerNId!, null, cancellationToken);
        if (!references.Items.Any(x => x.ReferenceNId == claim.Reference && x.ExpiresOn > clock.GetUtcNow())) throw Invalid();
        var statusAfterScan = await owned.CommitAsync(Trusted(principal), new(claim.Reference, claim.FileNId!, claim.ContentHash!), cancellationToken);
        if (statusAfterScan.Status != "Active") throw new StudioException("STUDIO_FILE_SCAN_UNCONFIRMED", "文件扫描尚未确认Clean，请稍后重试；内容仍被隔离。", 409);
        var readTicket = await owned.GetReadTicketAsync(Trusted(principal), claim.Reference, cancellationToken);
        return new(await owned.ReadAsync(Trusted(principal), new(claim.Reference, principal.SubjectNId, readTicket.AuthorizationTicket), cancellationToken), claim.ContentType);
    }

    private TraceNodeLinkResult External(StudioPrincipal principal, JsonObject link, string value, JsonObject parameters)
    {
        var key = StudioJson.RequiredString(link, "allowedOriginKey");
        if (!profile.TraceExternalLinks.TryGetValue(key, out var target) || !Uri.TryCreate(target.BaseAddress, UriKind.Absolute, out var url) || url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment)) throw new StudioException("STUDIO_TRACE_ORIGIN_UNAVAILABLE", "外链目标未登记。", 422);
        if (parameters.Any(x => !target.AllowedParameters.Contains(x.Key, StringComparer.Ordinal)) || parameters.ContainsKey(target.ValueParameter)) throw Invalid();
        var query = new List<string> { Uri.EscapeDataString(target.ValueParameter) + "=" + Uri.EscapeDataString(value) };
        query.AddRange(parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(Scalar(x.Value))));
        // The original origin is embedded directly; target CSP/X-Frame-Options remains authoritative.
        var expires = clock.GetUtcNow().AddMinutes(5);
        if (expires > principal.ExpiresOn) expires = principal.ExpiresOn;
        return new("External", url.AbsoluteUri + "?" + string.Join("&", query), expires, target.Embeddable, StudioJson.Text(link["label"]));
    }
    private LinkClaim ReadClaim(StudioPrincipal principal, string resourceNId, string ticket)
    {
        RequirePrincipal(principal);
        LinkClaim? claim;
        try { claim = JsonSerializer.Deserialize<LinkClaim>(_contexts.Read(_tickets.Unprotect(ticket))); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException) { throw Invalid(); }
        if (claim is null || claim.Tenant != principal.TenantNId || claim.Subject != principal.SubjectNId || claim.Issuer != principal.IssuerKey || claim.AuthVersion != principal.AuthVersion || claim.PolicyVersion != principal.PolicyVersion || claim.Resource != resourceNId || claim.ExpiresOn <= clock.GetUtcNow()) throw Invalid();
        return claim;
    }
    private void RequirePrincipal(StudioPrincipal principal) { if (principal.ExpiresOn <= clock.GetUtcNow() || !principal.HasPermission("studio.trace.run")) throw new StudioException("STUDIO_FORBIDDEN", "当前追溯身份无效。", 403); }
    private static string InputHash(TraceLinkInput input) => StudioJson.Hash(JsonSerializer.Serialize(input));
    private static TrustedServiceContext Trusted(StudioPrincipal principal) => new(principal.TenantNId, "platform-studio", principal.SubjectNId);
    private static string Scalar(JsonNode? node)
    {
        if (node is not JsonValue value) throw Invalid();
        var text = StudioJson.Text(value) ?? value.ToJsonString();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096 || text.Any(char.IsControl)) throw Invalid();
        return text;
    }
    private static string SafeType(string type) => type.ToLowerInvariant() switch
    {
        "application/pdf" => "application/pdf", "image/png" => "image/png", "image/jpeg" => "image/jpeg", "image/gif" => "image/gif", "image/webp" => "image/webp",
        // HTML/SVG are inert source text. Never execute active content on the host origin.
        "text/html" or "image/svg+xml" or "text/plain" or "text/csv" or "application/json" => "text/plain; charset=utf-8",
        _ => throw new StudioException("STUDIO_FILE_PREVIEW_UNSUPPORTED", "此文件格式不支持安全的当前页预览。", 422)
    };
    private static StudioException Invalid() => new("STUDIO_TRACE_LINK_INVALID", "链接配置、内容绑定或短期读取凭证无效，请重新打开。", 403);
    private sealed record LinkClaim(string Tenant, string Subject, string Issuer, string AuthVersion, string PolicyVersion, string Resource, string InputHash, JsonObject Context, DateTimeOffset ExpiresOn, string Mode, string? Reference, string? FileNId, string? OwnerNId, string ContentType, StudioMesFileMetadata? Metadata, string? ContentHash);
}
