using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IndustrialPlatform.Identity.Api.Tests;

/// <summary>
/// 管理端点全栈测试(§16):真实 Program/控制器/服务,替换管理存储与审计/会话端口,
/// 验证授权信封、分页信封与 400/404/409 业务错误映射。
/// </summary>
public sealed class ManagementEndpointTests
{
    private const string Tenant = "development";
    private const string UserNId = "user.alice";
    private const string Session = "SES-abc";
    private const string StrongPassword = "Ab1!defghijk";

    [Fact]
    public async Task Users_List_WithPermission_ReturnsPage()
    {
        var store = new InMemoryManagementStore
        {
            UserPage = new StoredUserPage([StoredUser("alice.user", "alice", "Alice")], 1),
        };
        using var factory = CreateFactory(store, [PermissionCatalog.UserView]);
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/api/v1/users", token: await CreateTokenAsync(factory));

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal("alice.user", data.GetProperty("items")[0].GetProperty("userNId").GetString());
    }

    [Fact]
    public async Task Users_List_Unauthenticated_Returns401Envelope()
    {
        using var factory = CreateFactory(new InMemoryManagementStore(), [PermissionCatalog.UserView]);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/users");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Users_Create_WithPermission_ReturnsSummary()
    {
        var store = new InMemoryManagementStore();
        using var factory = CreateFactory(store, [PermissionCatalog.UserCreate]);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/users",
            await CreateTokenAsync(factory),
            Json(new { nId = "alice.user", loginName = "alice", name = "Alice", initialPassword = StrongPassword }));

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("alice.user", payload.RootElement.GetProperty("data").GetProperty("userNId").GetString());
        Assert.Equal("user.create", Assert.Single(store.Audits).Action);
    }

