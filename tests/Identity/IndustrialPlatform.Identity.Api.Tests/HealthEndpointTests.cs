using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.Identity.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task HealthEndpointReturnsHealthyIdentityPayload()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, payload.RootElement.EnumerateObject().Count());
        Assert.Equal("Healthy", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal("Identity", payload.RootElement.GetProperty("service").GetString());
    }

    [Fact]
    public async Task LiveEndpointReturnsOkWithoutDependencyChecks()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyEndpointReportsAllRegisteredChecksWithoutCredentials()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable });

        var checks = payload.RootElement.GetProperty("checks").EnumerateArray().ToList();
        var checkNames = checks.Select(check => check.GetProperty("name").GetString()).ToArray();

        Assert.Contains("postgres", checkNames);
        Assert.Contains("redis", checkNames);
        Assert.Contains("seq", checkNames);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sample-dev-password", body);
    }

    [Fact]
    public async Task ReadyEndpointWithForcedUnhealthyCheckReturnsServiceUnavailable()
    {
        using var forcedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHealthCheck>();
            services.AddHealthChecks().AddCheck("forced-failure", () => HealthCheckResult.Unhealthy("boom"));
        }));

        using var client = forcedFactory.CreateClient();
        using var response = await client.GetAsync("/health/ready");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", payload.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            "forced-failure",
            payload.RootElement.GetProperty("checks").EnumerateArray().Select(check => check.GetProperty("name").GetString()));
    }
}
