using System.Net;
using System.Text;
using IndustrialPlatform.SystemData.Application.Authorization;
using IndustrialPlatform.SystemData.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

public sealed class HttpIdentityDirectoryClientAuthorizationTests
{
    [Fact]
    public async Task Evaluate_ForwardsBearerTokenAndReadsIdentityEnvelope()
    {
        var handler = new RecordingHandler();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SystemData:Identity:BaseUrl"] = "http://identity/",
        }).Build();
        var client = new HttpIdentityDirectoryClient(
            new SingleClientFactory(new HttpClient(handler)),
            configuration,
            NullLogger<HttpIdentityDirectoryClient>.Instance);

        var result = await client.EvaluateAsync(
            new SystemDataPermissionRequest(
                "development", "admin", "SES-1", 3,
                "systemdata.organization.view", "secret-access-token"),
            CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("secret-access-token", handler.AuthorizationParameter);
        Assert.Equal("/api/v1/authorization/evaluate", handler.RequestUri?.AbsolutePath);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"success\":true,\"code\":\"200\",\"message\":\"success\",\"data\":{\"allowed\":true,\"reason\":\"None\"}}",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
