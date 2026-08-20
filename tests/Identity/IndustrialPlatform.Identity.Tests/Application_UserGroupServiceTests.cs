using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Application.UserGroups;
using Identities = IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IndustrialPlatform.Identity.Application.Tests;

/// <summary>
/// 用户组管理用例测试(§29A.2/§29A.6):创建/资料/启停/最终成员集/最终角色集,
/// 最后系统管理员权威计数守卫,受影响用户授权版本推进、会话撤销、缓存失效与 Outbox 事件。
/// </summary>
public sealed class UserGroupServiceTests
{
    private const string Tenant = "development";
    private const string Actor = "actor.admin";
    private const string PasswordHash = "HASH:Ab1!defghijk";

    private static User CreateUser(string nId, string loginName)
        => User.Create(Tenant, nId, loginName, nId, "u@example.com", null, PasswordHash);

    private static Role CreateRole(string nId, bool isSystem = false)
        => Role.Create(Tenant, nId, nId, null, isSystem);

    private static Permission CreatePermission(string nId)
        => Permission.Create(nId, nId, PermissionType.Action, null, null);

    [Fact]
    public async Task CreateAsync_WithMembersAndRoles_PersistsAndReturnsSummary()
    {
        var store = new FakeGroupAndManagementStore();
        var alice = CreateUser("alice.user", "alice");
        var role = CreateRole("role.operator");
        store.Seed(alice);
        store.Seed(role);
        var service = CreateService(store);

        var summary = await service.CreateAsync(
            Tenant,
            Actor,
            new CreateUserGroupCommand("group.ops", "运维组", null, ["alice.user"], ["role.operator"]),
            CancellationToken.None);

        Assert.Equal("group.ops", summary.NId);
        Assert.Equal("运维组", summary.Name);
        Assert.Equal(["alice.user"], summary.MemberUserNIds);
        Assert.Equal(["role.operator"], summary.RoleNIds);
        // 创建事件进入 Outbox(同事务提交)
        Assert.Contains(store.Outbox, e => e.EventType == "Identity.UserGroupCreated.v1");
    }

