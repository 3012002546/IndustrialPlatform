using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Contracts.Management;
using Identities = IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IndustrialPlatform.Identity.Application.Tests;

/// <summary>
/// 用户管理用例测试(§16.1/§19.2):创建/修改/状态/角色分配/密码重置的校验与守卫,
/// 乐观并发映射,租户隔离,操作审计与权限缓存失效(best-effort)。
/// </summary>
public sealed class UserManagementServiceTests
{
    private const string Tenant = "development";
    private const string Actor = "actor.admin";
    private const string StrongPassword = "Ab1!defghijk";

    private readonly FakeManagementStore _store = new();
    private readonly FakeUserGroupStore _groupStore = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeRefreshSessionStore _refreshStore = new();
    private readonly FakePermissionCache _cache = new();
    private readonly FakeAuditSink _auditSink = new();

    private UserManagementService CreateService() => new(
        _store,
        _groupStore,
        _hasher,
        new FakeTemporaryPasswordGenerator(),
        _refreshStore,
        _cache,
        _auditSink,
        new AllowSystemAdminAuthorization(),
        NullLogger<UserManagementService>.Instance);

    private User SeedUser(string nId = "alice.user", string loginName = "alice", string name = "Alice")
    {
        var user = User.Create(Tenant, nId, loginName, name, "alice@example.com", null, _hasher.Hash(StrongPassword));
        _store.Seed(user);
        return user;
    }

    private Role SeedRole(string nId = "role.operator", bool isSystem = false)
    {
        var role = Role.Create(Tenant, nId, isSystem ? "System Admin" : "Operator", null, isSystem);
        _store.Seed(role);
        return role;
    }

    private static (long Optimistic, Guid Concurrency) Versions(User user)
        => (user.OptimisticVersion, user.ConcurrencyVersion);

