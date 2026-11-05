using IndustrialPlatform.PlatformStudio.Application.Trace;
using IndustrialPlatform.PlatformStudio.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IndustrialPlatform.PlatformStudio.Api;

internal static class StudioTraceLinkEndpoints
{
    public static void MapStudioTraceLinkContent(this RouteGroupBuilder group)
    {
        group.MapGet("/trace/link-sources", (HttpContext http, StudioDeploymentProfile profile) =>
        {
            var principal = StudioEndpoints.Principal(http);
            if (!principal.HasPermission("studio.trace.design")) throw new StudioException("STUDIO_FORBIDDEN", "没有追溯设计权限。", 403);
            return new
            {
                ownedAvailable = profile.Files.Provider != "Disabled",
                external = profile.TraceExternalLinks.Select(x => new { key = x.Key, x.Value.Embeddable, x.Value.ValueParameter, x.Value.AllowedParameters }).ToArray(),
                mes = profile.MesFileSources.Where(x => principal.HasPermission(x.Value.PermissionAction)).Select(x => new { key = x.Key, x.Value.ScanPolicy, x.Value.AllowedContentTypes, maxBytes = x.Value.MaxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), requiresFiles = x.Value.ScanPolicy == "ImportAndScan", available = x.Value.ScanPolicy != "ImportAndScan" || profile.Files.Provider != "Disabled" }).ToArray()
            };
        });
        group.MapGet("/trace/{nId}/link-content", async (string nId, string ticket, HttpContext http, ITraceNodeLinks links, TraceRuntime trace, CancellationToken ct) =>
        {
            var principal = StudioEndpoints.Principal(http);
            var context = links.ReadTicketContext(principal, nId, ticket);
            // Re-resolve the signed node against the current detail Dataset and factory permissions on every read.
            var current = await trace.ResolveNodeLinkInputAsync(principal, nId, context, ct);
            var content = await links.OpenAsync(principal, nId, ticket, current, ct);
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; sandbox";
            return Results.Stream(content.Stream, content.ContentType, enableRangeProcessing: false);
        });
    }
}
