using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Management;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;
using Xunit;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 管理存储测试(§16):分页/过滤/详情、NId 全历史唯一(含软删除)、登录名活跃唯一、
/// 持有者统计与角色权限投影。(PostgreSQL 真实验证标记「待验收」。)
/// </summary>
[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class ManagementStoreTests : IDisposable
{
    private const string TenantNId = "mgmt-test";
    private const string PasswordHash = "$2a$12$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123";

    private static readonly string[] BootstrapEnvNames =
    [
        "IDENTITY_BOOTSTRAP_TENANT_NID",
        "IDENTITY_BOOTSTRAP_USER_NID",
        "IDENTITY_BOOTSTRAP_LOGIN_NAME",
        "IDENTITY_BOOTSTRAP_PASSWORD",
    ];

    static ManagementStoreTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;
    private readonly ManagementStore _store;
    private readonly UserRepository _users;
    private readonly RoleRepository _roles;
    private readonly PermissionRepository _permissions;

    public ManagementStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-mgmt-store-{Guid.NewGuid():N}.db");
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
        _store = new ManagementStore(_dbContext, _users, _roles, _permissions);
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

    private async Task<Permission> SeedPermissionAsync(string nId)
    {
        var permission = Permission.Create(nId, nId, PermissionType.Action, null, null);
        await _permissions.AddAsync(permission, CancellationToken.None);
        return permission;
    }

    private async Task<Role> SeedRoleAsync(string nId, params Permission[] permissions)
    {
        var role = Role.Create(TenantNId, nId, nId, null, isSystem: false);
        foreach (var permission in permissions)
        {
            role.AssignPermission(permission);
        }

        await _roles.AddAsync(role, CancellationToken.None);
        return role;
    }

    private async Task<User> SeedUserAsync(string nId, string loginName, params Role[] roles)
    {
        var user = User.Create(TenantNId, nId, loginName, nId, null, null, PasswordHash);
        foreach (var role in roles)
        {
            user.AssignRole(role);
        }

        await _users.AddAsync(user, CancellationToken.None);
        return user;
    }

    [Fact]
    public async Task QueryUsersAsync_FiltersAndPages()
    {
        await SeedUserAsync("alice.user", "alice");
        await SeedUserAsync("alice.ops", "aops");
        await SeedUserAsync("bob.user", "bob");
        var filter = new UserListFilter(TenantNId, "alice", null, null, null, 1, 10);

        var page = await _store.QueryUsersAsync(filter, CancellationToken.None);

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, u => Assert.Equal(TenantNId, u.TenantNId));
        Assert.Contains(page.Items, u => u.NId == "alice.user");
        Assert.Contains(page.Items, u => u.NId == "alice.ops");
    }

    [Fact]
    public async Task QueryUsersAsync_StatusFilter_ExcludesDisabled()
    {
        var user = await SeedUserAsync("alice.user", "alice");
        var (optimistic, concurrency) = (user.OptimisticVersion, user.ConcurrencyVersion);
        user.Disable();
        await _users.UpdateAsync(user, optimistic, concurrency, CancellationToken.None);
        await SeedUserAsync("bob.user", "bob");
        var filter = new UserListFilter(TenantNId, null, null, null, UserStatus.Active, 1, 10);

        var page = await _store.QueryUsersAsync(filter, CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Equal("bob.user", Assert.Single(page.Items).NId);
    }

    [Fact]
    public async Task GetUserAsync_ReturnsActiveRoleNIds()
    {
        var perm = await SeedPermissionAsync("custom.role-perm");
        var role = await SeedRoleAsync("role.operator", perm);
        var user = await SeedUserAsync("alice.user", "alice", role);

        var stored = await _store.GetUserAsync("alice.user", CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(TenantNId, stored!.TenantNId);
        Assert.Equal(["role.operator"], stored.RoleNIds);
        Assert.True(stored.OptimisticVersion > 0);
    }

    [Fact]
    public async Task GetUserAggregateAsync_RoundtripWithRoles()
    {
        var role = await SeedRoleAsync("role.operator");
        await SeedUserAsync("alice.user", "alice", role);

        var aggregate = await _store.GetUserAggregateAsync("alice.user", CancellationToken.None);

        Assert.NotNull(aggregate);
        Assert.Equal("alice.user", aggregate!.NId);
        Assert.Single(aggregate.UserRoles);
    }

    [Fact]
    public async Task UserExistsByNIdAsync_IncludesSoftDeleted()
    {
        var user = await SeedUserAsync("alice.user", "alice");
        var loaded = await _users.GetByIdAsync(user.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        await _users.DeleteAsync(loaded!, loaded!.OptimisticVersion, loaded.ConcurrencyVersion, CancellationToken.None);

        Assert.True(await _store.UserExistsByNIdAsync("alice.user", CancellationToken.None));
    }

    [Fact]
    public async Task LoginNameExistsAsync_OnlyActiveRecords()
    {
        var user = await SeedUserAsync("alice.user", "alice");
        var loaded = await _users.GetByIdAsync(user.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        await _users.DeleteAsync(loaded!, loaded!.OptimisticVersion, loaded.ConcurrencyVersion, CancellationToken.None);

        // 软删除后登录名可复用:活动记录唯一
        Assert.False(await _store.LoginNameExistsAsync(TenantNId, "alice", CancellationToken.None));
    }

    [Fact]
    public async Task QueryRolesAsync_FiltersAndPages()
    {
        await SeedRoleAsync("role.alpha");
        await SeedRoleAsync("role.beta");
        var filter = new RoleListFilter(TenantNId, "alpha", null, 1, 10);

        var page = await _store.QueryRolesAsync(filter, CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Equal("role.alpha", Assert.Single(page.Items).NId);
    }

    [Fact]
    public async Task GetRoleAsync_ReturnsActivePermissionNIds()
    {
        var perm = await SeedPermissionAsync("custom.role-perm");
        await SeedRoleAsync("role.operator", perm);

        var stored = await _store.GetRoleAsync("role.operator", CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(["custom.role-perm"], stored!.PermissionNIds);
    }

    [Fact]
    public async Task RoleExistsByNIdAsync_IncludesSoftDeleted()
    {
        var role = await SeedRoleAsync("role.alpha");
        var loaded = await _roles.GetByIdAsync(role.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        await _roles.DeleteAsync(loaded!, loaded!.OptimisticVersion, loaded.ConcurrencyVersion, CancellationToken.None);

        Assert.True(await _store.RoleExistsByNIdAsync("role.alpha", CancellationToken.None));
    }

    [Fact]
    public async Task GetRolesByNIdsAsync_ExcludesDeleted()
    {
        await SeedRoleAsync("role.alpha");
        var beta = await SeedRoleAsync("role.beta");
        var loaded = await _roles.GetByIdAsync(beta.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        await _roles.DeleteAsync(loaded!, loaded!.OptimisticVersion, loaded.ConcurrencyVersion, CancellationToken.None);

        var roles = await _store.GetRolesByNIdsAsync(["role.alpha", "role.beta"], CancellationToken.None);

        var role = Assert.Single(roles);
        Assert.Equal("role.alpha", role.NId);
    }

    [Fact]
    public async Task GetPermissionsByNIdsAsync_ResolvesByIdentity()
    {
        await SeedPermissionAsync("custom.perm.a");
        await SeedPermissionAsync("custom.perm.b");

        var permissions = await _store.GetPermissionsByNIdsAsync(["custom.perm.b"], CancellationToken.None);

        var permission = Assert.Single(permissions);
        Assert.Equal("custom.perm.b", permission.NId);
    }

    [Fact]
    public async Task CountActiveRoleHoldersAsync_CountsOnlyActiveTenantUsers()
    {
        var role = await SeedRoleAsync("role.operator");
        await SeedUserAsync("alice.user", "alice", role);
        await SeedUserAsync("bob.user", "bob", role);
        await SeedUserAsync("carol.user", "carol");

        var holders = await _store.CountActiveRoleHoldersAsync(role.Id, TenantNId, CancellationToken.None);

        Assert.Equal(2, holders);
    }

    [Fact]
    public async Task CountActiveRoleHoldersAsync_ExcludesDisabledUser()
    {
        var role = await SeedRoleAsync("role.operator");
        var user = await SeedUserAsync("alice.user", "alice", role);
        var (optimistic, concurrency) = (user.OptimisticVersion, user.ConcurrencyVersion);
        user.Disable();
        await _users.UpdateAsync(user, optimistic, concurrency, CancellationToken.None);

        var holders = await _store.CountActiveRoleHoldersAsync(role.Id, TenantNId, CancellationToken.None);

        Assert.Equal(0, holders);
    }

    [Fact]
    public async Task GetUserNIdsForRoleAsync_ReturnsSortedHolders()
    {
        var role = await SeedRoleAsync("role.operator");
        await SeedUserAsync("bob.user", "bob", role);
        await SeedUserAsync("alice.user", "alice", role);

        var nIds = await _store.GetUserNIdsForRoleAsync(role.Id, TenantNId, CancellationToken.None);

        Assert.Equal(["alice.user", "bob.user"], nIds);
    }

    [Fact]
    public async Task UpdateUserAsync_StaleVersion_ThrowsConcurrency()
    {
        var user = await SeedUserAsync("alice.user", "alice");
        var loaded = await _users.GetByIdAsync(user.Id, CancellationToken.None);
        Assert.NotNull(loaded);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _store.UpdateUserAsync(loaded!, loaded!.OptimisticVersion - 1, loaded.ConcurrencyVersion, [], CancellationToken.None));
    }
}
