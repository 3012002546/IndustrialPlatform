using System.Net;
using System.Security.Claims;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Infrastructure;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Infrastructure_HttpIdentityDirectoryTests
{
    [Fact]
    public async Task Search_forwards_real_request_identity_and_server_cursor()
    {
        var signer = new RecordingSigner();
        var handler = new RecordingHandler("{\"success\":true,\"data\":{\"items\":[{\"userNId\":\"U-2\",\"displayName\":\"Bob\",\"canStart\":true}],\"nextCursor\":\"opaque-from-identity\"}}" );
        var directory = CreateDirectory(handler, signer);

        var page = await directory.SearchAsync("T-1", "U-1", "bob", "opaque-from-client", 20, CancellationToken.None);

        Assert.Equal("opaque-from-identity", page.NextCursor);
        Assert.Single(page.Items);
        Assert.Equal("U-2", page.Items[0].UserNId);
        Assert.Equal("U-1", signer.ActorUserNId);
        Assert.Equal("SID-1", signer.ActorSessionNId);
        Assert.Equal("7", signer.ActorSecurityVersion);
        Assert.Equal(
            "/internal/pf05/identity/users?cursor=opaque-from-client&keyword=bob&limit=20",
            handler.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Search_fails_closed_without_a_complete_request_identity()
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimConstants.UserNId, "U-1"),
            new Claim(ClaimConstants.TenantId, "T-1"),
        }, "test")) };
        var accessor = new HttpContextAccessor { HttpContext = http };
        var directory = new HttpIdentityDirectory(
            new StaticHttpClientFactory(new HttpClient(new RecordingHandler("{}"))),
            new RecordingSigner(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Collaboration:Identity:BaseUrl"] = "http://identity.test" }).Build(),
            accessor);

        var exception = await Assert.ThrowsAsync<CollaborationException>(() => directory.SearchAsync("T-1", "U-1", "bob", null, 20, CancellationToken.None));

        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task Directory_transport_failure_is_exposed_as_the_stable_unavailable_error()
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimConstants.UserNId, "U-1"),
            new Claim(ClaimConstants.TenantId, "T-1"),
            new Claim(ClaimConstants.SessionId, "SID-1"),
            new Claim(ClaimConstants.AuthVersion, "7"),
        }, "test")) };
        var directory = new HttpIdentityDirectory(
            new StaticHttpClientFactory(new HttpClient(new ThrowingHandler())),
            new RecordingSigner(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Collaboration:Identity:BaseUrl"] = "http://identity.test" }).Build(),
            new HttpContextAccessor { HttpContext = http });

        var exception = await Assert.ThrowsAsync<CollaborationException>(() => directory.GetAsync("T-1", "U-2", CancellationToken.None));

        Assert.Equal(503, exception.StatusCode);
        Assert.Equal("COLLAB_IDENTITY_DIRECTORY_UNAVAILABLE", exception.Code);
    }

    [Fact]
    public async Task Directory_failed_envelope_is_not_misreported_as_a_missing_user()
    {
        var directory = CreateDirectory(
            new RecordingHandler("{\"success\":false,\"code\":\"IDENTITY_UNAVAILABLE\",\"data\":null}"),
            new RecordingSigner());

        var exception = await Assert.ThrowsAsync<CollaborationException>(() => directory.GetAsync("T-1", "U-2", CancellationToken.None));

        Assert.Equal(503, exception.StatusCode);
        Assert.Equal("COLLAB_IDENTITY_DIRECTORY_UNAVAILABLE", exception.Code);
    }

    private static HttpIdentityDirectory CreateDirectory(RecordingHandler handler, RecordingSigner signer)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimConstants.UserNId, "U-1"),
            new Claim(ClaimConstants.TenantId, "T-1"),
            new Claim(ClaimConstants.SessionId, "SID-1"),
            new Claim(ClaimConstants.AuthVersion, "7"),
        }, "test")) };
        return new HttpIdentityDirectory(
            new StaticHttpClientFactory(new HttpClient(handler)),
            signer,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Collaboration:Identity:BaseUrl"] = "http://identity.test" }).Build(),
            new HttpContextAccessor { HttpContext = http });
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("identity unavailable"));
    }

    private sealed class RecordingSigner : ITrustedServiceCallSigner
    {
        public string? ActorUserNId { get; private set; }
        public string? ActorSessionNId { get; private set; }
        public string? ActorSecurityVersion { get; private set; }

        public string Sign(HttpMethod method, string pathAndQuery, ReadOnlyMemory<byte> body, string audience, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId)
        {
            ActorUserNId = actorUserNId;
            ActorSessionNId = actorSessionNId;
            ActorSecurityVersion = actorSecurityVersion;
            return "test-assertion";
        }
    }
}
