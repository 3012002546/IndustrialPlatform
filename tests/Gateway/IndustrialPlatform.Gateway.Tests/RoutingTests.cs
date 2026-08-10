using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IndustrialPlatform.Gateway.Tests;

public sealed class RoutingTests : IClassFixture<DownstreamStubFixture>
{
    private readonly DownstreamStub _stub;

    public RoutingTests(DownstreamStubFixture fixture)
    {
        _stub = fixture.Stub;
    }

    [Fact]
    public async Task ProxyForwardsRequestAndStripsPathPrefix()
    {
        using var factory = TestGateway.Create(_stub);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/stub/health/ready");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal("/health/ready", payload.RootElement.GetProperty("receivedPath").GetString());
    }

    [Fact]
    public async Task ProxyReturnsUnifiedEnvelopeWhenDestinationUnavailable()
    {
        using var factory = TestGateway.CreateUnreachable();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/stub/health/ready");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("503", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ProxyReturnsGatewayTimeoutWhenDestinationSlow()
    {
        using var factory = TestGateway.Create(_stub, timeoutSeconds: 1);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/stub/slow");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("504", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnknownRouteReturnsUnifiedNotFoundEnvelope()
    {
        using var factory = TestGateway.Create(_stub);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/unknown");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("404", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CorsPolicyAppliesAllowedOriginHeader()
    {
        using var factory = TestGateway.Create(_stub);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/stub/health/ready");
        request.Headers.Add("Origin", "http://localhost:5173");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task CorsPreflightRequestIsHandledByGateway()
    {
        using var factory = TestGateway.Create(_stub);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/stub/health/ready");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}
