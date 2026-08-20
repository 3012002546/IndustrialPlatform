using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.Identity.Infrastructure.Authentication;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 授权数据存储集成测试(§14/§18):租户校验、状态/安全版本/权限集装载,
/// 以及角色删除后权限从快照剔除。SQLite 模拟;(PostgreSQL 真实验证「待验收」。)
/// </summary>
[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class AuthorizationDataStoreTests : IDisposable
{
    private const string TenantNId = "development";
    private const string OtherTenantNId = "other-tenant";
    private const string PasswordHash = "$2a$12$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123";

    private static readonly string[] BootstrapEnvNames =
    [
        "IDENTITY_BOOTSTRAP_TENANT_NID",
        "IDENTITY_BOOTSTRAP_USER_NID",
        "IDENTITY_BOOTSTRAP_LOGIN_NAME",
        "IDENTITY_BOOTSTRAP_PASSWORD",
    ];

    static AuthorizationDataStoreTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;
    private readonly UserRepository _users;
    private readonly RoleRepository _roles;
    private readonly PermissionRepository _permissions;
    private readonly UserGroupRepository _userGroups;

    public AuthorizationDataStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-identity-authz-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));

        foreach (var name in BootstrapEnvNames)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        IdentityTestDatabase.ApplyCatalogAsync(_dbContext).GetAwaiter().GetResult();

        _users = new UserRepository(_dbContext);
        _roles = new RoleRepository(_dbContext);
        _permissions = new PermissionRepository(_dbContext);
        _userGroups = new UserGroupRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // SqlSugarScope 连接池可能短暂占用文件句柄,忽略清理失败。
        }
    }

    /// <summary>种子:复用迁移种子权限 → 角色(含该权限)→ 用户(含该角色)。</summary>
    private async Task<User> SeedAliceWithPermissionAsync(string permissionNId = "identity.user.view")
    {
        var permission = await _permissions.GetByNIdAsync(permissionNId);
        Assert.NotNull(permission);

        var role = Role.Create(TenantNId, "role.operator", "操作员", null, isSystem: false);
        role.AssignPermission(permission!);
        await _roles.AddAsync(role);

        var user = User.Create(TenantNId, "user.alice", "alice", "Alice", null, null, PasswordHash);
        user.AssignRole(role);
        await _users.AddAsync(user);
        return user;
    }

    [Fact]
    public async Task GetSnapshotAsync_ValidUser_ReturnsTenantScopedSnapshot()
    {
        // Arrange
        var user = await SeedAliceWithPermissionAsync();

        // Act
        var store = new AuthorizationDataStore(_users, _roles, _userGroups);
        var snapshot = await store.GetSnapshotAsync(TenantNId, "user.alice", CancellationToken.None);

        // Assert: 快照含租户/用户/状态/安全版本与权限集
        Assert.NotNull(snapshot);
        Assert.Equal(TenantNId, snapshot!.TenantNId);
        Assert.Equal("user.alice", snapshot.UserNId);
        Assert.Equal(UserStatus.Active, snapshot.Status);
        Assert.Equal(user.AuthVersion, snapshot.AuthVersion);
        Assert.Equal(["identity.user.view"], snapshot.PermissionNIds);
    }

    [Fact]
    public async Task GetSnapshotAsync_CrossTenant_ReturnsNull()
    {
        // Arrange: 用户仓储按 NId 查询不区分租户,授权存储必须显式校验租户(防跨租户越权)
        await SeedAliceWithPermissionAsync();

        // Act
        var store = new AuthorizationDataStore(_users, _roles, _userGroups);
        var snapshot = await store.GetSnapshotAsync(OtherTenantNId, "user.alice", CancellationToken.None);

        // Assert
        Assert.Null(snapshot);
    }

    [Fact]
    public async Task GetSnapshotAsync_UnknownUser_ReturnsNull()
    {
        // Arrange
        var store = new AuthorizationDataStore(_users, _roles, _userGroups);

        // Act
        var snapshot = await store.GetSnapshotAsync(TenantNId, "user.ghost", CancellationToken.None);

        // Assert
        Assert.Null(snapshot);
    }

    [Fact]
    public async Task GetSnapshotAsync_DisabledUser_ReturnsDisabledStatus()
    {
        // Arrange
        var user = await SeedAliceWithPermissionAsync();
        var loaded = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(loaded);
        var optimistic = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;
        loaded.Disable();
        await _users.UpdateAsync(loaded, optimistic, concurrency);

        // Act
        var store = new AuthorizationDataStore(_users, _roles, _userGroups);
        var snapshot = await store.GetSnapshotAsync(TenantNId, "user.alice", CancellationToken.None);

        // Assert: 禁用状态进入快照,供授权评估拒绝
        Assert.NotNull(snapshot);
        Assert.Equal(UserStatus.Disabled, snapshot!.Status);
        Assert.Equal(loaded.AuthVersion, snapshot.AuthVersion);
    }

    [Fact]
    public async Task GetSnapshotAsync_DeletedRolePermissions_ExcludedFromSnapshot()
    {
        // Arrange
        var user = await SeedAliceWithPermissionAsync();

        // 软删除角色:复合外键 ON UPDATE CASCADE 同步 role_permission.role_is_deleted=1
        var role = (await _users.GetByIdAsync(user.Id))!.UserRoles.Single(r => !r.IsDeleted);
        var roleAggregate = await _roles.GetByIdAsync(role.RoleId);
        Assert.NotNull(roleAggregate);
        await _roles.DeleteAsync(roleAggregate!, roleAggregate!.OptimisticVersion, roleAggregate.ConcurrencyVersion);

        // Act: 角色删除后,其权限不再出现在用户快照(角色权限变化驱动失效的证据)
        var store = new AuthorizationDataStore(_users, _roles, _userGroups);
        var snapshot = await store.GetSnapshotAsync(TenantNId, "user.alice", CancellationToken.None);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot!.PermissionNIds);
    }

    /// <summary>种子:直接角色(permA)+ 用户组角色(permB)的 Alice,用于有效角色并集断言(§29A.2)。</summary>
    private async Task SeedAliceWithDirectAndGroupRolesAsync()
    {
        var directPermission = await _permissions.GetByNIdAsync("identity.user.view");
        Assert.NotNull(directPermission);
        var directRole = Role.Create(TenantNId, "role.direct", "直接角色", null, isSystem: false);
        directRole.AssignPermission(directPermission!);
        await _roles.AddAsync(directRole);

        var groupPermission = await _permissions.GetByNIdAsync("identity.role.view");
        Assert.NotNull(groupPermission);
        var groupRole = Role.Create(TenantNId, "role.group", "组角色", null, isSystem: false);
        groupRole.AssignPermission(groupPermission!);
        await _roles.AddAsync(groupRole);

        var alice = User.Create(TenantNId, "user.alice", "alice", "Alice", null, null, PasswordHash);
        alice.AssignRole(directRole);
        await _users.AddAsync(alice);

        var group = UserGroup.Create(TenantNId, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignRole(groupRole);
        await _userGroups.AddAsync(group, [], CancellationToken.None);
    }

    [Fact]
    public async Task GetSnapshotAsync_IncludesGroupInheritedPermissions()
    {
        // Arrange: 直接角色 identity.user.view + 组角色 identity.role.view(§29A.2 有效角色并集)
        await SeedAliceWithDirectAndGroupRolesAsync();

        // Act
        var store = new AuthorizationDataStore(_users, _roles, _userGroups);
        var snapshot = await store.GetSnapshotAsync(TenantNId, "user.alice", CancellationToken.None);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal(["identity.role.view", "identity.user.view"], snapshot!.PermissionNIds);
    }

    [Fact]
    public async Task GetSnapshotAsync_DisabledGroup_ExcludesGroupPermissions()
    {
        // Arrange
        await SeedAliceWithDirectAndGroupRolesAsync();
        var group = await _userGroups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(group);
        var optimistic = group!.OptimisticVersion;
        var concurrency = group.ConcurrencyVersion;
        group.Disable();
        await _userGroups.UpdateAsync(group, optimistic, concurrency, [], CancellationToken.None);

        // Act: 禁用组立即停止贡献角色(§29A.2)
        var store = new AuthorizationDataStore(_users, _roles, _userGroups);
        var snapshot = await store.GetSnapshotAsync(TenantNId, "user.alice", CancellationToken.None);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal(["identity.user.view"], snapshot!.PermissionNIds);
    }

    [Fact]
    public async Task GetSnapshotAsync_DirectAndGroupPermissions_AreDeduplicated()
    {
        // Arrange: 同一权限经直接角色与组角色各持有一次 → 快照去重(§29A.2 去重并排序)
        var permission = await _permissions.GetByNIdAsync("identity.user.view");
        Assert.NotNull(permission);

        var directRole = Role.Create(TenantNId, "role.direct", "直接角色", null, isSystem: false);
        directRole.AssignPermission(permission!);
        await _roles.AddAsync(directRole);

        var groupRole = Role.Create(TenantNId, "role.group", "组角色", null, isSystem: false);
        groupRole.AssignPermission(permission!);
        await _roles.AddAsync(groupRole);

        var alice = User.Create(TenantNId, "user.alice", "alice", "Alice", null, null, PasswordHash);
        alice.AssignRole(directRole);
        await _users.AddAsync(alice);

        var group = UserGroup.Create(TenantNId, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignRole(groupRole);
        await _userGroups.AddAsync(group, [], CancellationToken.None);

        // Act
        var store = new AuthorizationDataStore(_users, _roles, _userGroups);
        var snapshot = await store.GetSnapshotAsync(TenantNId, "user.alice", CancellationToken.None);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal(["identity.user.view"], snapshot!.PermissionNIds);
    }
}
