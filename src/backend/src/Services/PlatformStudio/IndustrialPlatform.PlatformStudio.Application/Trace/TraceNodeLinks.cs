using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Trace;

public sealed record TraceLinkInput(JsonObject Link, JsonNode? Value, JsonObject Parameters, string? Version);
public sealed record TraceNodeLinkResult(string Kind, string ModalUrl, DateTimeOffset ExpiresOn, bool Embeddable, string? Title = null, string? NewTabUrl = null);
public sealed record TraceLinkContent(Stream Stream, string ContentType);

/// <summary>Inputs are evaluated from the authorized node detail, never from browser file identifiers.</summary>
public interface ITraceNodeLinks
{
    Task<TraceNodeLinkResult> ResolveAsync(StudioPrincipal principal, string resourceNId, JsonObject link, JsonNode? value, JsonObject parameters, string? version, JsonObject context, CancellationToken cancellationToken);
    JsonObject ReadTicketContext(StudioPrincipal principal, string resourceNId, string ticket);
    Task<TraceLinkContent> OpenAsync(StudioPrincipal principal, string resourceNId, string ticket, TraceLinkInput current, CancellationToken cancellationToken);
}
