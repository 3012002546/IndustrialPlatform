using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IndustrialPlatform.Identity.Api.Tests;

/// <summary>
/// 有效刷新会话管理端点全栈授权回归：保留真实 Program、策略、授权处理器、控制器与应用服务，
/// 仅替换持久化端口以隔离测试数据。
/// </summary>
public sealed class SessionManagementEndpointTests
{
    private const string Tenant = "development";
    private const string UserNId = "user.alice";
    private const string CurrentSession = "SES-current";

    [Fact]
    public async Task Sessions_WithoutToken_Returns401ForReadAndRevoke()
    {
        using var factory = CreateFactory(new FakeRefreshSessionStore());
        using var client = factory.CreateClient();

        using var read = await client.GetAsync("/api/v1/sessions/active");
        using var revoke = await client.PostAsync("/api/v1/sessions/SES-target/revoke", null);

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revoke.StatusCode);
    }

    [Fact]
    public async Task Sessions_ViewPermission_ReturnsOnlyValidSafeProjection_AndCannotRevoke()
    {
        var store = new FakeRefreshSessionStore();
        var now = DateTimeOffset.UtcNow;
        store.Add(Tenant, "SES-current", expiresOn: now.AddMinutes(30));
        store.Add(Tenant, "SES-expired", expiresOn: now.AddMinutes(-1));
        store.Add(Tenant, "SES-used", used: true, expiresOn: now.AddMinutes(30));
        store.Add(Tenant, "SES-revoked", revoked: true, expiresOn: now.AddMinutes(30));
        store.Add(Tenant, "SES-deleted", deleted: true, expiresOn: now.AddMinutes(30));
        store.Add("other-tenant", "SES-other", expiresOn: now.AddMinutes(30));
        using var factory = CreateFactory(store, PermissionCatalog.SessionView);
        using var client = factory.CreateClient();
        var token = await CreateTokenAsync(factory);

        using var read = await SendAsync(client, HttpMethod.Get, "/api/v1/sessions/active", token);
        using var payload = JsonDocument.Parse(await read.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var item = payload.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("SES-current", item.GetProperty("sessionNId").GetString());
        Assert.True(item.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(
            ["sessionNId", "userNId", "loginName", "name", "loginOn", "lastRefreshedOn", "expiresOn", "isCurrent"],
            item.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.DoesNotContain("token", item.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ip", item.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userAgent", item.ToString(), StringComparison.OrdinalIgnoreCase);

        using var revoke = await SendAsync(client, HttpMethod.Post, "/api/v1/sessions/SES-current/revoke", token);
        Assert.Equal(HttpStatusCode.Forbidden, revoke.StatusCode);
        Assert.False(store.IsRevoked(Tenant, "SES-current"));
    }

    [Fact]
    public async Task Sessions_RevokePermission_CannotReadButCanRevokeOneTenantSessionIdempotently()
    {
        var store = new FakeRefreshSessionStore();
        store.Add(Tenant, "SES-target");
        store.Add(Tenant, "SES-sibling");
        store.Add("other-tenant", "SES-target");
        using var factory = CreateFactory(store, PermissionCatalog.SessionRevoke);
        using var client = factory.CreateClient();
        var token = await CreateTokenAsync(factory);

        using var read = await SendAsync(client, HttpMethod.Get, "/api/v1/sessions/active", token);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        using var first = await SendAsync(client, HttpMethod.Post, "/api/v1/sessions/SES-target/revoke", token);
        using var second = await SendAsync(client, HttpMethod.Post, "/api/v1/sessions/SES-target/revoke", token);
        var firstResult = await ReadDataAsync(first);
        var secondResult = await ReadDataAsync(second);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(firstResult.GetProperty("found").GetBoolean());
        Assert.True(secondResult.GetProperty("found").GetBoolean());
        Assert.False(firstResult.GetProperty("isCurrent").GetBoolean());
        Assert.True(store.IsRevoked(Tenant, "SES-target"));
        Assert.False(store.IsRevoked(Tenant, "SES-sibling"));
        Assert.False(store.IsRevoked("other-tenant", "SES-target"));
        Assert.Equal(2, store.RevokeCalls.Count(call => call == (Tenant, "SES-target")));
    }

    [Fact]
    public async Task Sessions_CurrentRevoke_ReturnsCurrentMarkerForLocalLogout()
    {
        var store = new FakeRefreshSessionStore();
        store.Add(Tenant, CurrentSession);
        using var factory = CreateFactory(store, PermissionCatalog.SessionRevoke);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/sessions/{CurrentSession}/revoke",
            await CreateTokenAsync(factory, CurrentSession));
        var result = await ReadDataAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result.GetProperty("found").GetBoolean());
        Assert.True(result.GetProperty("isCurrent").GetBoolean());
        Assert.True(store.IsRevoked(Tenant, CurrentSession));
    }

    [Fact]
    public async Task Sessions_NoSessionPermission_Returns403ForBothOperations()
    {
        var store = new FakeRefreshSessionStore();
        store.Add(Tenant, "SES-target");
        using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        var token = await CreateTokenAsync(factory);

        using var read = await SendAsync(client, HttpMethod.Get, "/api/v1/sessions/active", token);
        using var revoke = await SendAsync(client, HttpMethod.Post, "/api/v1/sessions/SES-target/revoke", token);

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, revoke.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeRefreshSessionStore store,
        params string[] permissions)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRefreshSessionStore>();
                services.AddSingleton<IRefreshSessionStore>(store);
                services.RemoveAll<ISessionRevocationStore>();
                services.AddSingleton<ISessionRevocationStore>(new FakeRevocationStore());
                services.RemoveAll<IAuthorizationDataStore>();
                services.AddSingleton<IAuthorizationDataStore>(
                    new FakeAuthorizationDataStore(
                        new AuthorizationSnapshot(Tenant, UserNId, UserStatus.Active, 3, permissions)));
                services.RemoveAll<IPermissionCache>();
                services.AddSingleton<IPermissionCache>(new FakePermissionCache());
            }));

    private static async Task<string> CreateTokenAsync(
        WebApplicationFactory<Program> factory,
        string sessionNId = CurrentSession)
    {
        var tokenFactory = factory.Services.GetRequiredService<IAccessTokenFactory>();
        return tokenFactory.Create(new AccessTokenDescriptor(
            UserNId,
            "alice",
            Tenant,
            ["role.operator"],
            sessionNId,
            3,
            DateTimeOffset.UtcNow)).Token;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        return root.TryGetProperty("data", out var data) ? data.Clone() : root.Clone();
    }

    private sealed class FakeRefreshSessionStore : IRefreshSessionStore
    {
        private readonly List<Row> rows = [];

        public List<(string TenantNId, string SessionNId)> RevokeCalls { get; } = [];

        public void Add(
            string tenantNId,
            string sessionNId,
            bool used = false,
            bool revoked = false,
            bool deleted = false,
            bool userDeleted = false,
            DateTimeOffset? expiresOn = null)
        {
            var now = DateTimeOffset.UtcNow;
            rows.Add(new Row(
                tenantNId,
                sessionNId,
                used,
                revoked,
                deleted,
                userDeleted,
                expiresOn ?? now.AddHours(1),
                new ActiveRefreshSession(
                    sessionNId,
                    "USR-1",
                    "alice",
                    "Alice",
                    now.AddMinutes(-5),
                    now.AddMinutes(-1),
                    expiresOn ?? now.AddHours(1),
                    Guid.NewGuid())));
        }

        public bool IsRevoked(string tenantNId, string sessionNId)
            => rows.Any(row => row.TenantNId == tenantNId && row.SessionNId == sessionNId && row.Revoked);

        public Task<StoredRefreshSession?> FindByRawTokenAsync(string rawToken, CancellationToken cancellationToken)
            => Task.FromResult<StoredRefreshSession?>(null);

        public Task AddAsync(NewRefreshSession session, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<RefreshRotationStatus> RotateAsync(Guid currentSessionId, NewRefreshSession replacement, CancellationToken cancellationToken)
            => Task.FromResult(RefreshRotationStatus.Invalid);

        public Task RevokeFamilyAsync(string familyNId, string reason, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ActiveRefreshSession>> ListActiveForTenantAsync(
            string tenantNId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ActiveRefreshSession>>(rows
                .Where(row => row.TenantNId == tenantNId
                    && !row.Deleted
                    && !row.UserDeleted
                    && !row.Used
                    && !row.Revoked
                    && row.ExpiresOn > now)
                .Select(row => row.Projection)
                .ToList());

        public Task<bool> RevokeByNIdAsync(
            string tenantNId,
            string sessionNId,
            string reason,
            CancellationToken cancellationToken)
        {
            var row = rows.FirstOrDefault(row => row.TenantNId == tenantNId && row.SessionNId == sessionNId && !row.Deleted);
            if (row is null) return Task.FromResult(false);
            row.Revoked = true;
            RevokeCalls.Add((tenantNId, sessionNId));
            return Task.FromResult(true);
        }

        private sealed class Row(
            string tenantNId,
            string sessionNId,
            bool used,
            bool revoked,
            bool deleted,
            bool userDeleted,
            DateTimeOffset expiresOn,
            ActiveRefreshSession projection)
        {
            public string TenantNId { get; } = tenantNId;
            public string SessionNId { get; } = sessionNId;
            public bool Used { get; } = used;
            public bool Revoked { get; set; } = revoked;
            public bool Deleted { get; } = deleted;
            public bool UserDeleted { get; } = userDeleted;
            public DateTimeOffset ExpiresOn { get; } = expiresOn;
            public ActiveRefreshSession Projection { get; } = projection;
        }
    }

    private sealed class FakeAuthorizationDataStore(AuthorizationSnapshot snapshot) : IAuthorizationDataStore
    {
        public Task<AuthorizationSnapshot?> GetSnapshotAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
            => Task.FromResult<AuthorizationSnapshot?>(snapshot);
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

    private sealed class FakeRevocationStore : ISessionRevocationStore
    {
        public Task RevokeAsync(string sessionNId, TimeSpan ttl, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<bool> IsRevokedAsync(string sessionNId, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }
}
