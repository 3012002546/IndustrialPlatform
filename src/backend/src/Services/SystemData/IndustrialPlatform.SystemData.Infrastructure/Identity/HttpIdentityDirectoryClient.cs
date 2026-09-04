using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Authorization;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.IdentityDirectory;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Contracts.Administration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.SystemData.Infrastructure.Identity;

/// <summary>
/// Identity 稳定 HTTP 适配器。当前仓库已存在的公开端点为
/// <c>GET /api/v1/permissions/tree</c> 和 <c>GET /api/v1/users/{id}</c>；
/// 未配置地址或端点不可用时 fail-closed，不伪造成功回执。
/// </summary>
public sealed class HttpIdentityDirectoryClient : IIdentityPermissionRegistry, IIdentityUserDirectory, ISystemDataPermissionEvaluator
{
    private static readonly Action<ILogger, Exception?> PermissionVerificationFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, nameof(PermissionVerificationFailed)),
        "Identity permission registry verification failed; fail closed.");
    private readonly IHttpClientFactory _clients;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HttpIdentityDirectoryClient> _logger;

    public HttpIdentityDirectoryClient(IHttpClientFactory clients, IConfiguration configuration, ILogger<HttpIdentityDirectoryClient> logger)
    {
        _clients = clients;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PermissionRegistrationReceipt?> VerifyAsync(PermissionManifestV1 manifest, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        if (client is null) return null;
        try
        {
            using var response = await client.GetAsync("api/v1/permissions/tree", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectPermissionNIds(document.RootElement, available);
            if (manifest.Permissions.Any(x => !available.Contains(x.PermissionNId))) return null;
            return new PermissionRegistrationReceipt(manifest.ModuleNId, manifest.ManifestVersion, manifest.Checksum, true, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            SystemDataMetrics.PermissionRegistryFailures.Add(1);
            PermissionVerificationFailed(_logger, exception);
            return null;
        }
    }

    public async Task<IdentityUserDirectoryEntryV1?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        if (client is null) throw new IdentityDirectoryUnavailableException("Identity user directory is not configured.");
        try
        {
            using var response = await client.GetAsync($"api/v1/users/{Uri.EscapeDataString(userNId)}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = FindObjectWith(document.RootElement, "userNId") ?? document.RootElement;
            return new IdentityUserDirectoryEntryV1
            {
                TenantNId = ReadString(root, "tenantNId") ?? tenantNId,
                UserNId = ReadString(root, "userNId") ?? userNId,
                LoginName = ReadString(root, "loginName") ?? string.Empty,
                Name = ReadString(root, "name") ?? string.Empty,
                Status = ReadString(root, "status") ?? string.Empty,
                AuthVersion = ReadString(root, "authVersion"),
                IsSystemAdmin = root.TryGetProperty("isSystemAdmin", out var isSystemAdmin)
                    && isSystemAdmin.ValueKind == JsonValueKind.True
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            SystemDataMetrics.IdentityFailures.Add(1);
            throw new IdentityDirectoryUnavailableException("Identity user directory is unavailable.");
        }
    }

    public async Task<SystemDataPermissionDecision> EvaluateAsync(
        SystemDataPermissionRequest request,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();
        if (client is null || string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Unavailable();
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/authorization/evaluate")
            {
                Content = JsonContent.Create(new { permissionNId = request.PermissionNId }),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.AccessToken);
            using var response = await client.SendAsync(message, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new SystemDataPermissionDecision(false, SystemDataPermissionDenialReason.SessionInvalid);
            }
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new SystemDataPermissionDecision(false, SystemDataPermissionDenialReason.MissingPermission);
            }
            if (!response.IsSuccessStatusCode)
            {
                return Unavailable();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;
            var allowed = root.TryGetProperty("allowed", out var allowedElement) && allowedElement.GetBoolean();
            var reasonText = ReadString(root, "reason");
            var reason = Enum.TryParse<SystemDataPermissionDenialReason>(reasonText, out var parsed)
                ? parsed
                : SystemDataPermissionDenialReason.SecurityStoreUnavailable;
            return new SystemDataPermissionDecision(allowed, allowed ? SystemDataPermissionDenialReason.None : reason);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            SystemDataMetrics.IdentityFailures.Add(1);
            return Unavailable();
        }
    }

    private HttpClient? CreateClient()
    {
        var baseUrl = _configuration["SystemData:Identity:BaseUrl"]
            ?? _configuration["Identity:BaseUrl"]
            ?? _configuration["InternalInitialization:ServiceUrls:Identity"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return null;
        var client = _clients.CreateClient("SystemData.Identity");
        client.BaseAddress = uri;
        return client;
    }

    private static void CollectPermissionNIds(JsonElement element, ISet<string> values)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("permissionNId") && property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } value) values.Add(value);
                CollectPermissionNIds(property.Value, values);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) CollectPermissionNIds(child, values);
        }
    }

    private static JsonElement? FindObjectWith(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out _)) return element;
            foreach (var property in element.EnumerateObject())
                if (FindObjectWith(property.Value, propertyName) is { } found) return found;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                if (FindObjectWith(child, propertyName) is { } found) return found;
        }
        return null;
    }

    private static string? ReadString(JsonElement element, string propertyName) => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static SystemDataPermissionDecision Unavailable() =>
        new(false, SystemDataPermissionDenialReason.SecurityStoreUnavailable);
}
