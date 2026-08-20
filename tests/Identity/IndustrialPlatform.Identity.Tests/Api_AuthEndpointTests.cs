using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IndustrialPlatform.Identity.Api.Tests;

/// <summary>
/// 登录与当前用户端点测试(§15):未认证 401 统一信封、参数缺失 400 统一信封;
/// 登录全链路 E2E 需真实 PostgreSQL+Redis,以 <c>IDENTITY_E2E_DB=1</c> 门控(待验收)。
/// </summary>
public sealed class AuthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public AuthEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401ApiResultEnvelope()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/me");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Login_MissingPassword_Returns400ValidationEnvelope()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/v1/auth/login",
            new StringContent("""{"loginName":"alice"}""", Encoding.UTF8, "application/json"));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("ID_VALIDATION_FAILED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_EmptyBody_Returns400ValidationEnvelope()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/v1/auth/login",
            new StringContent("""{}""", Encoding.UTF8, "application/json"));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ID_VALIDATION_FAILED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Refresh_EmptyBody_Returns400ValidationEnvelope()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/v1/auth/refresh",
            new StringContent("""{}""", Encoding.UTF8, "application/json"));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("ID_VALIDATION_FAILED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Logout_WithoutToken_Returns401ApiResultEnvelope()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/v1/auth/logout",
            new StringContent("""{"refreshToken":"raw-token"}""", Encoding.UTF8, "application/json"));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LogoutAll_WithoutToken_Returns401ApiResultEnvelope()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/v1/auth/logout-all",
            new StringContent("""{}""", Encoding.UTF8, "application/json"));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ChangePassword_WithoutToken_Returns401ApiResultEnvelope()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/v1/auth/change-password",
            new StringContent("""{"currentPassword":"old","newPassword":"New-Passw0rd!"}""", Encoding.UTF8, "application/json"));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
    }
}
