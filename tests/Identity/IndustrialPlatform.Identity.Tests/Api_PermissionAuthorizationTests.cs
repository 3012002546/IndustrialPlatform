using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using IndustrialPlatform.Identity.Api.Authorization;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Domain.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IndustrialPlatform.Identity.Api.Tests;

/// <summary>
/// 权限策略探针控制器:仅用于全栈授权 E2E 测试,经 ApplicationPart 挂载,不参与产品路由。
/// </summary>
[ApiController]
[Route("test/permission-probe")]
public sealed class PermissionProbeController : ControllerBase
{
    /// <summary>受 <c>identity.user.view</c> 策略保护的探针端点。</summary>
    [Authorize(Policy = PermissionPolicies.UserView)]
    [HttpGet]
    public IActionResult Get() => Ok(new { granted = true });

    /// <summary>受 <c>platform.operation.view</c> 策略保护的生产操作入口探针。</summary>
    [Authorize(Policy = PermissionPolicies.PlatformOperationView)]
    [HttpGet("operation")]
    public IActionResult GetOperation() => Ok(new { granted = true });
}

/// <summary>
/// 服务端 RBAC 全栈测试(§18):真实 Program/处理器/评估器,仅替换三个存储端口
/// (撤销/授权数据/权限缓存),验证 200 放行与 401/403/503 信封映射。
/// </summary>
public sealed class PermissionAuthorizationTests
{
    private const string Tenant = "development";
    private const string UserNId = "user.alice";
    private const string Session = "SES-abc";
    private const string Required = "identity.user.view";

