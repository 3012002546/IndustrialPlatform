using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Tests;

internal sealed class StudioTestSourceExecutor : IStudioSourceExecutor
{
    public int Calls { get; private set; }
    public JsonNode? Data { get; set; } = new JsonArray();
    public JsonObject? Parameters { get; private set; }
    public string ConfigurationHash(StudioSourceEndpoint endpoint) => SourceAdministration.EndpointHash(endpoint);
    public Task<JsonObject> TestAsync(StudioSource source, CancellationToken cancellationToken) => Task.FromResult(new JsonObject { ["status"] = "Ready" });
    public Task<StudioSourceResult> ExecuteAsync(StudioPrincipal principal, StudioSource source, JsonObject document, JsonObject parameters, bool preview, CancellationToken cancellationToken)
    {
        Calls++; Parameters = parameters.DeepClone().AsObject();
        return Task.FromResult(new StudioSourceResult(Data?.DeepClone(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Data is JsonArray array ? array.Count : 1, 1));
    }
}
