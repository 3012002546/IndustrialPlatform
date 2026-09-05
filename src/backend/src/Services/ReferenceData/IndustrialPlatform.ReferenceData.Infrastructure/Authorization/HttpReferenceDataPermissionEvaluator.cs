using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Authorization;

public sealed class HttpReferenceDataPermissionEvaluator(IHttpClientFactory clients, IConfiguration configuration) : IReferenceDataPermissionEvaluator
{
    public async Task<ReferenceDataPermissionDecision> EvaluateAsync(ReferenceDataPermissionRequest request, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["ReferenceData:Identity:BaseUrl"] ?? configuration["Identity:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address) || string.IsNullOrWhiteSpace(request.AccessToken)) return Unavailable();
        using var client = clients.CreateClient("ReferenceData.Identity");
        client.BaseAddress = address;
        client.Timeout = TimeSpan.FromSeconds(5);
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/authorization/evaluate")
        {
            Content = JsonContent.Create(new { permissionNId = request.PermissionNId }),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.AccessToken);
        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return new(false, ReferenceDataPermissionDenialReason.SessionInvalid);
            if (response.StatusCode == HttpStatusCode.Forbidden) return new(false, ReferenceDataPermissionDenialReason.MissingPermission);
            if (!response.IsSuccessStatusCode) return Unavailable();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("allowed", out var allowed) || allowed.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return Unavailable();
            if (allowed.GetBoolean()) return new(true, ReferenceDataPermissionDenialReason.None);
            return data.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
                && Enum.TryParse<ReferenceDataPermissionDenialReason>(reason.GetString(), out var parsed)
                ? new(false, parsed) : Unavailable();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Unavailable();
        }
    }

    private static ReferenceDataPermissionDecision Unavailable() => new(false, ReferenceDataPermissionDenialReason.SecurityStoreUnavailable);
}
