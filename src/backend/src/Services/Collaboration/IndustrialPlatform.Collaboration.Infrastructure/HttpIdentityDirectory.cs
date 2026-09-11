using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Identity.Contracts.Collaboration;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>HTTP adapter for the Identity PF05 internal boundary.</summary>
public sealed class HttpIdentityDirectory : IndustrialPlatform.Collaboration.Application.ICollaborationIdentityDirectory
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IHttpClientFactory _clients;
    private readonly ITrustedServiceCallSigner _signer;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpIdentityDirectory(IHttpClientFactory clients, ITrustedServiceCallSigner signer, IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _clients = clients;
        _signer = signer;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<DirectoryUser?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        var path = $"/internal/pf05/identity/users/{Uri.EscapeDataString(userNId)}";
        using var response = await SendAsync(HttpMethod.Get, path, ReadOnlyMemory<byte>.Empty, tenantNId, "directory.get", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        var result = await ReadAsync<CollaborationDirectoryUserDetail>(response, cancellationToken);
        return result is null ? null : new DirectoryUser(result.UserNId, result.DisplayName, result.Status, result.SecurityVersion);
    }

    public async Task<DirectorySearchPage> SearchAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var requestNId = $"DIR-{Guid.NewGuid():N}";
        var query = $"cursor={Uri.EscapeDataString(cursor ?? string.Empty)}&keyword={Uri.EscapeDataString(keyword)}&limit={pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var path = $"/internal/pf05/identity/users?{query}";
        var requestIdentity = GetRequestIdentity(tenantNId);
        if (!string.Equals(requestIdentity.ActorUserNId, actorUserNId, StringComparison.Ordinal))
            throw new CollaborationException(401, "COLLAB_SESSION_INVALID", "当前登录会话已失效，请重新登录。");
        using var response = await SendAsync(HttpMethod.Get, path, ReadOnlyMemory<byte>.Empty, tenantNId, "directory.search", cancellationToken, requestNId);
        var result = await ReadAsync<CollaborationDirectoryPage>(response, cancellationToken);
        return result is null
            ? new DirectorySearchPage([], null)
            : new DirectorySearchPage(result.Items.Select(item => new DirectoryUser(item.UserNId, item.DisplayName, "Active", "0")).ToArray(), result.NextCursor);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, ReadOnlyMemory<byte> body, string tenantNId, string action, CancellationToken cancellationToken, string? requestNId = null)
    {
        var baseUrl = _configuration["Collaboration:Identity:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("https" or "http"))
            throw new CollaborationException(503, "COLLAB_IDENTITY_DIRECTORY_UNAVAILABLE", "Identity 服务内部地址未配置。");
        var client = _clients.CreateClient("Collaboration.Identity");
        var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Content = new ByteArrayContent(body.ToArray());
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var (actorUserNId, actorSessionNId, actorSecurityVersion) = GetRequestIdentity(tenantNId);
        var assertion = _signer.Sign(method, path, body, CollaborationServiceConstants.InternalIdentityAudience, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, action, requestNId ?? $"DIR-{Guid.NewGuid():N}");
        request.Headers.TryAddWithoutValidation(TrustedServiceCallValidator.HeaderName, assertion);
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            request.Dispose();
            throw;
        }
        catch (HttpRequestException)
        {
            request.Dispose();
            throw new CollaborationException(503, "COLLAB_IDENTITY_DIRECTORY_UNAVAILABLE", "Identity 目录服务不可用。");
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new CollaborationException((int)response.StatusCode, "COLLAB_IDENTITY_DIRECTORY_UNAVAILABLE", "Identity 目录调用失败。");
        var envelope = await response.Content.ReadFromJsonAsync<InternalApiResult<T>>(JsonOptions, cancellationToken);
        if (envelope is null || !envelope.Success)
            throw new CollaborationException(503, "COLLAB_IDENTITY_DIRECTORY_UNAVAILABLE", "Identity 未返回有效目录结果。");
        return envelope.Data;
    }

    private (string ActorUserNId, string ActorSessionNId, string ActorSecurityVersion) GetRequestIdentity(string tenantNId)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var actorUserNId = principal?.FindFirst(ClaimConstants.UserNId)?.Value;
        var actorTenantNId = principal?.FindFirst(ClaimConstants.TenantId)?.Value;
        var actorSessionNId = principal?.FindFirst(ClaimConstants.SessionId)?.Value;
        var actorSecurityVersion = principal?.FindFirst(ClaimConstants.AuthVersion)?.Value;
        if (principal?.Identity?.IsAuthenticated != true
            || string.IsNullOrWhiteSpace(actorUserNId)
            || !string.Equals(actorTenantNId, tenantNId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(actorSessionNId)
            || string.IsNullOrWhiteSpace(actorSecurityVersion))
            throw new CollaborationException(401, "COLLAB_SESSION_INVALID", "当前登录会话已失效，请重新登录。");
        return (actorUserNId, actorSessionNId, actorSecurityVersion);
    }

    private sealed record InternalApiResult<T>(bool Success, string? Code, string? Message, T? Data);
}