    [Fact]
    public async Task CreateAsync_WithFullRequest_ReturnsSummaryAndAudits()
    {
        var role = SeedRole("role.operator");
        var service = CreateService();

        var result = await service.CreateAsync(
            Tenant,
            Actor,
            new CreateUserRequest("alice.user", "alice", "Alice", "alice@example.com", "13800000000", ["role.operator"]),
            CancellationToken.None);

        Assert.Equal("alice.user", result.User.UserNId);
        Assert.Equal("alice", result.User.LoginName);
        Assert.Equal("Alice", result.User.Name);
        Assert.Equal("Active", result.User.Status);
        Assert.Equal(Tenant, result.User.TenantNId);
        Assert.Equal(["role.operator"], result.User.DirectRoleNIds);
        Assert.True(result.User.MustChangePassword);
        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));

        var audit = Assert.Single(_auditSink.Entries);
        Assert.Equal(OperationAction.UserCreate, audit.Action);
        Assert.Equal(OperationObjectType.User, audit.ObjectType);
        Assert.Equal(Actor, audit.ActorUserNId);
        Assert.Equal("alice.user", audit.ObjectNId);
        Assert.Null(audit.BeforeSummary);
        Assert.NotNull(audit.AfterSummary);
        Assert.Contains("loginName=alice", audit.AfterSummary);
        Assert.DoesNotContain("HASH:", audit.AfterSummary);
    }

    [Fact]
    public async Task CreateAsync_GeneratedNId_WhenAbsent()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            Tenant,
            Actor,
            new CreateUserRequest(null, "alice", "Alice", null, null, null),
            CancellationToken.None);

        Assert.StartsWith("USR-", result.User.UserNId, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
    }

    [Fact]
    public async Task CreateAsync_NullLoginName_ThrowsValidation()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(Tenant, Actor, new CreateUserRequest(null, null, "Alice", null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ReservedLoginName_ThrowsUserLoginNameReserved()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<UserLoginNameReservedException>(() =>
            service.CreateAsync(Tenant, Actor, new CreateUserRequest("bob.user", "admin", "Bob", null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_NIdConflict_ThrowsUserNIdConflict()
    {
        SeedUser();
        var service = CreateService();

        await Assert.ThrowsAsync<UserNIdConflictException>(() =>
            service.CreateAsync(Tenant, Actor, new CreateUserRequest("alice.user", "bob", "Bob", null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_LoginNameConflict_ThrowsUserLoginNameConflict()
    {
        SeedUser();
        var service = CreateService();

        await Assert.ThrowsAsync<UserLoginNameConflictException>(() =>
            service.CreateAsync(Tenant, Actor, new CreateUserRequest("bob.user", "alice", "Bob", null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_InvalidRole_ThrowsBusinessRuleViolation()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.CreateAsync(Tenant, Actor, new CreateUserRequest("alice.user", "alice", "Alice", null, null, ["role.missing"]), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_CrossTenantRole_ThrowsBusinessRuleViolation()
    {
        var foreignRole = Role.Create("other", "role.foreign", "Foreign", null, isSystem: false);
        _store.Seed(foreignRole);
        var service = CreateService();

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.CreateAsync(Tenant, Actor, new CreateUserRequest("alice.user", "alice", "Alice", null, null, ["role.foreign"]), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_AuditSinkFailure_DoesNotBlockCreate()
    {
        _auditSink.ThrowOnWrite = true;
        var service = CreateService();

        var result = await service.CreateAsync(
            Tenant,
            Actor,
            new CreateUserRequest("alice.user", "alice", "Alice", null, null, null),
            CancellationToken.None);

        Assert.Equal("alice.user", result.User.UserNId);
        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
    }

    [Fact]
    public async Task UpdateAsync_ChangesProfileAndLoginName_Succeeds()
    {
        var user = SeedUser();
        var service = CreateService();
        var (optimistic, concurrency) = Versions(user);

        var result = await service.UpdateAsync(
            Tenant,
            Actor,
            "alice.user",
            new UpdateUserRequest("alice.updated", "Alicia", null, null, optimistic, concurrency),
            CancellationToken.None);

        Assert.Equal("alice.updated", result.LoginName);
        Assert.Equal("Alicia", result.Name);
        Assert.Equal(OperationAction.UserUpdate, Assert.Single(_auditSink.Entries).Action);
        Assert.Equal(("development", "alice.user"), Assert.Single(_cache.Invalidated));
    }

    [Fact]
    public async Task UpdateAsync_LoginNameConflict_ThrowsUserLoginNameConflict()
    {
        var alice = SeedUser();
        SeedUser("bob.user", "bob", "Bob");
        var service = CreateService();
        var (optimistic, concurrency) = Versions(alice);

        await Assert.ThrowsAsync<UserLoginNameConflictException>(() =>
            service.UpdateAsync(Tenant, Actor, "alice.user", new UpdateUserRequest("bob", null, null, null, optimistic, concurrency), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_ConcurrencyConflict_ThrowsConcurrencyConflict()
    {
        SeedUser();
        var service = CreateService();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.UpdateAsync(Tenant, Actor, "alice.user", new UpdateUserRequest(null, "Alicia", null, null, -1, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_CrossTenant_ThrowsResourceNotFound()
    {
        SeedUser();
        var service = CreateService();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.UpdateAsync("other", Actor, "alice.user", new UpdateUserRequest(null, "Alicia", null, null, 0, Guid.Empty), CancellationToken.None));
    }

    [Fact]
    public async Task SetStatusAsync_DisableSelf_ThrowsBusinessRuleViolation()
    {
        var user = SeedUser("alice.user", "alice", "Alice");
        var service = CreateService();
        var (optimistic, concurrency) = Versions(user);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.SetStatusAsync(Tenant, "alice.user", "alice.user", new SetUserStatusRequest(false, optimistic, concurrency), CancellationToken.None));
    }

    [Fact]
    public async Task SetStatusAsync_DisableLastSystemAdmin_ThrowsBusinessRuleViolation()
    {
        var sysRole = SeedRole("role.sysadmin", isSystem: true);
        var user = SeedUser("alice.user", "alice", "Alice");
        user.AssignRole(sysRole);
        var service = CreateService();
        var (optimistic, concurrency) = Versions(user);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.SetStatusAsync(Tenant, Actor, "alice.user", new SetUserStatusRequest(false, optimistic, concurrency), CancellationToken.None));
    }

    [Fact]
    public async Task SetStatusAsync_DisableNormalUser_SucceedsAndInvalidatesCache()
    {
        var user = SeedUser();
        var service = CreateService();
        var (optimistic, concurrency) = Versions(user);

        var result = await service.SetStatusAsync(
            Tenant,
            Actor,
            "alice.user",
            new SetUserStatusRequest(false, optimistic, concurrency),
            CancellationToken.None);

        Assert.Equal("Disabled", result.Status);
        Assert.Equal(OperationAction.UserDisable, Assert.Single(_auditSink.Entries).Action);
        Assert.Equal(("development", "alice.user"), Assert.Single(_cache.Invalidated));
    }

    [Fact]
    public async Task SetStatusAsync_Enable_Succeeds()
    {
        var user = SeedUser();
        var (optimistic, concurrency) = Versions(user);
        user.Disable();
        var service = CreateService();

        var result = await service.SetStatusAsync(
            Tenant,
            Actor,
            "alice.user",
            new SetUserStatusRequest(true, optimistic, concurrency),
            CancellationToken.None);

        Assert.Equal("Active", result.Status);
        Assert.Equal(OperationAction.UserEnable, Assert.Single(_auditSink.Entries).Action);
        Assert.Empty(_cache.Invalidated);
    }

    [Fact]
    public async Task SetStatusAsync_ConcurrencyConflict_Throws()
    {
        SeedUser();
        var service = CreateService();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.SetStatusAsync(Tenant, Actor, "alice.user", new SetUserStatusRequest(false, -1, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task AssignRolesAsync_AssignAndRemove_Succeeds()
    {
        var user = SeedUser();
        SeedRole("role.a");
        SeedRole("role.b");
        var service = CreateService();
        var (optimistic, concurrency) = Versions(user);

        var result = await service.AssignRolesAsync(
            Tenant,
            Actor,
            "alice.user",
            new AssignUserRolesRequest(["role.b"], optimistic, concurrency),
            CancellationToken.None);

        Assert.Equal(["role.b"], result.DirectRoleNIds);
        Assert.Equal(OperationAction.UserAssignRoles, Assert.Single(_auditSink.Entries).Action);
        Assert.Single(_cache.Invalidated);
    }

    [Fact]
    public async Task AssignRolesAsync_InvalidRole_ThrowsBusinessRuleViolation()
    {
        var user = SeedUser();
        var service = CreateService();
        var (optimistic, concurrency) = Versions(user);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.AssignRolesAsync(Tenant, Actor, "alice.user", new AssignUserRolesRequest(["role.missing"], optimistic, concurrency), CancellationToken.None));
    }

    [Fact]
    public async Task AssignRolesAsync_RemoveLastSystemAdmin_ThrowsBusinessRuleViolation()
    {
        var sysRole = SeedRole("role.sysadmin", isSystem: true);
        var user = SeedUser();
        user.AssignRole(sysRole);
        var service = CreateService();
        var (optimistic, concurrency) = Versions(user);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.AssignRolesAsync(Tenant, Actor, "alice.user", new AssignUserRolesRequest([], optimistic, concurrency), CancellationToken.None));
    }

    [Fact]
    public async Task ResetPasswordAsync_RevokesAllSessionsAndAudits()
    {
        var user = SeedUser();
        var service = CreateService();

        var result = await service.ResetPasswordAsync(
            Tenant,
            Actor,
            "alice.user",
            new ResetPasswordRequest(),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
        var revoked = Assert.Single(_refreshStore.RevokedAll);
        Assert.Equal(user.Id, revoked.UserId);
        Assert.Equal("admin_reset", revoked.Reason);
        Assert.Equal(OperationAction.UserResetPassword, Assert.Single(_auditSink.Entries).Action);
        Assert.Single(_cache.Invalidated);
    }

    [Fact]
    public async Task ListAsync_OverridesTenantAndMapsPage()
    {
        SeedUser();
        SeedUser("bob.user", "bob", "Bob");
        var service = CreateService();

        var page = await service.ListAsync(
            Tenant,
            new UserListFilter("ignored", null, null, null, null, 1, 20),
            CancellationToken.None);

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(1, page.PageIndex);
        Assert.Equal(20, page.PageSize);
    }

    [Fact]
    public async Task GetAsync_CrossTenant_ThrowsResourceNotFound()
    {
        SeedUser();
        var service = CreateService();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GetAsync("other", "alice.user", CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_Unknown_ThrowsResourceNotFound()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GetAsync(Tenant, "missing.user", CancellationToken.None));
    }

    [Fact]
    public async Task ResetPasswordAsync_AuditSinkFailure_StillRevokes()
    {
        var user = SeedUser();
        _auditSink.ThrowOnWrite = true;
        var service = CreateService();

        var result = await service.ResetPasswordAsync(
            Tenant,
            Actor,
            "alice.user",
            new ResetPasswordRequest(),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
        var revoked = Assert.Single(_refreshStore.RevokedAll);
        Assert.Equal(user.Id, revoked.UserId);
    }

    // ---- 安全删除/恢复(§29A.3)----

    [Fact]
    public async Task DeleteAsync_DeletesTombstone_RevokesSessionsAndInvalidatesCache()
    {
        var user = SeedUser();
        var role = SeedRole();
        user.AssignRole(role);
        _store.Seed(user); // 变更后重新登记持久化版本(AssignRole 已推进)
        var service = CreateService();

        await service.DeleteAsync(
            Tenant,
            Actor,
            "alice.user",
            new DeleteUserRequest("违规账号", user.OptimisticVersion, user.ConcurrencyVersion),
            CancellationToken.None);

        Assert.True(user.IsDeleted);
        Assert.Equal(1, user.AuthVersion);
        Assert.All(user.UserRoles, r => Assert.True(r.IsDeleted));
        // 全部刷新会话撤销 + 版本化权限缓存失效
        Assert.Contains(_refreshStore.RevokedAll, r => r.UserId == user.Id && r.Reason == "user_deleted");
        Assert.Contains(_cache.Invalidated, i => i.UserNId == "alice.user");
        // 删除事件进入 Outbox(同事务)
        Assert.Contains(_store.Outbox, e => e.EventType == "Identity.UserDeleted.v1");
        // 操作审计
        Assert.Contains(_auditSink.Entries, e => e.Action == OperationAction.UserDelete && e.ObjectNId == "alice.user");
    }

    [Fact]
    public async Task DeleteAsync_SelfDeletion_Throws()
    {
        var user = SeedUser();
        var service = CreateService();

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.DeleteAsync(Tenant, "alice.user", "alice.user", new DeleteUserRequest("x", user.OptimisticVersion, user.ConcurrencyVersion), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_ReservedAdmin_Throws()
    {
        var admin = User.Create(Tenant, "ADMIN", "admin", "管理员", null, null, _hasher.Hash(StrongPassword));
        _store.Seed(admin);
        var service = CreateService();

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.DeleteAsync(Tenant, Actor, "ADMIN", new DeleteUserRequest("x", admin.OptimisticVersion, admin.ConcurrencyVersion), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_LastSystemAdminDirect_Throws()
    {
        var sysAdmin = SeedRole("SYSTEM_ADMIN", isSystem: true);
        var user = SeedUser();
        user.AssignRole(sysAdmin);
        _store.Seed(user);
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<LastAdminRequiredException>(() =>
            service.DeleteAsync(Tenant, Actor, "alice.user", new DeleteUserRequest("x", user.OptimisticVersion, user.ConcurrencyVersion), CancellationToken.None));
        Assert.Equal("ID_LAST_ADMIN_REQUIRED", ex.Code);
    }

    [Fact]
    public async Task DeleteAsync_LastSystemAdminViaGroupInherited_Throws()
    {
        var sysAdmin = SeedRole("SYSTEM_ADMIN", isSystem: true);
        var user = SeedUser();
        // 用户唯一管理员路径来自组继承(§29A.3 组继承路径守卫)
        _groupStore.SeedGroupInheritedRole(user.Id, sysAdmin.Id);
        var service = CreateService();

        await Assert.ThrowsAsync<LastAdminRequiredException>(() =>
            service.DeleteAsync(Tenant, Actor, "alice.user", new DeleteUserRequest("x", user.OptimisticVersion, user.ConcurrencyVersion), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_SecondSystemAdminExists_Allowed()
    {
        var sysAdmin = SeedRole("SYSTEM_ADMIN", isSystem: true);
        var alice = SeedUser();
        alice.AssignRole(sysAdmin);
        _store.Seed(alice);
        var bob = User.Create(Tenant, "bob.user", "bob", "Bob", null, null, _hasher.Hash(StrongPassword));
        bob.AssignRole(sysAdmin);
        _store.Seed(bob);
        var service = CreateService();

        await service.DeleteAsync(
            Tenant,
            Actor,
            "alice.user",
            new DeleteUserRequest("离职", alice.OptimisticVersion, alice.ConcurrencyVersion),
            CancellationToken.None);

        Assert.True(alice.IsDeleted);
    }

    [Fact]
    public async Task RestoreAsync_RestoresTombstoneAsDisabled_NoAuthorizationRestored()
    {
        var user = SeedUser();
        var service = CreateService();
        await service.DeleteAsync(Tenant, Actor, "alice.user", new DeleteUserRequest("x", user.OptimisticVersion, user.ConcurrencyVersion), CancellationToken.None);
        _store.Outbox.Clear();

        var tombstone = _store.GetSeededUser("alice.user")!;
        var summary = await service.RestoreAsync(
            Tenant,
            Actor,
            "alice.user",
            new RestoreUserRequest("误删恢复", tombstone.OptimisticVersion, tombstone.ConcurrencyVersion),
            CancellationToken.None);

        // 恢复后保持 Disabled,且不恢复授权关系(§29A.3)
        Assert.Equal("Disabled", summary.Status);
        Assert.False(tombstone.IsDeleted);
        Assert.All(tombstone.UserRoles, r => Assert.True(r.IsDeleted));
        // 恢复事件进入 Outbox + 操作审计
        Assert.Contains(_store.Outbox, e => e.EventType == "Identity.UserRestored.v1");
        Assert.Contains(_auditSink.Entries, e => e.Action == OperationAction.UserRestore && e.ObjectNId == "alice.user");
    }

    [Fact]
    public async Task RestoreAsync_ActiveUser_Throws()
    {
        SeedUser();
        var service = CreateService();
        var user = _store.GetSeededUser("alice.user")!;

        await Assert.ThrowsAsync<UserDeletedException>(() =>
            service.RestoreAsync(Tenant, Actor, "alice.user", new RestoreUserRequest("x", user.OptimisticVersion, user.ConcurrencyVersion), CancellationToken.None));
    }

    private sealed class FakeManagementStore : IManagementStore
    {
        private readonly Dictionary<string, User> _usersByNormalized = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, User> _usersById = new();
        private readonly Dictionary<string, Role> _rolesByNormalized = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, Role> _rolesById = new();
        private readonly Dictionary<string, Permission> _permissionsByNormalized = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, Permission> _permissionsById = new();

        // 已持久化版本快照:与真实仓储一致,期望版本与最近一次成功写入的版本比较,
        // 而不是与聚合当前(可能已被业务修改 Touch 递增)的版本比较。
        private readonly Dictionary<Guid, (long OptimisticVersion, Guid ConcurrencyVersion)> _persistedVersions = new();

        /// <summary>写入接口收到的 Outbox 信封(用于删除/恢复事件原子性断言)。</summary>
        public List<OutboxEnvelope> Outbox { get; } = [];

        public void Seed(User user)
        {
            _usersByNormalized[user.NormalizedNId] = user;
            _usersById[user.Id] = user;
            _persistedVersions[user.Id] = (user.OptimisticVersion, user.ConcurrencyVersion);
        }

        public void Seed(Role role)
        {
            _rolesByNormalized[Identities.NId.Create(role.NId).Normalized] = role;
            _rolesById[role.Id] = role;
            _persistedVersions[role.Id] = (role.OptimisticVersion, role.ConcurrencyVersion);
        }

        public void Seed(Permission permission)
        {
            _permissionsByNormalized[Identities.NId.Create(permission.NId).Normalized] = permission;
            _permissionsById[permission.Id] = permission;
            _persistedVersions[permission.Id] = (permission.OptimisticVersion, permission.ConcurrencyVersion);
        }

        public User? GetSeededUser(string nId)
            => _usersByNormalized.TryGetValue(Identities.NId.Create(nId).Normalized, out var user) ? user : null;

        public Task<StoredUserPage> QueryUsersAsync(UserListFilter filter, CancellationToken cancellationToken)
        {
            var query = _usersByNormalized.Values
                .Where(u => u.TenantNId == filter.TenantNId && !u.IsDeleted)
                .AsEnumerable();
            if (!string.IsNullOrWhiteSpace(filter.NId))
            {
                query = query.Where(u => u.NId.Contains(filter.NId.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(filter.LoginName))
            {
                query = query.Where(u => u.LoginName.Contains(filter.LoginName.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(filter.Name))
            {
                query = query.Where(u => u.Name.Contains(filter.Name.Trim()));
            }

            if (filter.Status is { } status)
            {
                query = query.Where(u => u.Status == status);
            }

            var ordered = query.OrderByDescending(u => u.CreatedOn).ToList();
            var items = ordered
                .Skip((filter.PageIndex - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(ToStoredUser)
                .ToList();
            return Task.FromResult(new StoredUserPage(items, ordered.Count));
        }

        public Task<StoredUser?> GetUserAsync(string userNId, CancellationToken cancellationToken)
        {
            var user = GetSeededUser(userNId);
            return Task.FromResult(user is { IsDeleted: false } ? ToStoredUser(user) : null);
        }

        public Task<User?> GetUserAggregateAsync(string userNId, CancellationToken cancellationToken)
        {
            var user = GetSeededUser(userNId);
            return Task.FromResult(user is { IsDeleted: false } ? user : null);
        }

        public Task<User?> GetUserAggregateIncludingDeletedAsync(string userNId, CancellationToken cancellationToken)
        {
            var user = GetSeededUser(userNId);
            return Task.FromResult(user);
        }

        public Task<bool> UserExistsByNIdAsync(string userNId, CancellationToken cancellationToken)
            => Task.FromResult(GetSeededUser(userNId) is not null);

        public Task<bool> LoginNameExistsAsync(string tenantNId, string loginName, CancellationToken cancellationToken)
            => Task.FromResult(_usersByNormalized.Values.Any(u =>
                u.TenantNId == tenantNId && !u.IsDeleted && string.Equals(u.NormalizedLoginName, loginName.Trim(), StringComparison.OrdinalIgnoreCase)));

        public Task<StoredRolePage> QueryRolesAsync(RoleListFilter filter, CancellationToken cancellationToken)
        {
            var query = _rolesByNormalized.Values
                .Where(r => r.TenantNId == filter.TenantNId && !r.IsDeleted)
                .AsEnumerable();
            if (!string.IsNullOrWhiteSpace(filter.NId))
            {
                query = query.Where(r => r.NId.Contains(filter.NId.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(filter.Name))
            {
                query = query.Where(r => r.Name.Contains(filter.Name.Trim()));
            }

            var ordered = query.OrderByDescending(r => r.CreatedOn).ToList();
            var items = ordered
                .Skip((filter.PageIndex - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .Select(ToStoredRole)
                .ToList();
            return Task.FromResult(new StoredRolePage(items, ordered.Count));
        }

        public Task<StoredRole?> GetRoleAsync(string roleNId, CancellationToken cancellationToken)
        {
            var role = GetSeededRole(roleNId);
            return Task.FromResult(role is { IsDeleted: false } ? ToStoredRole(role) : null);
        }

        public Task<Role?> GetRoleAggregateAsync(string roleNId, CancellationToken cancellationToken)
        {
            var role = GetSeededRole(roleNId);
            return Task.FromResult(role is { IsDeleted: false } ? role : null);
        }

        public Task<bool> RoleExistsByNIdAsync(string roleNId, CancellationToken cancellationToken)
            => Task.FromResult(GetSeededRole(roleNId) is not null);

        public Task<IReadOnlyList<Role>> GetRolesByNIdsAsync(IReadOnlyCollection<string> roleNIds, CancellationToken cancellationToken)
        {
            var roles = roleNIds
                .Select(n => GetSeededRole(n))
                .Where(r => r is { IsDeleted: false })
                .Cast<Role>()
                .ToList();
            return Task.FromResult<IReadOnlyList<Role>>(roles);
        }

        public Task<IReadOnlyList<Role>> GetRolesByIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
        {
            var roles = roleIds
                .Where(id => _rolesById.TryGetValue(id, out var r) && !r.IsDeleted)
                .Select(id => _rolesById[id])
                .ToList();
            return Task.FromResult<IReadOnlyList<Role>>(roles);
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetRoleNIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                roleIds.Where(id => _rolesById.TryGetValue(id, out var r) && !r.IsDeleted).ToDictionary(id => id, id => _rolesById[id].NId));

        public Task<IReadOnlyList<Permission>> GetPermissionsByNIdsAsync(IReadOnlyCollection<string> permissionNIds, CancellationToken cancellationToken)
        {
            var permissions = permissionNIds
                .Select(GetSeededPermission)
                .Where(p => p is { IsDeleted: false })
                .Cast<Permission>()
                .ToList();
            return Task.FromResult<IReadOnlyList<Permission>>(permissions);
        }

        public Task<IReadOnlyList<Permission>> GetPermissionsByIdsAsync(IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken)
        {
            var permissions = permissionIds
                .Where(id => _permissionsById.TryGetValue(id, out var p) && !p.IsDeleted)
                .Select(id => _permissionsById[id])
                .ToList();
            return Task.FromResult<IReadOnlyList<Permission>>(permissions);
        }

        public Task<IReadOnlyList<Permission>> GetAllPermissionsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Permission>>(
                _permissionsByNormalized.Values.Where(p => !p.IsDeleted).OrderBy(p => p.NormalizedNId, StringComparer.Ordinal).ToList());

        public Task<int> CountActiveRoleHoldersAsync(Guid roleId, string tenantNId, CancellationToken cancellationToken)
            => Task.FromResult(_usersByNormalized.Values.Count(u =>
                u.TenantNId == tenantNId && !u.IsDeleted &&
                u.UserRoles.Any(ur => ur.RoleId == roleId && !ur.IsDeleted && !ur.RoleIsDeleted)));

        public Task<IReadOnlyList<string>> GetUserNIdsForRoleAsync(Guid roleId, string tenantNId, CancellationToken cancellationToken)
        {
            var nIds = _usersByNormalized.Values
                .Where(u => u.TenantNId == tenantNId && !u.IsDeleted &&
                    u.UserRoles.Any(ur => ur.RoleId == roleId && !ur.IsDeleted && !ur.RoleIsDeleted))
                .Select(u => u.NId)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(nIds);
        }

        public Task AddUserAsync(User user, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken)
        {
            Seed(user);
            return Task.CompletedTask;
        }

        public Task UpdateUserAsync(
            User user,
            long expectedOptimisticVersion,
            Guid expectedConcurrencyVersion,
            IReadOnlyCollection<OutboxEnvelope> outboxEvents,
            CancellationToken cancellationToken)
        {
            if (!_persistedVersions.TryGetValue(user.Id, out var persisted))
            {
                throw new ConcurrencyException("用户不存在");
            }

            if (persisted.OptimisticVersion != expectedOptimisticVersion || persisted.ConcurrencyVersion != expectedConcurrencyVersion)
            {
                throw new ConcurrencyException("乐观并发冲突");
            }

            _usersByNormalized[user.NormalizedNId] = user;
            _usersById[user.Id] = user;
            _persistedVersions[user.Id] = (user.OptimisticVersion, user.ConcurrencyVersion);
            Outbox.AddRange(outboxEvents);
            return Task.CompletedTask;
        }

        public Task RestoreUserAsync(
            User user,
            long expectedOptimisticVersion,
            Guid expectedConcurrencyVersion,
            IReadOnlyCollection<OutboxEnvelope> outboxEvents,
            CancellationToken cancellationToken)
        {
            if (!_persistedVersions.TryGetValue(user.Id, out var persisted))
            {
                throw new ConcurrencyException("用户不存在");
            }

            if (persisted.OptimisticVersion != expectedOptimisticVersion || persisted.ConcurrencyVersion != expectedConcurrencyVersion)
            {
                throw new ConcurrencyException("乐观并发冲突");
            }

            _usersByNormalized[user.NormalizedNId] = user;
            _usersById[user.Id] = user;
            _persistedVersions[user.Id] = (user.OptimisticVersion, user.ConcurrencyVersion);
            Outbox.AddRange(outboxEvents);
            return Task.CompletedTask;
        }

        public Task AddRoleAsync(Role role, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken)
        {
            Seed(role);
            return Task.CompletedTask;
        }

        public Task UpdateRoleAsync(
            Role role,
            long expectedOptimisticVersion,
            Guid expectedConcurrencyVersion,
            IReadOnlyCollection<OutboxEnvelope> outboxEvents,
            CancellationToken cancellationToken)
        {
            if (!_persistedVersions.TryGetValue(role.Id, out var persisted))
            {
                throw new ConcurrencyException("角色不存在");
            }

            if (persisted.OptimisticVersion != expectedOptimisticVersion || persisted.ConcurrencyVersion != expectedConcurrencyVersion)
            {
                throw new ConcurrencyException("乐观并发冲突");
            }

            _rolesByNormalized[Identities.NId.Create(role.NId).Normalized] = role;
            _rolesById[role.Id] = role;
            _persistedVersions[role.Id] = (role.OptimisticVersion, role.ConcurrencyVersion);
            return Task.CompletedTask;
        }

        private Role? GetSeededRole(string nId)
        {
            var normalized = Identities.NId.Create(nId).Normalized;
            return _rolesByNormalized.TryGetValue(normalized, out var role) ? role : null;
        }

        private Permission? GetSeededPermission(string nId)
        {
            var normalized = Identities.NId.Create(nId).Normalized;
            return _permissionsByNormalized.TryGetValue(normalized, out var permission) ? permission : null;
        }

        private StoredUser ToStoredUser(User u)
        {
            var roleNIds = u.UserRoles
                .Where(ur => !ur.IsDeleted && !ur.RoleIsDeleted)
                .Select(ur => _rolesById.TryGetValue(ur.RoleId, out var role) ? role.NId : null)
                .Where(n => n is not null)
                .Select(n => n!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            return new StoredUser(
                u.Id,
                u.TenantNId,
                u.NId,
                u.LoginName,
                u.Name,
                u.Email,
                u.Phone,
                u.Status,
                u.CreatedOn,
                u.LastUpdatedOn,
                u.LastLoginOn,
                u.MustChangePassword,
                roleNIds,
                [],
                [],
                u.OptimisticVersion,
                u.ConcurrencyVersion,
                u.IsDeleted);
        }

        private StoredRole ToStoredRole(Role r)
        {
            var permissionNIds = r.Permissions
                .Where(p => !p.IsDeleted && !p.PermissionIsDeleted)
                .Select(p => _permissionsById.TryGetValue(p.PermissionId, out var permission) ? permission.NId : null)
                .Where(n => n is not null)
                .Select(n => n!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            return new StoredRole(
                r.Id,
                r.TenantNId,
                r.NId,
                r.Name,
                r.Description,
                r.IsSystem,
                r.CreatedOn,
                r.LastUpdatedOn,
                r.OptimisticVersion,
                r.ConcurrencyVersion,
                permissionNIds);
        }
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => "HASH:" + password;

        public bool Verify(string passwordHash, string password) => passwordHash == "HASH:" + password;

        public bool NeedsRehash(string passwordHash) => false;
    }

    private sealed class FakeRefreshSessionStore : IRefreshSessionStore
    {
        public List<(Guid UserId, string Reason)> RevokedAll { get; } = [];

        public Task<StoredRefreshSession?> FindByRawTokenAsync(string rawToken, CancellationToken cancellationToken)
            => Task.FromResult<StoredRefreshSession?>(null);

        public Task AddAsync(NewRefreshSession session, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<RefreshRotationStatus> RotateAsync(Guid currentSessionId, NewRefreshSession replacement, CancellationToken cancellationToken)
            => Task.FromResult(RefreshRotationStatus.Invalid);

        public Task RevokeFamilyAsync(string familyNId, string reason, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken)
        {
            RevokedAll.Add((userId, reason));
            return Task.CompletedTask;
        }
    }

    private sealed class FakePermissionCache : IPermissionCache
    {
        public List<(string TenantNId, string UserNId)> Invalidated { get; } = [];
        public bool ThrowOnInvalidate { get; set; }

        public Task<UserSecurityCacheEntry?> TryGetUserSnapshotAsync(string tenantNId, string userNId, int authVersion, CancellationToken cancellationToken)
            => Task.FromResult<UserSecurityCacheEntry?>(null);

        public Task<IReadOnlyList<string>?> TryGetPermissionsAsync(string tenantNId, string userNId, int authVersion, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>?>(null);

        public Task SetAsync(AuthorizationSnapshot snapshot, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public async Task InvalidateAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
        {
            if (ThrowOnInvalidate)
            {
                await Task.Yield();
                throw new InvalidOperationException("cache unavailable");
            }

            Invalidated.Add((tenantNId, userNId));
        }
    }

    private sealed class FakeAuditSink : IOperationAuditSink
    {
        public List<OperationAuditEntry> Entries { get; } = [];
        public bool ThrowOnWrite { get; set; }

        public async Task WriteAsync(OperationAuditEntry entry, CancellationToken cancellationToken)
        {
            if (ThrowOnWrite)
            {
                await Task.Yield();
                throw new InvalidOperationException("sink unavailable");
            }

            Entries.Add(entry);
        }
    }

    private sealed class AllowSystemAdminAuthorization : ISystemAdminAuthorization
    {
        public Task<bool> IsSystemAdminAsync(string tenantNId, string userNId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task EnsureSystemAdminAsync(string tenantNId, string userNId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
