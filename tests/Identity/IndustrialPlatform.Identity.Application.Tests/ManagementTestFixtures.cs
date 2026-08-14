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
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Application.Tests;

/// <summary>管理用例共享测试常量。</summary>
internal static class ManagementTestData
{
    public const string Tenant = "development";
    public const string Actor = "actor.admin";
    public const string StrongPassword = "Ab1!defghijk";

    public static User CreateUser(
        string tenantNId = Tenant,
        string nId = "alice.user",
        string loginName = "alice",
        string name = "Alice",
        string passwordHash = "HASH:Ab1!defghijk")
        => User.Create(tenantNId, nId, loginName, name, "alice@example.com", null, passwordHash);

    public static Role CreateRole(string tenantNId = Tenant, string nId = "role.operator", bool isSystem = false)
        => Role.Create(tenantNId, nId, isSystem ? "System Admin" : "Operator", null, isSystem);

    public static Permission CreatePermission(string nId, PermissionType type = PermissionType.Action)
        => Permission.Create(nId, nId.Replace('.', ' '), type, null, null);

    public static (long Optimistic, Guid Concurrency) Versions(User user)
        => (user.OptimisticVersion, user.ConcurrencyVersion);

    public static (long Optimistic, Guid Concurrency) Versions(Role role)
        => (role.OptimisticVersion, role.ConcurrencyVersion);
}

/// <summary>用户/角色/权限管理用例的内存存储替身(§16 查询、冲突检查与写操作的模拟)。</summary>
internal sealed class FakeManagementStore : IManagementStore
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

    public Role? GetSeededRole(string nId)
        => _rolesByNormalized.TryGetValue(Identities.NId.Create(nId).Normalized, out var role) ? role : null;

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
            .Select(GetSeededRole)
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
            u.TenantNId == tenantNId && !u.IsDeleted && u.Status == UserStatus.Active &&
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
            u.OptimisticVersion,
            u.ConcurrencyVersion,
            roleNIds);
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

/// <summary>操作审计端口替身(记录条目,可模拟写入失败)。</summary>
internal sealed class FakeAuditSink : IOperationAuditSink
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

/// <summary>密码哈希端口替身(确定性前缀,便于断言敏感字段不进审计)。</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "HASH:" + password;

    public bool Verify(string passwordHash, string password) => passwordHash == "HASH:" + password;

    public bool NeedsRehash(string passwordHash) => false;
}

/// <summary>刷新会话存储替身(记录按用户全量撤销调用)。</summary>
internal sealed class FakeRefreshSessionStore : IRefreshSessionStore
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

/// <summary>权限缓存替身(记录失效调用,可模拟失效失败)。</summary>
internal sealed class FakePermissionCache : IPermissionCache
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

/// <summary>
/// 用户组存储替身(供用户管理用例测试使用):默认无组数据;可种子用户经活动组继承的角色,
/// 用于删除/禁用等最后系统管理员守卫的组继承路径断言。
/// </summary>
internal sealed class FakeUserGroupStore : IUserGroupStore
{
    private readonly Dictionary<Guid, List<Guid>> _groupRoleIdsByUser = new();

    /// <summary>种子:用户经活动用户组继承指定角色 Id(§29A.2 组继承路径)。</summary>
    public void SeedGroupInheritedRole(Guid userId, Guid roleId)
    {
        if (!_groupRoleIdsByUser.TryGetValue(userId, out var roleIds))
        {
            roleIds = [];
            _groupRoleIdsByUser[userId] = roleIds;
        }

        roleIds.Add(roleId);
    }

    public Task<UserGroup?> GetUserGroupAggregateAsync(string groupNId, CancellationToken cancellationToken)
        => Task.FromResult<UserGroup?>(null);

    public Task<UserGroup?> GetUserGroupAggregateIncludingDeletedAsync(string groupNId, CancellationToken cancellationToken)
        => Task.FromResult<UserGroup?>(null);

    public Task<bool> UserGroupExistsByNIdAsync(string groupNId, CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task<IReadOnlyList<User>> GetUsersByNIdsAsync(IReadOnlyCollection<string> userNIds, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<User>>([]);

    public Task AddAsync(UserGroup group, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task UpdateAsync(
        UserGroup group,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        IReadOnlyCollection<OutboxEnvelope> outboxEvents,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task RestoreAsync(
        UserGroup group,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<IReadOnlyList<User>> GetMembersAsync(Guid groupId, string tenantNId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<User>>([]);

    public Task<IReadOnlyList<string>> GetMemberUserNIdsAsync(Guid groupId, string tenantNId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<Guid>> GetRoleIdsForUserAsync(Guid userId, string tenantNId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Guid>>(
            _groupRoleIdsByUser.TryGetValue(userId, out var roleIds) ? roleIds.ToList() : []);

    public Task<bool> UserHoldsRoleOutsideGroupAsync(Guid userId, Guid groupId, Guid roleId, string tenantNId, CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task<bool> AnyMemberHoldsRoleOnlyViaGroupAsync(Guid groupId, Guid roleId, string tenantNId, CancellationToken cancellationToken)
        => Task.FromResult(false);
}
