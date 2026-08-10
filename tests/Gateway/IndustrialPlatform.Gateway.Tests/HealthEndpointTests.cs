using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IndustrialPlatform.Gateway.Tests;

public sealed class HealthEndpointTests : IClassFixture<DownstreamStubFixture>
{
    private readonly DownstreamStub _stub;

    public HealthEndpointTests(DownstreamStubFixture fixture)
    {
        _stub = fixture.Stub;
    }

    [Fact]
    public async Task HealthEndpointReturnsHealthyGatewayPayload()
    {
        using var factory = TestGateway.Create(_stub);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal("Gateway", payload.RootElement.GetProperty("service").GetString());
    }

    [Fact]
    public async Task LiveEndpointReturnsOkWithoutDependencyChecks()
    {
        using var factory = TestGateway.Create(_stub);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyEndpointAggregatesDownstreamHealthWithoutCredentials()
    {
        using var factory = TestGateway.Create(_stub);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", payload.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            "service.stub",
            payload.RootElement.GetProperty("checks").EnumerateArray().Select(check => check.GetProperty("name").GetString()));

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sample-dev-password", body);
    }

    [Fact]
    public async Task ReadyEndpointReturnsServiceUnavailableWhenAllServicesUnreachable()
    {
        using var factory = TestGateway.CreateUnreachable();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", payload.RootElement.GetProperty("status").GetString());
    }
}

/// <summary>
/// 共享的桩服务生命周期,每个测试类一次。
/// </summary>
public sealed class DownstreamStubFixture : IAsyncLifetime
{
    /// <summary>桩服务实例。</summary>
    public DownstreamStub Stub { get; private set; } = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        Stub = await DownstreamStub.StartAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await Stub.DisposeAsync();
    }
}
