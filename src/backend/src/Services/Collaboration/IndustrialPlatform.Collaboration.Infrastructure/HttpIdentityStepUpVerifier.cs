using System.Net.Http.Json;
using System.Text.Json;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Identity.Contracts.Collaboration;
using IndustrialPlatform.Security;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>Consumes step-up grants through Identity's authenticated internal endpoint.</summary>
public sealed class HttpIdentityStepUpVerifier : IComplianceStepUpVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IHttpClientFactory _clients;
    private readonly ITrustedServiceCallSigner _signer;
    private readonly IConfiguration _configuration;

    public HttpIdentityStepUpVerifier(IHttpClientFactory clients, ITrustedServiceCallSigner signer, IConfiguration configuration)
    {
        _clients = clients;
        _signer = signer;
        _configuration = configuration;
    }

    public async Task EnsureValidAsync(string proof, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId, string scopeChecksum, string requestHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(proof) || proof.Length > 512)
            throw new CollaborationException(403, "COLLAB_STEP_UP_INVALID", "重新认证证明格式无效。");
        var baseUrl = _configuration["Collaboration:Identity:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("https" or "http"))
            throw new CollaborationException(503, "COLLAB_IDENTITY_STEP_UP_UNAVAILABLE", "Identity 服务内部地址未配置。");

        var body = JsonSerializer.SerializeToUtf8Bytes(new CollaborationStepUpConsumeRequest(proof, action, requestNId, scopeChecksum, requestHash));
        const string path = "/internal/pf05/identity/step-up/consume";
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, path))
        {
            Content = new ByteArrayContent(body),
        };
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var assertion = _signer.Sign(HttpMethod.Post, path, body, CollaborationServiceConstants.InternalIdentityAudience, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, "step-up.consume", requestNId);
        message.Headers.TryAddWithoutValidation(TrustedServiceCallValidator.HeaderName, assertion);
        using var response = await _clients.CreateClient("Collaboration.Identity").SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new CollaborationException((int)response.StatusCode, "COLLAB_STEP_UP_INVALID", "重新认证证明无效、已过期或已使用。");
        _ = await response.Content.ReadFromJsonAsync<InternalApiResult<CollaborationStepUpConsumeResponse>>(JsonOptions, cancellationToken)
            ?? throw new CollaborationException(503, "COLLAB_IDENTITY_STEP_UP_UNAVAILABLE", "Identity 服务未返回再认证消费结果。");
    }

    private sealed record InternalApiResult<T>(bool Success, string? Code, string? Message, T? Data);
}