    [Fact]
    public async Task Probe_WithPermission_Returns200()
    {
        // Arrange
        var dataStore = new FakeAuthorizationDataStore
        {
            Snapshot = new AuthorizationSnapshot(Tenant, UserNId, UserStatus.Active, 3, [Required]),
        };
        using var factory = CreateFactory(dataStore: dataStore);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(ProbeRequest(await CreateTokenAsync(factory, authVersion: 3)));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OperationProbe_WithOperationPermission_Returns200()
    {
        var dataStore = new FakeAuthorizationDataStore
        {
            Snapshot = new AuthorizationSnapshot(
                Tenant,
                UserNId,
                UserStatus.Active,
                3,
                ["platform.operation.view"]),
        };
        using var factory = CreateFactory(dataStore: dataStore);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            ProbeRequest(await CreateTokenAsync(factory, authVersion: 3), "/api/v1/test/permission-probe/operation"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Probe_WithoutPermission_Returns403DeniedEnvelope()
    {
        // Arrange
        var dataStore = new FakeAuthorizationDataStore
        {
            Snapshot = new AuthorizationSnapshot(Tenant, UserNId, UserStatus.Active, 3, ["identity.user.update"]),
        };
        using var factory = CreateFactory(dataStore: dataStore);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(ProbeRequest(await CreateTokenAsync(factory, authVersion: 3)));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        // Assert: 缺权限 → 403 ID_PERMISSION_DENIED 信封
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("ID_PERMISSION_DENIED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Probe_AuthVersionMismatch_Returns401Envelope()
    {
        // Arrange: 数据库 AuthVersion=3,令牌 ver=4 → 会话失效
        var dataStore = new FakeAuthorizationDataStore
        {
            Snapshot = new AuthorizationSnapshot(Tenant, UserNId, UserStatus.Active, 3, [Required]),
        };
        using var factory = CreateFactory(dataStore: dataStore);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(ProbeRequest(await CreateTokenAsync(factory, authVersion: 4)));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Probe_DisabledUser_Returns403AccountDisabledEnvelope()
    {
        // Arrange: 持有权限但已禁用 → 状态门禁优先,账号不可用
        var dataStore = new FakeAuthorizationDataStore
        {
            Snapshot = new AuthorizationSnapshot(Tenant, UserNId, UserStatus.Disabled, 3, [Required]),
        };
        using var factory = CreateFactory(dataStore: dataStore);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(ProbeRequest(await CreateTokenAsync(factory, authVersion: 3)));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ID_PERMISSION_DENIED", payload.RootElement.GetProperty("code").GetString());
        Assert.Contains("账号不可用", payload.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Probe_RevokedSession_Returns401Envelope()
    {
        // Arrange: sid 已撤销 → 会话失效(评估器在数据装载前拒绝)
        var revocation = new FakeRevocationStore();
        revocation.Revoked.Add(Session);
        var dataStore = new FakeAuthorizationDataStore
        {
            Snapshot = new AuthorizationSnapshot(Tenant, UserNId, UserStatus.Active, 3, [Required]),
        };
        using var factory = CreateFactory(revocation: revocation, dataStore: dataStore);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(ProbeRequest(await CreateTokenAsync(factory, authVersion: 3)));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Probe_SecurityStoreUnavailable_Returns503Envelope()
    {
        // Arrange: 撤销存储不可用 → fail-closed 503
        var revocation = new FakeRevocationStore { FailOnCheck = true };
        var dataStore = new FakeAuthorizationDataStore
        {
            Snapshot = new AuthorizationSnapshot(Tenant, UserNId, UserStatus.Active, 3, [Required]),
        };
        using var factory = CreateFactory(revocation: revocation, dataStore: dataStore);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(ProbeRequest(await CreateTokenAsync(factory, authVersion: 3)));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ID_AUTH_SECURITY_STORE_UNAVAILABLE", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Probe_Unauthenticated_Returns401Envelope()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/permission-probe"));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
    }

    private static async Task<string> CreateTokenAsync(WebApplicationFactory<Program> factory, int authVersion)
    {
        var tokenFactory = factory.Services.GetRequiredService<IAccessTokenFactory>();
        var result = tokenFactory.Create(new AccessTokenDescriptor(
            UserNId,
            "alice",
            Tenant,
            ["role.operator"],
            Session,
            authVersion,
            DateTimeOffset.UtcNow));
        return result.Token;
    }

    private static HttpRequestMessage ProbeRequest(
        string token,
        string path = "/api/v1/test/permission-probe")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeRevocationStore? revocation = null,
        FakeAuthorizationDataStore? dataStore = null,
        FakePermissionCache? cache = null)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                // 替换三个存储端口,保留真实处理器/评估器/策略管线
                services.RemoveAll<ISessionRevocationStore>();
                services.AddSingleton<ISessionRevocationStore>(revocation ?? new FakeRevocationStore());
                services.RemoveAll<IAuthorizationDataStore>();
                services.AddSingleton<IAuthorizationDataStore>(dataStore ?? new FakeAuthorizationDataStore());
                services.RemoveAll<IPermissionCache>();
                services.AddSingleton<IPermissionCache>(cache ?? new FakePermissionCache());

                // 挂载测试项目内的探针控制器
                services.AddControllers().AddApplicationPart(typeof(PermissionProbeController).Assembly);
            }));

    private sealed class FakeRevocationStore : ISessionRevocationStore
    {
        public HashSet<string> Revoked { get; } = [];
        public bool FailOnCheck { get; set; }

        public Task RevokeAsync(string sessionNId, TimeSpan ttl, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public async Task<bool> IsRevokedAsync(string sessionNId, CancellationToken cancellationToken)
        {
            if (FailOnCheck)
            {
                await Task.Yield();
                throw new SecurityStoreUnavailableException();
            }

            return Revoked.Contains(sessionNId);
        }
    }

    private sealed class FakeAuthorizationDataStore : IAuthorizationDataStore
    {
        public AuthorizationSnapshot? Snapshot { get; set; }

        public Task<AuthorizationSnapshot?> GetSnapshotAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
            => Task.FromResult(Snapshot);
    }

    private sealed class FakePermissionCache : IPermissionCache
    {
        public Task<UserSecurityCacheEntry?> TryGetUserSnapshotAsync(string tenantNId, string userNId, int authVersion, CancellationToken cancellationToken)
            => Task.FromResult<UserSecurityCacheEntry?>(null);

        public Task<IReadOnlyList<string>?> TryGetPermissionsAsync(string tenantNId, string userNId, int authVersion, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>?>(null);

        public Task SetAsync(AuthorizationSnapshot snapshot, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task InvalidateAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
