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

/// <summary>
/// 登录全链路 E2E(待验收):需真实 PostgreSQL+Redis。
/// 需先通过显式初始化(IdentityInitializationService,SystemData 编排或受控部署)创建内置 admin;
/// 设置 <c>IDENTITY_E2E_DB=1</c> 后运行,未设置时跳过,不影响本地无依赖测试基线。
/// 本用例只使用测试夹具登录名/密码,绝不依赖环境变量密码引导(§29A.4 已移除)。
/// </summary>
public sealed class LoginE2ETests
{
    [Fact]
    [Trait("Category", "E2E")]
    public async Task Login_HappyPath_ReturnsSessionWithCurrentUser()
    {
        if (Environment.GetEnvironmentVariable("IDENTITY_E2E_DB") != "1")
        {
            // 待验收:需真实 PostgreSQL+Redis;未设置 IDENTITY_E2E_DB=1 时本地降级为占位通过。
            return;
        }

        const string loginName = "admin";
        const string password = "E2e-Admin-123456";
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var loginResponse = await client.PostAsync(
            "/api/v1/auth/login",
            new StringContent(
                $$"""{"loginName":"{{loginName}}","password":"{{password}}"}""",
                Encoding.UTF8,
                "application/json"));
        using var payload = JsonDocument.Parse(await loginResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.True(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("200", payload.RootElement.GetProperty("code").GetString());

        var data = payload.RootElement.GetProperty("data");
        var accessToken = data.GetProperty("accessToken").GetString();
        var refreshToken = data.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));
        Assert.True(DateTimeOffset.TryParse(data.GetProperty("expiresAt").GetString(), out _));
        Assert.Equal("ADMIN", data.GetProperty("user").GetProperty("userNId").GetString());

        // 用返回的 Access Token 访问当前用户端点
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var meResponse = await client.SendAsync(meRequest);
        using var mePayload = JsonDocument.Parse(await meResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.Equal("ADMIN", mePayload.RootElement.GetProperty("data").GetProperty("userNId").GetString());
    }
}
