using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IndustrialPlatform.Identity.Api.Tests;

public sealed class InternalInitializationEndpointTests
{
    [Fact]
    public async Task Missing_or_invalid_key_returns_401()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("InternalInitialization:Key", "expected-key"));
        using var client = factory.CreateClient();
        using var body = new StringContent(
            "{\"tenantNId\":\"tenant\",\"operationNId\":\"op-1\",\"desiredVersion\":\"identity-v1\",\"policy\":0,\"traceId\":\"trace-1\"}",
            Encoding.UTF8,
            "application/json");

        using var missing = await client.PostAsync("/api/v1/internal/initialization/identity/inspect", body);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/internal/initialization/identity/inspect")
        {
            Content = new StringContent(await body.ReadAsStringAsync(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Industrial-Initialization-Key", "wrong-key");
        using var invalid = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }
}
