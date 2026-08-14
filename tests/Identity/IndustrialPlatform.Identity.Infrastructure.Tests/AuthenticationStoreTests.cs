using IndustrialPlatform.Identity.Application.Authentication;
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
using Xunit;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 认证用例持久化端口与仓储新方法测试:按规范化登录名/NId 查询、角色 NId 快照、活动权限查询。
/// </summary>
[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class AuthenticationStoreTests : IDisposable
{
    private static readonly string[] BootstrapEnvNames =
    [
        "IDENTITY_BOOTSTRAP_TENANT_NID",
        "IDENTITY_BOOTSTRAP_USER_NID",
        "IDENTITY_BOOTSTRAP_LOGIN_NAME",
        "IDENTITY_BOOTSTRAP_PASSWORD",
    ];

    static AuthenticationStoreTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;
    private readonly UserRepository _users;
    private readonly RoleRepository _roles;
    private readonly PermissionRepository _permissions;
    private readonly UserGroupRepository _userGroups;
    private readonly AuthenticationStore _store;

    public AuthenticationStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-identity-auth-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));

        foreach (var name in BootstrapEnvNames)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance)
            .ApplyPendingAsync()
            .GetAwaiter()
            .GetResult();

        _users = new UserRepository(_dbContext);
        _roles = new RoleRepository(_dbContext);
        _permissions = new PermissionRepository(_dbContext);
        _userGroups = new UserGroupRepository(_dbContext);
        _store = new AuthenticationStore(_users, _roles, _userGroups);
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

    private async Task<Role> CreateRoleWithPermissionsAsync(string nId, params string[] permissionNIds)
    {
        var role = Role.Create("development", nId, nId, null, false);
        foreach (var permissionNId in permissionNIds)
        {
            var permission = await _permissions.GetByNIdAsync(permissionNId);
            Assert.NotNull(permission);
            role.AssignPermission(permission!);
        }

        await _roles.AddAsync(role);
        return role;
    }

    [Fact]
    public async Task FindByNormalizedLoginName_ReturnsUserWithRoleSnapshot()
    {
        var role = await CreateRoleWithPermissionsAsync("ops.role");
        var user = User.Create("development", "user.alice", "alice", "Alice", null, null, "hash-1");
        user.AssignRole(role);
        await _users.AddAsync(user);

        // 大小写不敏感:传大写规范化值命中
        var account = await _store.FindByNormalizedLoginNameAsync("development", "ALICE", CancellationToken.None);

        Assert.NotNull(account);
        Assert.Equal("user.alice", account!.User.NId);
        Assert.Equal([role.Id], account.RoleIds);
        Assert.Equal(["ops.role"], account.RoleNIds);
    }

    [Fact]
    public async Task FindByNormalizedLoginName_Unknown_ReturnsNull()
    {
        Assert.Null(await _store.FindByNormalizedLoginNameAsync("development", "GHOST", CancellationToken.None));
    }

    [Fact]
    public async Task FindByNId_NormalizesAndReturnsUser()
    {
        var role = await CreateRoleWithPermissionsAsync("ops.role");
        var user = User.Create("development", "user.alice", "alice", "Alice", null, null, "hash-1");
        user.AssignRole(role);
        await _users.AddAsync(user);

        // 传小写业务标识,按规范化列匹配
        var account = await _store.FindByNIdAsync("user.alice", CancellationToken.None);

        Assert.NotNull(account);
        Assert.Equal("user.alice", account!.User.NId);
        Assert.Equal(["ops.role"], account.RoleNIds);
    }

    [Fact]
    public async Task FindByNId_DeletedRoleExcludedFromSnapshot()
    {
        var role = await CreateRoleWithPermissionsAsync("ops.role");
        var user = User.Create("development", "user.alice", "alice", "Alice", null, null, "hash-1");
        user.AssignRole(role);
        await _users.AddAsync(user);

        var loadedRole = await _roles.GetByIdAsync(role.Id);
        Assert.NotNull(loadedRole);
        await _roles.DeleteAsync(loadedRole!, loadedRole!.OptimisticVersion, loadedRole.ConcurrencyVersion);

        var account = await _store.FindByNIdAsync("user.alice", CancellationToken.None);

        Assert.NotNull(account);
        Assert.Empty(account!.RoleIds);
        Assert.Empty(account.RoleNIds);
    }

    [Fact]
    public async Task GetActivePermissionsForRoles_DedupsAcrossRoles()
    {
        var roleA = await CreateRoleWithPermissionsAsync("role.a", PermissionCatalog.UserView, PermissionCatalog.RoleView);
        var roleB = await CreateRoleWithPermissionsAsync("role.b", PermissionCatalog.RoleView, PermissionCatalog.UserCreate);

        var permissions = await _store.GetPermissionsForRolesAsync([roleA.Id, roleB.Id], CancellationToken.None);

        var nIds = permissions.Select(p => p.NId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            new[] { PermissionCatalog.UserView, PermissionCatalog.RoleView, PermissionCatalog.UserCreate }
                .Order(StringComparer.Ordinal)
                .ToList(),
            nIds);
    }

    [Fact]
    public async Task GetActivePermissionsForRoles_EmptyRoles_ReturnsEmpty()
    {
        var permissions = await _store.GetPermissionsForRolesAsync([], CancellationToken.None);

        Assert.Empty(permissions);
    }

    [Fact]
    public async Task GetNIds_ExcludesDeletedRoles()
    {
        var role = await CreateRoleWithPermissionsAsync("ops.role");
        var loadedRole = await _roles.GetByIdAsync(role.Id);
        Assert.NotNull(loadedRole);
        await _roles.DeleteAsync(loadedRole!, loadedRole!.OptimisticVersion, loadedRole.ConcurrencyVersion);

        var nIds = await _roles.GetNIdsAsync([role.Id], CancellationToken.None);

        Assert.Empty(nIds);
    }

    [Fact]
    public async Task FindByNId_IncludesGroupInheritedRoles_AsEffectiveRoles()
    {
        // 直接角色 ops.role + 组继承角色 group.role(§29A.2 有效角色并集)
        var directRole = await CreateRoleWithPermissionsAsync("ops.role", PermissionCatalog.UserView);
        var groupRole = await CreateRoleWithPermissionsAsync("group.role", PermissionCatalog.RoleView);
        var user = User.Create("development", "user.alice", "alice", "Alice", null, null, "hash-1");
        user.AssignRole(directRole);
        await _users.AddAsync(user);

        var group = UserGroup.Create("development", "group.ops", "运维组", null);
        group.AssignMember(user);
        group.AssignRole(groupRole);
        await _userGroups.AddAsync(group, [], CancellationToken.None);

        var account = await _store.FindByNIdAsync("user.alice", CancellationToken.None);

        Assert.NotNull(account);
        Assert.Contains(directRole.Id, account!.RoleIds);
        Assert.Contains(groupRole.Id, account.RoleIds);
        Assert.Equal(
            new List<string> { "ops.role", "group.role" }.Order(StringComparer.Ordinal).ToList(),
            account.RoleNIds.Order(StringComparer.Ordinal).ToList());
    }
}
