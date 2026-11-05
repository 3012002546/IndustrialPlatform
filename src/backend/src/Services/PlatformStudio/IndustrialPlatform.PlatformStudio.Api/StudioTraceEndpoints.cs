using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Trace;
using IndustrialPlatform.PlatformStudio.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IndustrialPlatform.PlatformStudio.Api;

internal static class StudioTraceEndpoints
{
    public static void MapStudioTrace(this RouteGroupBuilder group)
    {
        static StudioPrincipal Principal(HttpContext http) => (StudioPrincipal)http.Items["StudioPrincipal"]!;
        group.MapGet("/trace-models/runnable", (HttpContext http, TraceRuntime runtime, CancellationToken ct) => runtime.ModelsAsync(Principal(http), ct));
        group.MapGet("/trace/{nId}/contract", (string nId, string? revisionNId, HttpContext http, TraceRuntime runtime, CancellationToken ct) => runtime.ContractAsync(Principal(http), nId, revisionNId, ct));
        group.MapPost("/trace-models/preview-start", (JsonObject input, HttpContext http, TraceRuntime runtime, CancellationToken ct) => runtime.PreviewStartAsync(Principal(http), input, ct));
        group.MapPost("/trace-models/preview-relation", (JsonObject input, HttpContext http, TraceRuntime runtime, CancellationToken ct) => runtime.PreviewRelationAsync(Principal(http), input, ct));
        group.MapPost("/trace-models/preview-options", (JsonObject input, HttpContext http, TraceRuntime runtime, CancellationToken ct) => runtime.OptionsAsync(Principal(http), null, input, ct));
        group.MapPost("/trace/{nId}/starts", (string nId, JsonObject input, HttpContext http, TraceRuntime runtime, CancellationToken ct) => runtime.StartsAsync(Principal(http), nId, input, ct));
        group.MapPost("/trace/{nId}/search-options", (string nId, JsonObject input, HttpContext http, TraceRuntime runtime, CancellationToken ct) => runtime.OptionsAsync(Principal(http), nId, input, ct));
        group.MapPost("/trace/{nId}/query", (string nId, JsonObject input, HttpContext http, TraceRuntime runtime, CancellationToken ct) => runtime.QueryAsync(Principal(http), nId, input, ct));
        group.MapPost("/trace/{nId}/expand", (string nId, JsonObject input, HttpContext http, TraceRuntime runtime, CancellationToken ct) => runtime.ExpandAsync(Principal(http), nId, input, ct));
        group.MapPost("/trace/{nId}/node-detail", (string nId, JsonObject input, HttpContext http, TraceRuntime runtime, CancellationToken ct) => runtime.NodeDetailAsync(Principal(http), nId, input, ct));
        group.MapPost("/trace/{nId}/node-section", (string nId, JsonObject input, HttpContext http, TraceRuntime runtime, CancellationToken ct) => runtime.NodeSectionAsync(Principal(http), nId, input, ct));
        group.MapPost("/trace/{nId}/node-link", (string nId, JsonObject input, HttpContext http, TraceRuntime runtime, CancellationToken ct) => runtime.NodeLinkAsync(Principal(http), nId, input, ct));
    }
}
