using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Contracts.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>通过受信服务断言写入 SystemData 审计事实。</summary>
public sealed class HttpSystemDataAuditPort : ICollaborationAuditPort
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> ContractPayloadFields = new(StringComparer.Ordinal)
    {
        "schemaVersion", "requestNId", "result", "conversationNId", "messageNId", "attachmentNId", "caseNId", "operationId", "scopeChecksum", "stateVersion", "count", "errorCode",
    };
    private readonly IHttpClientFactory _clients;
    private readonly ITrustedServiceCallSigner _signer;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpSystemDataAuditPort(IHttpClientFactory clients, ITrustedServiceCallSigner signer, IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _clients = clients;
        _signer = signer;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task WriteAsync(
        string tenantNId,
        TrustedCollaborationCall? serviceCall,
        string actorUserNId,
        string action,
        string objectType,
        string objectNId,
        object payload,
        CancellationToken cancellationToken)
    {
        var auditAction = action.StartsWith("collaboration.", StringComparison.Ordinal)
            ? action
            : $"collaboration.{action}";
        var payloadJson = BuildContractPayload(auditAction, payload);
        if (Encoding.UTF8.GetByteCount(payloadJson) > 8192)
            throw new CollaborationException(400, "COLLAB_AUDIT_PAYLOAD_TOO_LARGE", "审计载荷超过允许大小。");

        var eventSeed = $"{tenantNId}\n{actorUserNId}\n{auditAction}\n{objectType}\n{objectNId}\n{payloadJson}";
        var eventNId = "AUD-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventSeed))).ToLowerInvariant();
        var request = new AuditFactIngestRequest
        {
            ProducerServiceKey = "collaboration",
            AuditEventNId = eventNId,
            OccurredOn = DateTimeOffset.UtcNow,
            ActorUserNId = actorUserNId,
            Action = auditAction,
            ObjectType = objectType,
            ObjectNId = objectNId,
            PayloadJson = payloadJson,
            Severity = "Info",
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(request);
        const string path = "/internal/pf05/audits/facts:ingest";
        var baseUrl = _configuration["Collaboration:SystemData:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("https" or "http"))
            throw new CollaborationException(503, "COLLAB_SYSTEMDATA_AUDIT_UNAVAILABLE", "SystemData 服务内部地址未配置。");

        if (serviceCall is not null
            && (!string.Equals(serviceCall.TenantNId, tenantNId, StringComparison.Ordinal)
                || !string.Equals(serviceCall.ActorUserNId, actorUserNId, StringComparison.Ordinal)))
            throw new CollaborationException(403, "COLLAB_AUDIT_SCOPE_INVALID", "审计调用主体与受信上下文不一致。");
        var actorContext = serviceCall is null
            ? CollaborationHttpActorContext.Require(_httpContextAccessor, _configuration, tenantNId, actorUserNId)
            : (SessionNId: serviceCall.ActorSessionNId, SecurityVersion: serviceCall.ActorSecurityVersion);
        var actorSessionNId = actorContext.SessionNId;
        var actorSecurityVersion = actorContext.SecurityVersion;
        // The SystemData endpoint binds the trusted request id to AuditEventNId.
        // The caller's service-call id is context, not the audit fact id.
        var requestNId = eventNId;
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, path))
        {
            Content = new ByteArrayContent(body),
        };
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var assertion = _signer.Sign(
            HttpMethod.Post,
            path,
            body,
            CollaborationServiceConstants.InternalSystemDataAudience,
            tenantNId,
            actorUserNId,
            actorSessionNId,
            actorSecurityVersion,
            "audit.ingest",
            requestNId);
        message.Headers.TryAddWithoutValidation(TrustedServiceCallValidator.HeaderName, assertion);

        HttpResponseMessage response;
        try
        {
            response = await _clients.CreateClient("Collaboration.SystemData").SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new CollaborationException(503, "COLLAB_SYSTEMDATA_AUDIT_UNAVAILABLE", "SystemData 审计服务不可用。");
        }
        using (response)
        {
        if (!response.IsSuccessStatusCode)
            throw new CollaborationException((int)response.StatusCode, "COLLAB_SYSTEMDATA_AUDIT_UNAVAILABLE", "SystemData 审计调用失败。");
        var result = await response.Content.ReadFromJsonAsync<InternalApiResult<AuditFactV1>>(JsonOptions, cancellationToken);
        if (result is null || !result.Success || result.Data is null)
            throw new CollaborationException(503, "COLLAB_SYSTEMDATA_AUDIT_UNAVAILABLE", "SystemData 未返回有效审计结果。");
        }
    }

    private sealed record InternalApiResult<T>(bool Success, string? Code, string? Message, T? Data);

    private static string BuildContractPayload(string action, object payload)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 1,
            ["result"] = action.EndsWith(".failed", StringComparison.Ordinal) || action.EndsWith(".rejected", StringComparison.Ordinal)
                ? "Failed"
                : action.EndsWith(".approval-expired", StringComparison.Ordinal)
                    ? "Denied"
                    : action.EndsWith(".start", StringComparison.Ordinal) || action.EndsWith(".prepare", StringComparison.Ordinal)
                        ? "Requested"
                        : "Succeeded",
        };
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (ContractPayloadFields.Contains(property.Name) && property.Name is not ("schemaVersion" or "result"))
                    result[property.Name] = property.Value.Clone();
            }
        }

        return JsonSerializer.Serialize(result);
    }
}
