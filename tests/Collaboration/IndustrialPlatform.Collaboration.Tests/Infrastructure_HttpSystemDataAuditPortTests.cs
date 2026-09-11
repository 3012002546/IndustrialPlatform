using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Infrastructure;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Infrastructure_HttpSystemDataAuditPortTests
{
    [Fact]
    public async Task Audit_transport_uses_the_fixed_ingest_path_and_contract_payload()
    {
        var handler = new RecordingHandler("{\"success\":true,\"data\":{\"auditEventNId\":\"AUD-1\"}}");
        var port = CreatePort(handler);

        await port.WriteAsync("T-1", null, "U-1", "message.send", "message", "M-1", new { conversationNId = "CV-1", messageType = "Text" }, CancellationToken.None);

        Assert.Equal("/internal/pf05/audits/facts:ingest", handler.RequestUri?.AbsolutePath);
        using var document = JsonDocument.Parse(handler.Body);
        Assert.Equal("collaboration.message.send", document.RootElement.GetProperty("Action").GetString());
        using var payload = JsonDocument.Parse(document.RootElement.GetProperty("PayloadJson").GetString()!);
        Assert.Equal(1, payload.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Succeeded", payload.RootElement.GetProperty("result").GetString());
        Assert.False(payload.RootElement.TryGetProperty("messageType", out _));
    }

    [Fact]
    public async Task Audit_transport_failure_is_exposed_as_the_stable_unavailable_error()
    {
        var port = new HttpSystemDataAuditPort(
            new StaticHttpClientFactory(new HttpClient(new ThrowingHandler())),
            new RecordingSigner(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Collaboration:SystemData:BaseUrl"] = "http://systemdata.test",
            }).Build(),
            new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimConstants.UserNId, "U-1"),
                        new Claim(ClaimConstants.TenantId, "T-1"),
                        new Claim(ClaimConstants.SessionId, "SID-1"),
                        new Claim(ClaimConstants.AuthVersion, "7"),
                    ], "test")),
                },
            });

        var exception = await Assert.ThrowsAsync<CollaborationException>(() => port.WriteAsync(
            "T-1",
            null,
            "U-1",
            "collaboration.test",
            "test",
            "OBJECT-1",
            new { ok = true },
            CancellationToken.None));

        Assert.Equal(503, exception.StatusCode);
        Assert.Equal("COLLAB_SYSTEMDATA_AUDIT_UNAVAILABLE", exception.Code);
    }

    private static HttpSystemDataAuditPort CreatePort(HttpMessageHandler handler)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimConstants.UserNId, "U-1"),
                new Claim(ClaimConstants.TenantId, "T-1"),
                new Claim(ClaimConstants.SessionId, "SID-1"),
                new Claim(ClaimConstants.AuthVersion, "7"),
            ], "test")),
        };
        return new HttpSystemDataAuditPort(
            new StaticHttpClientFactory(new HttpClient(handler)),
            new RecordingSigner(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Collaboration:SystemData:BaseUrl"] = "http://systemdata.test",
            }).Build(),
            new HttpContextAccessor { HttpContext = http });
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("systemdata unavailable"));
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class RecordingSigner : ITrustedServiceCallSigner
    {
        public string Sign(HttpMethod method, string pathAndQuery, ReadOnlyMemory<byte> body, string audience, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId) => "test-assertion";
    }
}