    [Fact]
    public async Task Users_Create_NoPermission_Returns403Envelope()
    {
        var store = new InMemoryManagementStore();
        using var factory = CreateFactory(store, [PermissionCatalog.UserView]);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/users",
            await CreateTokenAsync(factory),
            Json(new { loginName = "alice", name = "Alice", initialPassword = StrongPassword }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ID_PERMISSION_DENIED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Users_Create_EmptyLogin_Returns400ValidationEnvelope()
    {
        var store = new InMemoryManagementStore();
        using var factory = CreateFactory(store, [PermissionCatalog.UserCreate]);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/users",
            await CreateTokenAsync(factory),
            Json(new { name = "Alice", initialPassword = StrongPassword }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ID_VALIDATION_FAILED", payload.RootElement.GetProperty("code").GetString());
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Users_Get_Missing_Returns404Envelope()
    {
        var store = new InMemoryManagementStore { StoredUser = null };
        using var factory = CreateFactory(store, [PermissionCatalog.UserView]);
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/api/v1/users/missing.user", token: await CreateTokenAsync(factory));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("404", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Users_SetStatus_DisableSelf_Returns400BusinessRuleEnvelope()
    {
        // 目标用户即执行者:禁止禁用当前登录用户
        var store = new InMemoryManagementStore { UserAggregate = CreateAggregateUser(UserNId) };
        using var factory = CreateFactory(store, [PermissionCatalog.UserStatus]);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/users/{UserNId}/status",
            await CreateTokenAsync(factory),
            Json(new { enabled = false, expectedOptimisticVersion = 0, expectedConcurrencyVersion = Guid.Empty }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ID_BUSINESS_RULE_VIOLATION", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Users_ResetPassword_Success_Returns200()
    {
        var store = new InMemoryManagementStore { UserAggregate = CreateAggregateUser("alice.user") };
        using var factory = CreateFactory(store, [PermissionCatalog.UserUpdate]);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/users/alice.user/reset-password",
            await CreateTokenAsync(factory),
            Json(new { newPassword = "Xy2!newpassword" }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Roles_List_WithPermission_ReturnsPage()
    {
        var store = new InMemoryManagementStore
        {
            RolePage = new StoredRolePage(
                [
                    new StoredRole(
                        Guid.NewGuid(),
                        Tenant,
                        "role.operator",
                        "Operator",
                        null,
                        IsSystem: false,
                        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                        1,
                        Guid.NewGuid(),
                        ["identity.user.view"]),
                ],
                1),
        };
        using var factory = CreateFactory(store, [PermissionCatalog.RoleView]);
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/api/v1/roles", token: await CreateTokenAsync(factory));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = payload.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("role.operator", item.GetProperty("roleNId").GetString());
        Assert.Equal("identity.user.view", item.GetProperty("permissionNIds")[0].GetString());
    }

    [Fact]
    public async Task Permissions_Tree_WithPermission_ReturnsTree()
    {
        var store = new InMemoryManagementStore
        {
            AllPermissions =
            [
                Permission.Create("identity.user.view", "查看用户", PermissionType.Action, null, null),
            ],
        };
        using var factory = CreateFactory(store, [PermissionCatalog.PermissionView]);
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/api/v1/permissions/tree", token: await CreateTokenAsync(factory));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = payload.RootElement.GetProperty("data")[0];
        Assert.Equal("identity.user.view", root.GetProperty("permissionNId").GetString());
        Assert.Equal("Action", root.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Audits_Logins_WithPermission_ReturnsPage()
    {
        var store = new InMemoryManagementStore();
        var auditStore = new InMemoryLoginAuditQueryStore(
            new LoginAuditPage(
                [
                    new LoginAuditRow(
                        Tenant,
                        UserNId,
                        "alice",
                        true,
                        null,
                        "ip-hash",
                        "ua-hash",
                        "trace-1",
                        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
                ],
                1));
        using var factory = CreateFactory(store, [PermissionCatalog.AuditLoginView], auditStore);
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/api/v1/audits/logins", token: await CreateTokenAsync(factory));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = payload.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("alice", item.GetProperty("loginNameSnapshot").GetString());
        Assert.True(item.GetProperty("success").GetBoolean());
    }

    private static async Task<string> CreateTokenAsync(WebApplicationFactory<Program> factory)
    {
        var tokenFactory = factory.Services.GetRequiredService<IAccessTokenFactory>();
        var result = tokenFactory.Create(new AccessTokenDescriptor(
            UserNId,
            "alice",
            Tenant,
            ["role.operator"],
            Session,
            3,
            DateTimeOffset.UtcNow));
        return result.Token;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        InMemoryManagementStore store,
        IReadOnlyList<string> permissions,
        ILoginAuditQueryStore? auditStore = null)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISessionRevocationStore>();
                services.AddSingleton<ISessionRevocationStore>(new InMemoryRevocationStore());
                services.RemoveAll<IAuthorizationDataStore>();
                services.AddSingleton<IAuthorizationDataStore>(
                    new InMemoryAuthorizationDataStore(
                        new AuthorizationSnapshot(Tenant, UserNId, UserStatus.Active, 3, permissions)));
                services.RemoveAll<IPermissionCache>();
                services.AddSingleton<IPermissionCache>(new InMemoryPermissionCache());

                services.RemoveAll<IManagementStore>();
                services.AddSingleton<IManagementStore>(store);
                services.RemoveAll<IOperationAuditSink>();
                services.AddSingleton<IOperationAuditSink>(store);
                services.RemoveAll<ILoginAuditQueryStore>();
                services.AddSingleton<ILoginAuditQueryStore>(auditStore ?? new InMemoryLoginAuditQueryStore(new LoginAuditPage([], 0)));
                services.RemoveAll<IRefreshSessionStore>();
                services.AddSingleton<IRefreshSessionStore>(new InMemoryRefreshSessionStore());
            }));

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string? token = null,
        string? json = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static StoredUser StoredUser(string nId, string loginName, string name) => new(
        Guid.NewGuid(),
        Tenant,
        nId,
        loginName,
        name,
        "alice@example.com",
        null,
        UserStatus.Active,
        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        null,
        1,
        Guid.NewGuid(),
        []);

    private static User CreateAggregateUser(string nId)
        => User.Create(Tenant, nId, "alice", "Alice", "alice@example.com", null, "HASH:" + StrongPassword);

    private sealed class InMemoryManagementStore : IManagementStore, IOperationAuditSink
    {
        public StoredUserPage UserPage { get; init; } = new([], 0);
        public StoredRolePage RolePage { get; init; } = new([], 0);
        public StoredUser? StoredUser { get; set; }
        public User? UserAggregate { get; init; }
        public IReadOnlyList<Permission> AllPermissions { get; init; } = [];
        public List<OperationAuditEntry> Audits { get; } = [];

        public Task<StoredUserPage> QueryUsersAsync(UserListFilter filter, CancellationToken cancellationToken)
            => Task.FromResult(UserPage);

        public Task<StoredUser?> GetUserAsync(string userNId, CancellationToken cancellationToken)
            => Task.FromResult(StoredUser);

        public Task<User?> GetUserAggregateAsync(string userNId, CancellationToken cancellationToken)
            => Task.FromResult(UserAggregate);

        public Task<User?> GetUserAggregateIncludingDeletedAsync(string userNId, CancellationToken cancellationToken)
            => Task.FromResult(UserAggregate);

        public Task<bool> UserExistsByNIdAsync(string userNId, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<bool> LoginNameExistsAsync(string tenantNId, string loginName, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<StoredRolePage> QueryRolesAsync(RoleListFilter filter, CancellationToken cancellationToken)
            => Task.FromResult(RolePage);

        public Task<StoredRole?> GetRoleAsync(string roleNId, CancellationToken cancellationToken)
            => Task.FromResult<StoredRole?>(null);

        public Task<Role?> GetRoleAggregateAsync(string roleNId, CancellationToken cancellationToken)
            => Task.FromResult<Role?>(null);

        public Task<bool> RoleExistsByNIdAsync(string roleNId, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<IReadOnlyList<Role>> GetRolesByNIdsAsync(IReadOnlyCollection<string> roleNIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Role>>([]);

        public Task<IReadOnlyList<Role>> GetRolesByIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Role>>([]);

        public Task<IReadOnlyDictionary<Guid, string>> GetRoleNIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyList<Permission>> GetPermissionsByNIdsAsync(IReadOnlyCollection<string> permissionNIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Permission>>([]);

        public Task<IReadOnlyList<Permission>> GetPermissionsByIdsAsync(IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Permission>>([]);

        public Task<IReadOnlyList<Permission>> GetAllPermissionsAsync(CancellationToken cancellationToken)
            => Task.FromResult(AllPermissions);

        public Task<int> CountActiveRoleHoldersAsync(Guid roleId, string tenantNId, CancellationToken cancellationToken)
            => Task.FromResult(1);

        public Task<IReadOnlyList<string>> GetUserNIdsForRoleAsync(Guid roleId, string tenantNId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task AddUserAsync(User user, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken)
        {
            StoredUser = new StoredUser(
                user.Id,
                user.TenantNId,
                user.NId,
                user.LoginName,
                user.Name,
                user.Email,
                user.Phone,
                user.Status,
                user.CreatedOn,
                user.LastUpdatedOn,
                user.LastLoginOn,
                user.OptimisticVersion,
                user.ConcurrencyVersion,
                []);
            return Task.CompletedTask;
        }

        public Task UpdateUserAsync(
            User user,
            long expectedOptimisticVersion,
            Guid expectedConcurrencyVersion,
            IReadOnlyCollection<OutboxEnvelope> outboxEvents,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RestoreUserAsync(
            User user,
            long expectedOptimisticVersion,
            Guid expectedConcurrencyVersion,
            IReadOnlyCollection<OutboxEnvelope> outboxEvents,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AddRoleAsync(Role role, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UpdateRoleAsync(
            Role role,
            long expectedOptimisticVersion,
            Guid expectedConcurrencyVersion,
            IReadOnlyCollection<OutboxEnvelope> outboxEvents,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public async Task WriteAsync(OperationAuditEntry entry, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Audits.Add(entry);
        }
    }

    private sealed class InMemoryAuthorizationDataStore : IAuthorizationDataStore
    {
        public InMemoryAuthorizationDataStore(AuthorizationSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public AuthorizationSnapshot? Snapshot { get; }

        public Task<AuthorizationSnapshot?> GetSnapshotAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
            => Task.FromResult(Snapshot);
    }

    private sealed class InMemoryRevocationStore : ISessionRevocationStore
    {
        public Task RevokeAsync(string sessionNId, TimeSpan ttl, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<bool> IsRevokedAsync(string sessionNId, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class InMemoryPermissionCache : IPermissionCache
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

    private sealed class InMemoryRefreshSessionStore : IRefreshSessionStore
    {
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
    }

    private sealed class InMemoryLoginAuditQueryStore : ILoginAuditQueryStore
    {
        public InMemoryLoginAuditQueryStore(LoginAuditPage result)
        {
            Result = result;
        }

        public LoginAuditPage Result { get; }

        public Task<LoginAuditPage> QueryAsync(LoginAuditFilter filter, CancellationToken cancellationToken)
            => Task.FromResult(Result);
    }
}
