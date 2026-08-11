using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IndustrialPlatform.Identity.Api.Tests;

/// <summary>
/// JWKS 公钥端点测试(§12):/.well-known/jwks.json 返回 RSA 公钥文档且禁止缓存。
/// 不依赖数据库/Redis:签名密钥在配置为空时使用开发期临时密钥。
/// </summary>
public sealed class JwksEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public JwksEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task JwksEndpointReturnsPublicKeyDocumentWithoutCache()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/.well-known/jwks.json");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.NoStore, "JWKS 响应必须设置 Cache-Control: no-store");

        var keys = payload.RootElement.GetProperty("keys").EnumerateArray().ToList();
        var key = Assert.Single(keys);
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.Equal("identity-default", key.GetProperty("kid").GetString());
        Assert.Equal("sig", key.GetProperty("use").GetString());
        Assert.Equal("RS256", key.GetProperty("alg").GetString());
        Assert.False(string.IsNullOrWhiteSpace(key.GetProperty("n").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(key.GetProperty("e").GetString()));
    }
}
