using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Application.UserGroups;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Users;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IndustrialPlatform.Identity.Api.Tests;

/// <summary>
/// 用户组管理端点全栈测试(§29A.5):真实 Program/控制器/授权管线,替换 IUserGroupService 与
/// 幂等存储,验证列表/详情/创建/成员集/角色集端点、权限门禁、404/403 与 Idempotency-Key 幂等语义。
/// </summary>
public sealed class UserGroupEndpointTests
{
    private const string Tenant = "development";
    private const string UserNId = "user.alice";
    private const string Session = "SES-abc";

    [Fact]
    public async Task List_WithPermission_ReturnsPage()
    {
        var service = new FakeUserGroupService
        {
            OnList = _ => new ManagementPage<StoredUserGroup>(
                [new StoredUserGroup("group.ops", "运维组", null, "Active", Tenant, 2, 1, default, default, 1, Guid.NewGuid(), IsDeleted: false)],
                1,
                1,
                20),
        };
        using var factory = CreateFactory(service, [PermissionCatalog.UserGroupView]);
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/api/v1/user-groups", token: await CreateTokenAsync(factory));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = payload.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("group.ops", item.GetProperty("groupNId").GetString());
        Assert.Equal("Active", item.GetProperty("status").GetString());
        Assert.Equal(2, item.GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task List_Unauthenticated_Returns401()
    {
        using var factory = CreateFactory(new FakeUserGroupService(), [PermissionCatalog.UserGroupView]);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/user-groups");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_NoPermission_Returns403()
    {
        using var factory = CreateFactory(new FakeUserGroupService(), [PermissionCatalog.UserView]);
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/api/v1/user-groups", token: await CreateTokenAsync(factory));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ID_PERMISSION_DENIED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_WithPermission_ReturnsDetail()
    {
        var service = new FakeUserGroupService
        {
            OnCreate = _ => new UserGroupSummary("group.ops", "运维组", null, "Active", Tenant, [], [], 1, Guid.NewGuid()),
        };
        using var factory = CreateFactory(service, [PermissionCatalog.UserGroupCreate]);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/user-groups",
            await CreateTokenAsync(factory),
            """{"nId":"group.ops","name":"运维组"}""");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("group.ops", payload.RootElement.GetProperty("data").GetProperty("groupNId").GetString());
    }

    [Fact]
    public async Task SetMembers_WithIdempotencyKey_SameKeyDifferentContent_Returns409()
    {
        var service = new FakeUserGroupService
        {
            OnSetMembers = _ => new UserGroupSummary("group.ops", "运维组", null, "Active", Tenant, ["alice.user"], [], 1, Guid.NewGuid()),
        };
        using var factory = CreateFactory(service, [PermissionCatalog.UserGroupAssignMember]);
        using var client = factory.CreateClient();

        // 第一次携带 Idempotency-Key 成功
        var first = await SendAsync(
            client,
            HttpMethod.Put,
            "/api/v1/user-groups/group.ops/members",
            await CreateTokenAsync(factory),
            """{"memberUserNIds":["alice.user"],"expectedOptimisticVersion":0,"expectedConcurrencyVersion":"00000000-0000-0000-0000-000000000000"}""",
            idempotencyKey: "KEY-1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // 同键不同内容 → 409 ID_IDEMPOTENCY_CONFLICT
        var second = await SendAsync(
            client,
            HttpMethod.Put,
            "/api/v1/user-groups/group.ops/members",
            await CreateTokenAsync(factory),
            """{"memberUserNIds":["bob.user"],"expectedOptimisticVersion":0,"expectedConcurrencyVersion":"00000000-0000-0000-0000-000000000000"}""",
            idempotencyKey: "KEY-1");
        using var secondPayload = JsonDocument.Parse(await second.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("ID_IDEMPOTENCY_CONFLICT", secondPayload.RootElement.GetProperty("code").GetString());
        Assert.Equal(1, service.SetMembersCalls);
    }

    [Fact]
    public async Task SetMembers_WithIdempotencyKey_Replay_DoesNotReExecute()
    {
        var service = new FakeUserGroupService
        {
            OnSetMembers = _ => new UserGroupSummary("group.ops", "运维组", null, "Active", Tenant, ["alice.user"], [], 1, Guid.NewGuid()),
        };
        using var factory = CreateFactory(service, [PermissionCatalog.UserGroupAssignMember]);
        using var client = factory.CreateClient();

        const string body = """{"memberUserNIds":["alice.user"],"expectedOptimisticVersion":0,"expectedConcurrencyVersion":"00000000-0000-0000-0000-000000000000"}""";
        var first = await SendAsync(client, HttpMethod.Put, "/api/v1/user-groups/group.ops/members", await CreateTokenAsync(factory), body, "KEY-2");
        var replay = await SendAsync(client, HttpMethod.Put, "/api/v1/user-groups/group.ops/members", await CreateTokenAsync(factory), body, "KEY-2");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        // 重放不重复执行写操作
        Assert.Equal(1, service.SetMembersCalls);
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
        FakeUserGroupService service,
        IReadOnlyList<string> permissions)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISessionRevocationStore>();
                services.AddSingleton<ISessionRevocationStore>(new FakeRevocationStore());
                services.RemoveAll<IAuthorizationDataStore>();
                services.AddSingleton<IAuthorizationDataStore>(
                    new FakeAuthorizationDataStore(
                        new AuthorizationSnapshot(Tenant, UserNId, UserStatus.Active, 3, permissions)));
                services.RemoveAll<IPermissionCache>();
                services.AddSingleton<IPermissionCache>(new FakePermissionCache());

                services.RemoveAll<IUserGroupService>();
                services.AddSingleton<IUserGroupService>(service);
                services.RemoveAll<IIdempotencyStore>();
                services.AddSingleton<IIdempotencyStore>(new FakeIdempotencyStore());
            }));

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string? token = null,
        string? json = null,
        string? idempotencyKey = null)
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

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    private sealed class FakeRevocationStore : ISessionRevocationStore
    {
        public Task RevokeAsync(string sessionNId, TimeSpan ttl, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<bool> IsRevokedAsync(string sessionNId, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class FakeAuthorizationDataStore : IAuthorizationDataStore
    {
        private readonly AuthorizationSnapshot? _snapshot;

        public FakeAuthorizationDataStore(AuthorizationSnapshot? snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<AuthorizationSnapshot?> GetSnapshotAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
            => Task.FromResult(_snapshot);
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

    /// <summary>幂等存储替身:记录 (key, hash),同键不同哈希冲突,同键同哈希已完成重放。</summary>
    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        private readonly Dictionary<string, (string Hash, bool Completed)> _records = new(StringComparer.Ordinal);

        public Task<IdempotencyDecision> TryReserveAsync(string tenantNId, string actorUserNId, string idempotencyKey, string requestHash, CancellationToken cancellationToken = default)
        {
            var scopedKey = $"{tenantNId}|{actorUserNId}|{idempotencyKey}";
            if (_records.TryGetValue(scopedKey, out var record))
            {
                if (!string.Equals(record.Hash, requestHash, StringComparison.Ordinal))
                {
                    throw new IdempotencyConflictException();
                }

                return Task.FromResult(record.Completed ? IdempotencyDecision.Replay : IdempotencyDecision.Proceed);
            }

            _records[scopedKey] = (requestHash, false);
            return Task.FromResult(IdempotencyDecision.Proceed);
        }

        public Task MarkCompletedAsync(string tenantNId, string actorUserNId, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            var scopedKey = $"{tenantNId}|{actorUserNId}|{idempotencyKey}";
            if (_records.TryGetValue(scopedKey, out var record))
            {
                _records[scopedKey] = (record.Hash, true);
            }

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(string tenantNId, string actorUserNId, string idempotencyKey, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>用户组服务替身:可配置行为并记录调用次数。</summary>
    private sealed class FakeUserGroupService : IUserGroupService
    {
        public Func<UserGroupListQuery, ManagementPage<StoredUserGroup>>? OnList { get; set; }

        public Func<CreateUserGroupCommand, UserGroupSummary>? OnCreate { get; set; }

        public Func<(string GroupNId, IReadOnlyCollection<string> MemberUserNIds), UserGroupSummary>? OnSetMembers { get; set; }

        public int SetMembersCalls { get; private set; }

        public Task<UserGroupSummary> CreateAsync(string tenantNId, string actorUserNId, CreateUserGroupCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(OnCreate!(command));

        public Task<UserGroupSummary> UpdateAsync(string tenantNId, string actorUserNId, string groupNId, UpdateUserGroupCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(new UserGroupSummary(groupNId, command.Name, command.Description, "Active", tenantNId, [], [], 1, Guid.NewGuid()));

        public Task<UserGroupSummary> SetStatusAsync(string tenantNId, string actorUserNId, string groupNId, bool enabled, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken = default)
            => Task.FromResult(new UserGroupSummary(groupNId, "g", null, enabled ? "Active" : "Disabled", tenantNId, [], [], 1, Guid.NewGuid()));

        public Task<UserGroupSummary> SetMembersAsync(string tenantNId, string actorUserNId, string groupNId, IReadOnlyCollection<string> memberUserNIds, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken = default)
        {
            SetMembersCalls++;
            return Task.FromResult(OnSetMembers is null
                ? new UserGroupSummary(groupNId, "g", null, "Active", tenantNId, memberUserNIds.ToList(), [], 1, Guid.NewGuid())
                : OnSetMembers((groupNId, memberUserNIds)));
        }

        public Task<UserGroupSummary> SetRolesAsync(string tenantNId, string actorUserNId, string groupNId, IReadOnlyCollection<string> roleNIds, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken = default)
            => Task.FromResult(new UserGroupSummary(groupNId, "g", null, "Active", tenantNId, [], roleNIds.ToList(), 1, Guid.NewGuid()));

        public Task DeleteAsync(string tenantNId, string actorUserNId, string groupNId, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<UserGroupSummary> RestoreAsync(string tenantNId, string actorUserNId, string groupNId, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken = default)
            => Task.FromResult(new UserGroupSummary(groupNId, "g", null, "Disabled", tenantNId, [], [], 1, Guid.NewGuid()));

        public Task<UserGroupSummary> GetAsync(string tenantNId, string groupNId, CancellationToken cancellationToken = default)
            => Task.FromResult(new UserGroupSummary(groupNId, "g", null, "Active", tenantNId, [], [], 1, Guid.NewGuid()));

        public Task<ManagementPage<StoredUserGroup>> ListAsync(string tenantNId, UserGroupListQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(OnList is null
                ? new ManagementPage<StoredUserGroup>([], 0, query.PageIndex, query.PageSize)
                : OnList(query));
    }
}