    [Fact]
    public async Task CreateAsync_DuplicateNId_ThrowsGroupNIdConflict()
    {
        var store = new FakeGroupAndManagementStore();
        store.Seed(UserGroup.Create(Tenant, "group.ops", "已存在", null));
        var service = CreateService(store);

        var ex = await Assert.ThrowsAsync<GroupNIdConflictException>(() =>
            service.CreateAsync(Tenant, Actor, new CreateUserGroupCommand("group.ops", "新组", null, [], []), CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("ID_GROUP_NID_CONFLICT", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_InvalidMember_ThrowsBusinessRuleViolation()
    {
        var store = new FakeGroupAndManagementStore();
        var service = CreateService(store);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.CreateAsync(Tenant, Actor, new CreateUserGroupCommand("group.ops", "组", null, ["ghost.user"], []), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_InvalidRole_ThrowsBusinessRuleViolation()
    {
        var store = new FakeGroupAndManagementStore();
        var service = CreateService(store);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.CreateAsync(Tenant, Actor, new CreateUserGroupCommand("group.ops", "组", null, [], ["ghost.role"]), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_ChangesProfile()
    {
        var store = new FakeGroupAndManagementStore();
        store.Seed(UserGroup.Create(Tenant, "group.ops", "旧名", null));
        var service = CreateService(store);

        var summary = await service.UpdateAsync(
            Tenant,
            Actor,
            "group.ops",
            new UpdateUserGroupCommand("新名", "新描述", 0, store.GetGroup("group.ops")!.ConcurrencyVersion),
            CancellationToken.None);

        Assert.Equal("新名", summary.Name);
        Assert.Equal("新描述", summary.Description);
    }

    [Fact]
    public async Task SetMembersAsync_AddAndRemove_BumpsAffectedUsersAndRevokesSessions()
    {
        var store = new FakeGroupAndManagementStore();
        var alice = CreateUser("alice.user", "alice");
        var bob = CreateUser("bob.user", "bob");
        var carol = CreateUser("carol.user", "carol");
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignMember(bob);
        store.Seed(alice);
        store.Seed(bob);
        store.Seed(carol);
        store.Seed(group);
        var service = CreateService(store);

        // 最终成员集:移除 alice/bob,加入 carol
        var summary = await service.SetMembersAsync(
            Tenant,
            Actor,
            "group.ops",
            ["carol.user"],
            group.OptimisticVersion,
            group.ConcurrencyVersion,
            CancellationToken.None);

        Assert.Equal(["carol.user"], summary.MemberUserNIds);
        // 受影响用户(alice/bob/carol)授权版本全部推进
        Assert.Equal(1, store.GetUser("alice.user")!.AuthVersion);
        Assert.Equal(1, store.GetUser("bob.user")!.AuthVersion);
        Assert.Equal(1, store.GetUser("carol.user")!.AuthVersion);
        // 会话全部撤销 + 版本化权限缓存全部失效(§29A.2)
        Assert.Equal(3, store.Refresh.RevokedAll.Count);
        Assert.Contains(store.Refresh.RevokedAll, r => r.UserId == alice.Id);
        Assert.Contains(store.Refresh.RevokedAll, r => r.UserId == carol.Id);
        Assert.Equal(3, store.Cache.Invalidated.Count);
        Assert.Contains(store.Cache.Invalidated, i => i.UserNId == "bob.user");
        // 成员变更事件进入 Outbox
        Assert.Contains(store.Outbox, e => e.EventType == "Identity.UserGroupMembershipChanged.v1");
    }

    [Fact]
    public async Task SetMembersAsync_StaleVersion_ThrowsConcurrencyConflict()
    {
        var store = new FakeGroupAndManagementStore();
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        store.Seed(group);
        var service = CreateService(store);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.SetMembersAsync(Tenant, Actor, "group.ops", [], 999, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task SetMembersAsync_RemovingLastSystemAdminViaGroup_Throws()
    {
        var store = new FakeGroupAndManagementStore();
        var sysAdmin = CreateRole("SYSTEM_ADMIN", isSystem: true);
        var alice = CreateUser("alice.user", "alice");
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignRole(sysAdmin);
        store.Seed(sysAdmin);
        store.Seed(alice);
        store.Seed(group);
        var service = CreateService(store);

        // alice 仅经本组持有 SYSTEM_ADMIN 且为唯一持有者 → 拒绝移除(§29A.3)
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.SetMembersAsync(Tenant, Actor, "group.ops", [], group.OptimisticVersion, group.ConcurrencyVersion, CancellationToken.None));

        Assert.Contains("系统管理员", ex.Message);
    }

    [Fact]
    public async Task SetMembersAsync_RemovingMemberWhoKeepsDirectSystemAdmin_Allowed()
    {
        var store = new FakeGroupAndManagementStore();
        var sysAdmin = CreateRole("SYSTEM_ADMIN", isSystem: true);
        var alice = CreateUser("alice.user", "alice");
        var bob = CreateUser("bob.user", "bob");
        alice.AssignRole(sysAdmin); // alice 直接持有 SYSTEM_ADMIN
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignMember(bob);
        group.AssignRole(sysAdmin);
        store.Seed(sysAdmin);
        store.Seed(alice);
        store.Seed(bob);
        store.Seed(group);
        var service = CreateService(store);

        // alice 在本组之外仍直接持有 SYSTEM_ADMIN → 移除本组不剥夺其管理员身份 → 允许
        var summary = await service.SetMembersAsync(
            Tenant,
            Actor,
            "group.ops",
            ["bob.user"],
            group.OptimisticVersion,
            group.ConcurrencyVersion,
            CancellationToken.None);

        Assert.Equal(["bob.user"], summary.MemberUserNIds);
    }

    [Fact]
    public async Task SetRolesAsync_ChangesRolesAndBumpsAllMembers()
    {
        var store = new FakeGroupAndManagementStore();
        var alice = CreateUser("alice.user", "alice");
        var editor = CreateRole("role.editor");
        var viewer = CreateRole("role.viewer");
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignRole(editor);
        store.Seed(alice);
        store.Seed(editor);
        store.Seed(viewer);
        store.Seed(group);
        var service = CreateService(store);

        var summary = await service.SetRolesAsync(
            Tenant,
            Actor,
            "group.ops",
            ["role.viewer"],
            group.OptimisticVersion,
            group.ConcurrencyVersion,
            CancellationToken.None);

        Assert.Equal(["role.viewer"], summary.RoleNIds);
        Assert.Equal(1, store.GetUser("alice.user")!.AuthVersion);
        Assert.Contains(store.Outbox, e => e.EventType == "Identity.UserGroupRolesChanged.v1");
    }

    [Fact]
    public async Task SetRolesAsync_RemovingSystemRole_LastAdmin_Throws()
    {
        var store = new FakeGroupAndManagementStore();
        var sysAdmin = CreateRole("SYSTEM_ADMIN", isSystem: true);
        var alice = CreateUser("alice.user", "alice");
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignRole(sysAdmin);
        store.Seed(sysAdmin);
        store.Seed(alice);
        store.Seed(group);
        var service = CreateService(store);

        // alice 仅经本组持有 SYSTEM_ADMIN 且为唯一持有者 → 移除系统角色被拒
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.SetRolesAsync(Tenant, Actor, "group.ops", [], group.OptimisticVersion, group.ConcurrencyVersion, CancellationToken.None));
    }

    [Fact]
    public async Task SetStatusAsync_Disable_BumpsAllMembersAndRevokesSessions()
    {
        var store = new FakeGroupAndManagementStore();
        var alice = CreateUser("alice.user", "alice");
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        group.AssignMember(alice);
        store.Seed(alice);
        store.Seed(group);
        var service = CreateService(store);

        var summary = await service.SetStatusAsync(
            Tenant,
            Actor,
            "group.ops",
            enabled: false,
            group.OptimisticVersion,
            group.ConcurrencyVersion,
            CancellationToken.None);

        Assert.Equal("Disabled", summary.Status);
        Assert.Equal(1, store.GetUser("alice.user")!.AuthVersion);
        Assert.Contains(store.Refresh.RevokedAll, r => r.UserId == alice.Id);
        Assert.Contains(store.Cache.Invalidated, i => i.UserNId == "alice.user");
        Assert.Contains(store.Outbox, e => e.EventType == "Identity.UserGroupChanged.v1");
    }

    [Fact]
    public async Task SetStatusAsync_Disable_LastSystemAdmin_Throws()
    {
        var store = new FakeGroupAndManagementStore();
        var sysAdmin = CreateRole("SYSTEM_ADMIN", isSystem: true);
        var alice = CreateUser("alice.user", "alice");
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignRole(sysAdmin);
        store.Seed(sysAdmin);
        store.Seed(alice);
        store.Seed(group);
        var service = CreateService(store);

        // 禁用组会剥夺 alice 唯一的管理员路径 → 拒绝
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.SetStatusAsync(Tenant, Actor, "group.ops", enabled: false, group.OptimisticVersion, group.ConcurrencyVersion, CancellationToken.None));
    }

    [Fact]
    public async Task SetStatusAsync_Disable_Allowed_WhenSecondAdminExistsElsewhere()
    {
        var store = new FakeGroupAndManagementStore();
        var sysAdmin = CreateRole("SYSTEM_ADMIN", isSystem: true);
        var alice = CreateUser("alice.user", "alice");
        var bob = CreateUser("bob.user", "bob");
        bob.AssignRole(sysAdmin); // 第二个有效持有者(直接)
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignRole(sysAdmin);
        store.Seed(sysAdmin);
        store.Seed(alice);
        store.Seed(bob);
        store.Seed(group);
        var service = CreateService(store);

        // 租户内 SYSTEM_ADMIN 有效持有者=2(bob 直接 + alice 经组)→ 禁用允许
        var summary = await service.SetStatusAsync(
            Tenant,
            Actor,
            "group.ops",
            enabled: false,
            group.OptimisticVersion,
            group.ConcurrencyVersion,
            CancellationToken.None);

        Assert.Equal("Disabled", summary.Status);
    }

    [Fact]
    public async Task GetAsync_CrossTenant_ThrowsResourceNotFound()
    {
        var store = new FakeGroupAndManagementStore();
        store.Seed(UserGroup.Create(Tenant, "group.ops", "运维组", null));
        var service = CreateService(store);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GetAsync("other-tenant", "group.ops", CancellationToken.None));
    }

    // ---- 安全删除/恢复(§29A.3)----

    [Fact]
    public async Task DeleteAsync_DeletesGroupSoftDeletesRelations_InvalidatesMembers()
    {
        var store = new FakeGroupAndManagementStore();
        var alice = CreateUser("alice.user", "alice");
        var editor = CreateRole("role.editor");
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignRole(editor);
        store.Seed(alice);
        store.Seed(editor);
        store.Seed(group);
        var service = CreateService(store);

        await service.DeleteAsync(
            Tenant,
            Actor,
            "group.ops",
            group.OptimisticVersion,
            group.ConcurrencyVersion,
            CancellationToken.None);

        Assert.True(group.IsDeleted);
        Assert.All(group.Memberships, m => Assert.True(m.IsDeleted));
        Assert.All(group.Roles, r => Assert.True(r.IsDeleted));
        // 成员失去组继承角色 → 授权版本推进 + 会话撤销 + 缓存失效(§29A.2)
        Assert.Equal(1, store.GetUser("alice.user")!.AuthVersion);
        Assert.Contains(store.Refresh.RevokedAll, r => r.UserId == alice.Id);
        Assert.Contains(store.Cache.Invalidated, i => i.UserNId == "alice.user");
        Assert.Contains(store.Audit.Entries, e => e.Action == OperationAction.UserGroupDelete && e.ObjectNId == "group.ops");
    }

    [Fact]
    public async Task DeleteAsync_LastSystemAdminViaGroup_Throws()
    {
        var store = new FakeGroupAndManagementStore();
        var sysAdmin = CreateRole("SYSTEM_ADMIN", isSystem: true);
        var alice = CreateUser("alice.user", "alice");
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignRole(sysAdmin);
        store.Seed(sysAdmin);
        store.Seed(alice);
        store.Seed(group);
        var service = CreateService(store);

        // 删除组会剥夺 alice 唯一的管理员路径 → 拒绝(§29A.3)
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.DeleteAsync(Tenant, Actor, "group.ops", group.OptimisticVersion, group.ConcurrencyVersion, CancellationToken.None));
        Assert.False(group.IsDeleted);
    }

    [Fact]
    public async Task RestoreAsync_RestoresTombstoneAsDisabled_RelationsNotRestored()
    {
        var store = new FakeGroupAndManagementStore();
        var alice = CreateUser("alice.user", "alice");
        var editor = CreateRole("role.editor");
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignRole(editor);
        store.Seed(alice);
        store.Seed(editor);
        store.Seed(group);
        var service = CreateService(store);

        await service.DeleteAsync(Tenant, Actor, "group.ops", group.OptimisticVersion, group.ConcurrencyVersion, CancellationToken.None);

        var tombstone = store.GetGroup("group.ops")!;
        var summary = await service.RestoreAsync(
            Tenant,
            Actor,
            "group.ops",
            tombstone.OptimisticVersion,
            tombstone.ConcurrencyVersion,
            CancellationToken.None);

        Assert.Equal("Disabled", summary.Status);
        Assert.False(tombstone.IsDeleted);
        Assert.All(tombstone.Memberships, m => Assert.True(m.IsDeleted));
        Assert.All(tombstone.Roles, r => Assert.True(r.IsDeleted));
        Assert.Contains(store.Audit.Entries, e => e.Action == OperationAction.UserGroupRestore && e.ObjectNId == "group.ops");
    }

    [Fact]
    public async Task RestoreAsync_ActiveGroup_Throws()
    {
        var store = new FakeGroupAndManagementStore();
        var group = UserGroup.Create(Tenant, "group.ops", "运维组", null);
        store.Seed(group);
        var service = CreateService(store);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            service.RestoreAsync(Tenant, Actor, "group.ops", group.OptimisticVersion, group.ConcurrencyVersion, CancellationToken.None));
    }

    private static UserGroupService CreateService(FakeGroupAndManagementStore store)
        => new(store, store, store.Refresh, store.Cache, store.Audit, NullLogger<UserGroupService>.Instance);

    /// <summary>组合替身:同时实现 IUserGroupStore 与 IManagementStore,共享内存状态(组+用户+角色)。</summary>
    private sealed class FakeGroupAndManagementStore : IUserGroupStore, IManagementStore
    {
        private readonly Dictionary<string, User> _usersByNormalized = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, User> _usersById = new();
        private readonly Dictionary<string, Role> _rolesByNormalized = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, Role> _rolesById = new();
        private readonly Dictionary<string, Permission> _permissionsByNormalized = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, Permission> _permissionsById = new();
        private readonly Dictionary<string, UserGroup> _groupsByNormalized = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, UserGroup> _groupsById = new();
        private readonly Dictionary<Guid, (long OptimisticVersion, Guid ConcurrencyVersion)> _persistedVersions = new();

        public FakeRefreshSessionStore Refresh { get; } = new();
        public FakePermissionCache Cache { get; } = new();
        public FakeAuditSink Audit { get; } = new();
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

        public void Seed(UserGroup group)
        {
            _groupsByNormalized[Identities.NId.Create(group.NId).Normalized] = group;
            _groupsById[group.Id] = group;
            _persistedVersions[group.Id] = (group.OptimisticVersion, group.ConcurrencyVersion);
        }

        public UserGroup? GetGroup(string nId)
            => _groupsByNormalized.TryGetValue(Identities.NId.Create(nId).Normalized, out var group) ? group : null;

        public User? GetUser(string nId)
            => _usersByNormalized.TryGetValue(Identities.NId.Create(nId).Normalized, out var user) ? user : null;

        // ---- IUserGroupStore ----

        public Task<UserGroup?> GetUserGroupAggregateAsync(string groupNId, CancellationToken cancellationToken)
            => Task.FromResult(GetGroup(groupNId) is { IsDeleted: false } group ? group : null);

        public Task<StoredUserGroupPage> QueryUserGroupsAsync(UserGroupListQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new StoredUserGroupPage([], 0));

        public Task<UserGroup?> GetUserGroupAggregateIncludingDeletedAsync(string groupNId, CancellationToken cancellationToken)
            => Task.FromResult(GetGroup(groupNId));

        public Task<bool> UserGroupExistsByNIdAsync(string groupNId, CancellationToken cancellationToken)
            => Task.FromResult(GetGroup(groupNId) is not null);

        public Task<IReadOnlyList<User>> GetUsersByNIdsAsync(IReadOnlyCollection<string> userNIds, CancellationToken cancellationToken)
        {
            var users = userNIds
                .Select(n => _usersByNormalized.TryGetValue(Identities.NId.Create(n).Normalized, out var u) && !u.IsDeleted ? u : null)
                .Where(u => u is not null)
                .Cast<User>()
                .ToList();
            return Task.FromResult<IReadOnlyList<User>>(users);
        }

        public Task AddAsync(UserGroup group, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken)
        {
            Seed(group);
            Outbox.AddRange(outboxEvents);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            UserGroup group,
            long expectedOptimisticVersion,
            Guid expectedConcurrencyVersion,
            IReadOnlyCollection<OutboxEnvelope> outboxEvents,
            CancellationToken cancellationToken)
        {
            EnsureVersions(group.Id, expectedOptimisticVersion, expectedConcurrencyVersion, "用户组不存在");
            _groupsByNormalized[Identities.NId.Create(group.NId).Normalized] = group;
            _groupsById[group.Id] = group;
            _persistedVersions[group.Id] = (group.OptimisticVersion, group.ConcurrencyVersion);
            Outbox.AddRange(outboxEvents);
            return Task.CompletedTask;
        }

        public Task RestoreAsync(
            UserGroup group,
            long expectedOptimisticVersion,
            Guid expectedConcurrencyVersion,
            CancellationToken cancellationToken)
        {
            EnsureVersions(group.Id, expectedOptimisticVersion, expectedConcurrencyVersion, "用户组不存在");
            _groupsByNormalized[Identities.NId.Create(group.NId).Normalized] = group;
            _groupsById[group.Id] = group;
            _persistedVersions[group.Id] = (group.OptimisticVersion, group.ConcurrencyVersion);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<User>> GetMembersAsync(Guid groupId, string tenantNId, CancellationToken cancellationToken)
        {
            var members = _groupsById.TryGetValue(groupId, out var group)
                ? group.Memberships
                    .Where(m => !m.IsDeleted && !m.UserIsDeleted && !m.UserGroupIsDeleted)
                    .Select(m => _usersById.TryGetValue(m.UserId, out var u) && !u.IsDeleted && u.TenantNId == tenantNId ? u : null)
                    .Where(u => u is not null)
                    .Cast<User>()
                    .ToList()
                : [];
            return Task.FromResult<IReadOnlyList<User>>(members);
        }

        public Task<IReadOnlyList<string>> GetMemberUserNIdsAsync(Guid groupId, string tenantNId, CancellationToken cancellationToken)
        {
            var nIds = _groupsById.TryGetValue(groupId, out var group)
                ? group.Memberships
                    .Where(m => !m.IsDeleted && !m.UserIsDeleted && !m.UserGroupIsDeleted)
                    .Select(m => _usersById.TryGetValue(m.UserId, out var u) && !u.IsDeleted && u.TenantNId == tenantNId ? u.NId : null)
                    .Where(n => n is not null)
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList()
                : [];
            return Task.FromResult<IReadOnlyList<string>>(nIds);
        }

        public Task<IReadOnlyList<Guid>> GetRoleIdsForUserAsync(Guid userId, string tenantNId, CancellationToken cancellationToken)
        {
            var roleIds = _groupsById.Values
                .Where(g => g.TenantNId == tenantNId && !g.IsDeleted && g.Status == UserGroupStatus.Active
                    && g.Memberships.Any(m => m.UserId == userId && !m.IsDeleted && !m.UserIsDeleted && !m.UserGroupIsDeleted))
                .SelectMany(g => g.Roles.Where(r => !r.IsDeleted && !r.RoleIsDeleted && !r.UserGroupIsDeleted).Select(r => r.RoleId))
                .Distinct()
                .ToList();
            return Task.FromResult<IReadOnlyList<Guid>>(roleIds);
        }

        public Task<bool> UserHoldsRoleOutsideGroupAsync(Guid userId, Guid groupId, Guid roleId, string tenantNId, CancellationToken cancellationToken)
        {
            // 直接分配
            if (_usersById.TryGetValue(userId, out var user)
                && user.UserRoles.Any(ur => ur.RoleId == roleId && !ur.IsDeleted && !ur.RoleIsDeleted))
            {
                return Task.FromResult(true);
            }

            // 其他活动用户组继承(排除本组)
            var viaOtherGroup = _groupsById.Values.Any(g =>
                g.TenantNId == tenantNId
                && g.Id != groupId
                && !g.IsDeleted
                && g.Status == UserGroupStatus.Active
                && g.Memberships.Any(m => m.UserId == userId && !m.IsDeleted && !m.UserIsDeleted && !m.UserGroupIsDeleted)
                && g.Roles.Any(r => r.RoleId == roleId && !r.IsDeleted && !r.RoleIsDeleted && !r.UserGroupIsDeleted));
            return Task.FromResult(viaOtherGroup);
        }

        public Task<bool> AnyMemberHoldsRoleOnlyViaGroupAsync(Guid groupId, Guid roleId, string tenantNId, CancellationToken cancellationToken)
        {
            if (!_groupsById.TryGetValue(groupId, out var group))
            {
                return Task.FromResult(false);
            }

            var memberIds = group.Memberships
                .Where(m => !m.IsDeleted && !m.UserIsDeleted && !m.UserGroupIsDeleted)
                .Select(m => m.UserId)
                .ToList();
            foreach (var memberId in memberIds)
            {
                if (!UserHoldsRoleOutsideGroupAsync(memberId, groupId, roleId, tenantNId, CancellationToken.None).GetAwaiter().GetResult())
                {
                    return Task.FromResult(true);
                }
            }

            return Task.FromResult(false);
        }

        // ---- IManagementStore ----

        public Task<StoredUserPage> QueryUsersAsync(UserListFilter filter, CancellationToken cancellationToken)
            => Task.FromResult(new StoredUserPage([], 0));

        public Task<StoredUser?> GetUserAsync(string userNId, CancellationToken cancellationToken)
            => Task.FromResult<StoredUser?>(null);

        public Task<User?> GetUserAggregateAsync(string userNId, CancellationToken cancellationToken)
        {
            var user = GetUser(userNId);
            return Task.FromResult(user is { IsDeleted: false } ? user : null);
        }

        public Task<User?> GetUserAggregateIncludingDeletedAsync(string userNId, CancellationToken cancellationToken)
            => Task.FromResult(GetUser(userNId));

        public Task<bool> UserExistsByNIdAsync(string userNId, CancellationToken cancellationToken)
            => Task.FromResult(GetUser(userNId) is not null);

        public Task<bool> LoginNameExistsAsync(string tenantNId, string loginName, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<StoredRolePage> QueryRolesAsync(RoleListFilter filter, CancellationToken cancellationToken)
            => Task.FromResult(new StoredRolePage([], 0));

        public Task<StoredRole?> GetRoleAsync(string roleNId, CancellationToken cancellationToken)
            => Task.FromResult<StoredRole?>(null);

        public Task<Role?> GetRoleAggregateAsync(string roleNId, CancellationToken cancellationToken)
        {
            var role = _rolesByNormalized.TryGetValue(Identities.NId.Create(roleNId).Normalized, out var r) && !r.IsDeleted ? r : null;
            return Task.FromResult(role);
        }

        public Task<bool> RoleExistsByNIdAsync(string roleNId, CancellationToken cancellationToken)
            => Task.FromResult(_rolesByNormalized.ContainsKey(Identities.NId.Create(roleNId).Normalized));

        public Task<IReadOnlyList<Role>> GetRolesByNIdsAsync(IReadOnlyCollection<string> roleNIds, CancellationToken cancellationToken)
        {
            var roles = roleNIds
                .Select(n => _rolesByNormalized.TryGetValue(Identities.NId.Create(n).Normalized, out var r) && !r.IsDeleted ? r : null)
                .Where(r => r is not null)
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
                .Select(n => _permissionsByNormalized.TryGetValue(Identities.NId.Create(n).Normalized, out var p) && !p.IsDeleted ? p : null)
                .Where(p => p is not null)
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
            => Task.FromResult<IReadOnlyList<Permission>>(_permissionsByNormalized.Values.Where(p => !p.IsDeleted).ToList());

        public Task<int> CountActiveRoleHoldersAsync(Guid roleId, string tenantNId, CancellationToken cancellationToken)
        {
            // 权威计数(§29A.3):直接分配 ∪ 组继承(活动用户、活动组)
            var direct = _usersByNormalized.Values.Count(u =>
                u.TenantNId == tenantNId && !u.IsDeleted &&
                u.UserRoles.Any(ur => ur.RoleId == roleId && !ur.IsDeleted && !ur.RoleIsDeleted));
            var group = _groupsById.Values
                .Where(g => g.TenantNId == tenantNId && !g.IsDeleted && g.Status == UserGroupStatus.Active
                    && g.Roles.Any(r => r.RoleId == roleId && !r.IsDeleted && !r.RoleIsDeleted))
                .SelectMany(g => g.Memberships.Where(m => !m.IsDeleted && !m.UserIsDeleted && !m.UserGroupIsDeleted).Select(m => m.UserId))
                .Where(uid => _usersById.TryGetValue(uid, out var u) && !u.IsDeleted && u.TenantNId == tenantNId)
                .Distinct()
                .Count();
            return Task.FromResult(direct + group);
        }

        public Task<IReadOnlyList<string>> GetUserNIdsForRoleAsync(Guid roleId, string tenantNId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);

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
            EnsureVersions(user.Id, expectedOptimisticVersion, expectedConcurrencyVersion, "用户不存在");
            _usersByNormalized[user.NormalizedNId] = user;
            _usersById[user.Id] = user;
            _persistedVersions[user.Id] = (user.OptimisticVersion, user.ConcurrencyVersion);
            return Task.CompletedTask;
        }

        public Task RestoreUserAsync(
            User user,
            long expectedOptimisticVersion,
            Guid expectedConcurrencyVersion,
            IReadOnlyCollection<OutboxEnvelope> outboxEvents,
            CancellationToken cancellationToken)
        {
            EnsureVersions(user.Id, expectedOptimisticVersion, expectedConcurrencyVersion, "用户不存在");
            _usersByNormalized[user.NormalizedNId] = user;
            _usersById[user.Id] = user;
            _persistedVersions[user.Id] = (user.OptimisticVersion, user.ConcurrencyVersion);
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
            EnsureVersions(role.Id, expectedOptimisticVersion, expectedConcurrencyVersion, "角色不存在");
            _rolesByNormalized[Identities.NId.Create(role.NId).Normalized] = role;
            _rolesById[role.Id] = role;
            _persistedVersions[role.Id] = (role.OptimisticVersion, role.ConcurrencyVersion);
            return Task.CompletedTask;
        }

        private void EnsureVersions(Guid id, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, string missingMessage)
        {
            if (!_persistedVersions.TryGetValue(id, out var persisted))
            {
                throw new ConcurrencyException(missingMessage);
            }

            if (persisted.OptimisticVersion != expectedOptimisticVersion || persisted.ConcurrencyVersion != expectedConcurrencyVersion)
            {
                throw new ConcurrencyException("乐观并发冲突");
            }
        }
    }
}
