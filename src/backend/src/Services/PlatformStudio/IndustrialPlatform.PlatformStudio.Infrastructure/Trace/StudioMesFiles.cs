using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Trace;

public sealed record StudioMesFileMetadata(string ExternalFileId, string? Version, string Name, string ContentType, long Length, string? Sha256);

/// <summary>MES read bridge. Both endpoints and credentials are deployment registered; redirects are disabled by DI.</summary>
public sealed class StudioMesFiles(StudioDeploymentProfile profile, IConfiguration configuration, HttpClient http)
{
    public async Task<StudioMesFileMetadata> GetMetadataAsync(StudioPrincipal principal, string sourceKey, string fileId, string? version, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(profile.DefaultQueryTimeoutSeconds, 1, 120)));
        token = timeout.Token;
        var source = Source(principal, sourceKey);
        using var response = await SendAsync(principal, sourceKey, source, source.MetadataEndpointKey, source.MetadataOperationKey, fileId, version, token);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var bytes = await ReadBoundedAsync(stream, 65536, token);
        var document = JsonNode.Parse(bytes.ToArray()) as JsonObject ?? throw Invalid();
        var actualVersion = StudioJson.Text(document["version"]);
        var hash = StudioJson.Text(document["sha256"])?.ToLowerInvariant();
        var type = StudioJson.Text(document["contentType"]);
        if (StudioJson.Text(document["externalFileId"]) != fileId || document["authorized"]?.GetValue<bool>() != true || version is not null && version != actualVersion || !long.TryParse(StudioJson.Text(document["length"]), NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length < 0 || length > source.MaxBytes || hash is not null && (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) || type is null || !source.AllowedContentTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) throw Invalid();
        var evidence = document["scanEvidence"] as JsonObject;
        if (source.ScanPolicy == "VerifySourceEvidence" && (string.IsNullOrWhiteSpace(actualVersion) || hash is null || evidence is null || StudioJson.Text(evidence["scannerKey"]) != source.TrustedScannerKey || StudioJson.Text(evidence["status"]) != "Clean" || StudioJson.Text(evidence["version"]) != actualVersion || !string.Equals(StudioJson.Text(evidence["sha256"]), hash, StringComparison.OrdinalIgnoreCase) || !DateTimeOffset.TryParse(StudioJson.Text(evidence["scannedOn"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))) throw new StudioException("STUDIO_MES_SCAN_UNCONFIRMED", "MES文件缺少绑定不可变版本的可信Clean扫描证据。", 409);
        return new(fileId, actualVersion, StudioJson.Text(document["name"]) ?? "file", type, length, hash);
    }

    public async Task<MemoryStream> OpenAsync(StudioPrincipal principal, string sourceKey, StudioMesFileMetadata expected, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(profile.DefaultQueryTimeoutSeconds, 1, 120)));
        token = timeout.Token;
        var source = Source(principal, sourceKey);
        // Recheck MES authorization and scan state for every content request.
        var current = await GetMetadataAsync(principal, sourceKey, expected.ExternalFileId, expected.Version, token);
        if (current != expected) throw new StudioException("STUDIO_MES_FILE_CHANGED", "MES文件版本或扫描依据已改变，请重新打开。", 409);
        using var response = await SendAsync(principal, sourceKey, source, source.ContentEndpointKey, source.ContentOperationKey, expected.ExternalFileId, expected.Version, token);
        if (response.Content.Headers.ContentLength is { } length && length != expected.Length) throw Invalid();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        var content = await ReadBoundedAsync(input, source.MaxBytes, token);
        if (content.Length != expected.Length || expected.Sha256 is not null && Convert.ToHexStringLower(SHA256.HashData(content.GetBuffer().AsSpan(0, (int)content.Length))) != expected.Sha256)
        {
            content.Dispose(); throw new StudioException("STUDIO_MES_FILE_CHANGED", "MES文件长度或内容哈希不符。", 409);
        }
        return content;
    }

    private StudioMesFileSource Source(StudioPrincipal principal, string sourceKey)
    {
        if (!profile.MesFileSources.TryGetValue(sourceKey, out var source)) throw new StudioException("STUDIO_MES_SOURCE_UNAVAILABLE", "MES文件来源未登记。", 503);
        if (string.IsNullOrWhiteSpace(source.PermissionAction) || !principal.HasPermission(source.PermissionAction)) throw new StudioException("STUDIO_FORBIDDEN", "没有读取此MES文件来源的权限。", 403);
        if (source.ScanPolicy is not ("ImportAndScan" or "VerifySourceEvidence") || source.ScanPolicy == "VerifySourceEvidence" && string.IsNullOrWhiteSpace(source.TrustedScannerKey)) throw new StudioException("STUDIO_MES_SCAN_UNCONFIRMED", "MES文件来源未登记可信扫描策略。", 503);
        return source;
    }

    private async Task<HttpResponseMessage> SendAsync(StudioPrincipal principal, string sourceKey, StudioMesFileSource source, string endpointKey, string operationKey, string fileId, string? version, CancellationToken token)
    {
        if (!profile.SourceRegistry.TryGetValue(endpointKey, out var endpoint) || endpoint.Provider != "RegisteredApi" || !endpoint.ReadOnlyVerified || !endpoint.Operations.TryGetValue(operationKey, out var operation) || !operation.ReadOnly || operation.Method != "GET" || !Uri.TryCreate(endpoint.BaseAddress, UriKind.Absolute, out var basis) || basis.Scheme != "https" || !string.IsNullOrEmpty(basis.UserInfo) || !string.IsNullOrEmpty(basis.Query) || !string.IsNullOrEmpty(basis.Fragment) || string.IsNullOrWhiteSpace(operation.Path) || operation.Path.StartsWith('/') || operation.Path.Contains("..", StringComparison.Ordinal) || operation.Path.Contains('?') || operation.Path.Contains('#') || Uri.TryCreate(operation.Path, UriKind.Absolute, out _)) throw new StudioException("STUDIO_MES_SOURCE_UNAVAILABLE", "MES读取端点须为登记的HTTPS只读操作。", 503);
        var target = new Uri(basis, operation.Path);
        if (target.GetLeftPart(UriPartial.Authority) != basis.GetLeftPart(UriPartial.Authority)) throw Invalid();
        var query = new Dictionary<string, string?> { ["sourceKey"] = sourceKey, ["externalFileId"] = fileId, ["version"] = version, ["tenantNId"] = principal.TenantNId, ["subjectNId"] = principal.SubjectNId, ["externalTenantId"] = principal.ExternalTenantId, ["externalSubjectId"] = principal.ExternalSubjectId, ["permissionAction"] = source.PermissionAction };
        using var request = new HttpRequestMessage(HttpMethod.Get, target.AbsoluteUri + "?" + string.Join("&", query.Where(x => x.Value is not null).Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value!))));
        var credentialRef = source.CredentialRef ?? endpoint.CredentialRef;
        if (credentialRef is not null)
        {
            var secret = configuration[credentialRef];
            if (string.IsNullOrWhiteSpace(secret)) throw new StudioException("STUDIO_MES_SOURCE_UNAVAILABLE", "MES来源凭据引用无法解析。", 503);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(profile.DefaultQueryTimeoutSeconds, 1, 120)));
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) { response.Dispose(); throw new StudioException("STUDIO_MES_SOURCE_UNAVAILABLE", "MES文件来源拒绝读取或暂不可用。", 503); }
        return response;
    }

    internal static async Task<MemoryStream> ReadBoundedAsync(Stream input, long maxBytes, CancellationToken token)
    {
        var output = new MemoryStream(); var buffer = new byte[65536];
        try
        {
            int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                if (output.Length + count > maxBytes) throw new StudioException("STUDIO_LIMIT", "文件内容超过登记读取预算。", 413);
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
            output.Position = 0; return output;
        }
        catch { output.Dispose(); throw; }
    }
    private static StudioException Invalid() => new("STUDIO_MES_FILE_INVALID", "MES文件权限、版本、长度、类型或内容证明不符合登记合同。", 409);
}
