using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IndustrialPlatform.Identity.Api.Tests;

/// <summary>
/// 验证 OpenAPI 文档端点生成有效文档,并包含既有 minimal API 端点。
/// </summary>
public sealed class OpenApiEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public OpenApiEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task OpenApiDocumentIsGeneratedForDefaultDocument()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = payload.RootElement;
        Assert.True(root.TryGetProperty("openapi", out var version));
        Assert.False(string.IsNullOrEmpty(version.GetString()));

        Assert.True(root.TryGetProperty("paths", out var paths));
        Assert.True(paths.TryGetProperty("/health", out _));
    }
}
