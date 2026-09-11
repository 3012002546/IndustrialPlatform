using System.Net;
using System.Security.Claims;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Infrastructure;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Infrastructure_HttpSystemDataFilePortTests
{
    [Fact]
    public async Task Release_reference_rejects_a_success_http_status_with_a_failed_envelope()
    {
        var handler = new RecordingHandler("{\"success\":false,\"code\":\"FILE_REFERENCE_NOT_FOUND\",\"message\":\"missing\"}");
        var port = CreatePort(handler);

        var exception = await Assert.ThrowsAsync<CollaborationException>(() => port.ReleaseLegalHoldReferenceAsync(
            "T-1",
            "U-1",
            "FILE-1",
            "CASE-A",
            CancellationToken.None));

        Assert.Equal(503, exception.StatusCode);
        Assert.Equal("COLLAB_SYSTEMDATA_FILE_UNAVAILABLE", exception.Code);
        Assert.Equal(HttpMethod.Delete, handler.Method);
        Assert.Equal("/internal/pf05/systemdata/files/FILE-1/references/HOLD-CASE-A-FILE-1", handler.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task File_transport_failure_is_exposed_as_the_stable_unavailable_error()
    {
        var port = CreatePort(new ThrowingHandler());

        var exception = await Assert.ThrowsAsync<CollaborationException>(() => port.GetAsync(
            "T-1",
            "U-1",
            "FILE-1",
            CancellationToken.None));

        Assert.Equal(503, exception.StatusCode);
        Assert.Equal("COLLAB_SYSTEMDATA_FILE_UNAVAILABLE", exception.Code);
    }

    [Fact]
    public async Task Collaboration_reference_and_hold_calls_use_the_frozen_internal_paths_and_payloads()
    {
        var handler = new RecordingHandler("{\"success\":true,\"data\":{\"tenantNId\":\"T-1\",\"referenceNId\":\"REF-1\",\"fileNId\":\"FILE-1\",\"status\":\"Active\",\"version\":0}}");
        var port = CreatePort(handler);

        await port.BindReferenceAsync("T-1", "U-1", "FILE-1", "REF-1", "CONV-1", "MSG-1", "ATT-1", "U-1", "CollaborationMessageAttachment", "REQ-1", CancellationToken.None);
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal("/internal/pf05/systemdata/files/references/REF-1", handler.RequestUri?.AbsolutePath);
        Assert.Contains("CollaborationMessageAttachment", handler.Body, StringComparison.Ordinal);

        await port.AddLegalHoldReferenceAsync("T-1", "U-1", "FILE-1", "CASE-A", "REQ-2", new string('a', 64), 3, CancellationToken.None);
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal("/internal/pf05/systemdata/files/holds/CASE-A/files/FILE-1", handler.RequestUri?.AbsolutePath);
        Assert.Contains("CaseRevision", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_reference_calls_use_the_receiver_reference_actions_and_request_ids()
    {
        var handler = new RecordingHandler("{\"success\":true,\"data\":{\"tenantNId\":\"T-1\",\"referenceNId\":\"REF-EXP-1\",\"fileNId\":\"FILE-1\",\"status\":\"Active\",\"version\":0}}");
        var signer = new RecordingSigner();
        var port = CreatePort(handler, signer);

        await port.BindExportReferenceAsync("T-1", "U-1", "FILE-1", "REF-EXP-1", "CV-1", "EXP-1", CancellationToken.None);

        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal("file.reference.bind", signer.Action);
        Assert.Equal("export-reference-EXP-1", signer.RequestNId);
        Assert.Equal("/internal/pf05/systemdata/files/references/REF-EXP-1", handler.RequestUri?.AbsolutePath);
        Assert.Contains("CollaborationComplianceExport", handler.Body, StringComparison.Ordinal);

        await port.ReleaseExportReferenceAsync("T-1", "U-1", "FILE-1", "REF-EXP-1", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("file.reference.release", signer.Action);
        Assert.Equal("export-release-REF-EXP-1", signer.RequestNId);
        Assert.Equal("/internal/pf05/systemdata/files/references/REF-EXP-1/release", handler.RequestUri?.AbsolutePath);
    }

    private static HttpSystemDataFilePort CreatePort(HttpMessageHandler handler, RecordingSigner? signer = null)
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
        return new HttpSystemDataFilePort(
            new StaticHttpClientFactory(new HttpClient(handler)),
            signer ?? new RecordingSigner(),
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

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("systemdata unavailable"));
    }

    private sealed class RecordingSigner : ITrustedServiceCallSigner
    {
        public string? Action { get; private set; }
        public string? RequestNId { get; private set; }

        public string Sign(HttpMethod method, string pathAndQuery, ReadOnlyMemory<byte> body, string audience, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId)
        {
            Action = action;
            RequestNId = requestNId;
            return "test-assertion";
        }
    }
}
