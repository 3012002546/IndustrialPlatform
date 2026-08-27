using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 身份库仓储集成测试:POCO↔聚合双向映射、软删除过滤、双版本并发与子项 diff 同步。
/// (PostgreSQL 真实验证标记「待验收」。)
/// </summary>
[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class IdentityRepositoryTests : IDisposable
{
    private const string TenantNId = "development";
    private const string PasswordHash = "$2a$12$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123";

    private static readonly string[] BootstrapEnvNames =
    [
        "IDENTITY_BOOTSTRAP_TENANT_NID",
        "IDENTITY_BOOTSTRAP_USER_NID",
        "IDENTITY_BOOTSTRAP_LOGIN_NAME",
        "IDENTITY_BOOTSTRAP_PASSWORD",
    ];

    static IdentityRepositoryTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;

    private readonly UserRepository _users;
    private readonly RoleRepository _roles;
    private readonly PermissionRepository _permissions;

    public IdentityRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-identity-repo-{Guid.NewGuid():N}.db");
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

    private static User CreateUser(string nId = "alice.user", string loginName = "alice")
        => User.Create(TenantNId, nId, loginName, "Alice", "alice@example.com", null, PasswordHash);

    private Task<int> CountActiveRelationsAsync(Guid userId) =>
        _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM identity_user_role WHERE user_id = @userId AND is_deleted = 0",
            new SugarParameter("@userId", userId));

    [Fact]
    public async Task User_AddThenGetById_Roundtrip()
    {
        var user = CreateUser();

        await _users.AddAsync(user);

        var loaded = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(loaded);
        Assert.Equal(user.TenantNId, loaded!.TenantNId);
        Assert.Equal(user.NId, loaded.NId);
        Assert.Equal(user.LoginName, loaded.LoginName);
        Assert.Equal("ALICE", loaded.NormalizedLoginName);
        Assert.Equal(user.Name, loaded.Name);
        Assert.Equal(user.PasswordHash, loaded.PasswordHash);
        Assert.Equal(user.Email, loaded.Email);
        Assert.Equal(user.Status, loaded.Status);
    }

    [Fact]
    public async Task User_Update_WithCurrentVersion_PersistsChanges()
    {
        var user = CreateUser();
        await _users.AddAsync(user);

        var loaded = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(loaded);
        var optimistic = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;
        loaded.ChangeProfile("Alice Renamed", "alice2@example.com", null);
        await _users.UpdateAsync(loaded, optimistic, concurrency);

        var persisted = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(persisted);
        Assert.Equal("Alice Renamed", persisted!.Name);
        Assert.Equal("alice2@example.com", persisted.Email);
    }

    [Fact]
    public async Task User_Update_WithStaleVersion_ThrowsConcurrency()
    {
        var user = CreateUser();
        await _users.AddAsync(user);

        var loaded = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(loaded);
        var optimistic = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;
        loaded.ChangeProfile("Alice First", null, null);
        await _users.UpdateAsync(loaded, optimistic, concurrency);

        // 用第一次更新前的双版本再次更新 → 原子 WHERE 不命中 → 并发异常
        var stale = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(stale);
        stale!.ChangeProfile("Alice Second", null, null);
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _users.UpdateAsync(stale, optimistic, concurrency));
    }

    [Fact]
    public async Task User_Delete_ThenGetById_ReturnsNull()
    {
        var user = CreateUser();
        await _users.AddAsync(user);

        var loaded = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(loaded);
        await _users.DeleteAsync(loaded!, loaded!.OptimisticVersion, loaded.ConcurrencyVersion);

        // 软删除默认查询排除
        Assert.Null(await _users.GetByIdAsync(user.Id));
    }

    [Fact]
    public async Task User_Restore_ThenGetById_ReturnsEntity()
    {
        var user = CreateUser();
        await _users.AddAsync(user);

        var loaded = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(loaded);
        await _users.DeleteAsync(loaded!, loaded!.OptimisticVersion, loaded.ConcurrencyVersion);

        // DeleteAsync 已就地推进内存实体双版本,用最新版本恢复
        await _users.RestoreAsync(loaded, loaded.OptimisticVersion, loaded.ConcurrencyVersion);

        var restored = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(restored);
        Assert.False(restored!.IsDeleted);
        Assert.Equal(user.NId, restored.NId);
    }

    [Fact]
    public async Task User_GetById_LoadsActiveRoles_AndSkipsRemoved()
    {
        var role = Role.Create(TenantNId, "ops.role", "运维", null, false);
        await _roles.AddAsync(role);

        var user = CreateUser();
        user.AssignRole(role);
        await _users.AddAsync(user);

        // 载入活动角色关系(双重过滤)
        var loaded = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(loaded);
        var relation = Assert.Single(loaded!.UserRoles);
        Assert.Equal(role.Id, relation.RoleId);
        Assert.False(relation.IsDeleted);

        // 解除角色并提交后,子关系软删,查询不再返回
        var again = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(again);
        var optimistic = again!.OptimisticVersion;
        var concurrency = again.ConcurrencyVersion;
        again.RemoveRole(role, activeHolderCountInTenant: 1);
        await _users.UpdateAsync(again, optimistic, concurrency);

        var afterRemove = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(afterRemove);
        Assert.Empty(afterRemove!.UserRoles);
    }

    [Fact]
    public async Task User_Update_NewRoleAssignment_InsertsRelationRow()
    {
        var role = Role.Create(TenantNId, "ops.role", "运维", null, false);
        await _roles.AddAsync(role);

        var user = CreateUser();
        await _users.AddAsync(user);

        var loaded = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(loaded);
        var optimistic = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;
        loaded.AssignRole(role);
        await _users.UpdateAsync(loaded, optimistic, concurrency);

        Assert.Equal(1, await CountActiveRelationsAsync(user.Id));
    }

    [Fact]
    public async Task User_Update_RoleRemoval_SoftDeletesRelationRow()
    {
        var role = Role.Create(TenantNId, "ops.role", "运维", null, false);
        await _roles.AddAsync(role);

        var user = CreateUser();
        user.AssignRole(role);
        await _users.AddAsync(user);

        var loaded = await _users.GetByIdAsync(user.Id);
        Assert.NotNull(loaded);
        var optimistic = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;
        loaded.RemoveRole(role, activeHolderCountInTenant: 1);
        await _users.UpdateAsync(loaded, optimistic, concurrency);

        Assert.Equal(0, await CountActiveRelationsAsync(user.Id));
        Assert.Equal(1, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM identity_user_role WHERE user_id = @userId AND is_deleted = 1",
            new SugarParameter("@userId", user.Id)));
    }

    [Fact]
    public async Task Role_AddThenGetById_Roundtrip()
    {
        var role = Role.Create(TenantNId, "ops.role", "运维", null, false);

        await _roles.AddAsync(role);

        var loaded = await _roles.GetByIdAsync(role.Id);
        Assert.NotNull(loaded);
        Assert.Equal(role.TenantNId, loaded!.TenantNId);
        Assert.Equal("ops.role", loaded.NId);
        Assert.Equal("OPS.ROLE", loaded.NormalizedNId);
        Assert.Equal("运维", loaded.Name);
        Assert.False(loaded.IsSystem);
    }

    [Fact]
    public async Task Role_GetById_LoadsActivePermissions()
    {
        var userView = await _permissions.GetByNIdAsync(PermissionCatalog.UserView);
        var roleView = await _permissions.GetByNIdAsync(PermissionCatalog.RoleView);
        Assert.NotNull(userView);
        Assert.NotNull(roleView);

        var role = Role.Create(TenantNId, "ops.role", "运维", null, false);
        role.AssignPermission(userView!);
        role.AssignPermission(roleView!);
        await _roles.AddAsync(role);

        var loaded = await _roles.GetByIdAsync(role.Id);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Permissions.Count);
        Assert.Contains(loaded.Permissions, p => p.PermissionId == userView!.Id);
        Assert.Contains(loaded.Permissions, p => p.PermissionId == roleView!.Id);
    }

    [Fact]
    public async Task Permission_GetByNId_ReturnsSeedPermission()
    {
        var permission = await _permissions.GetByNIdAsync(PermissionCatalog.UserView);

        Assert.NotNull(permission);
        Assert.Equal(PermissionCatalog.UserView, permission!.NId);
        Assert.False(permission.IsDeleted);
    }

    [Fact]
    public async Task Permission_GetByNId_Unknown_ReturnsNull()
    {
        Assert.Null(await _permissions.GetByNIdAsync("no.such.permission"));
    }

    [Fact]
    public async Task Permission_GetAll_ReturnsCatalogSeedPermissions()
    {
        var all = await _permissions.GetAllAsync();

        // §9.2 第一批 21 项 + §29A.5 用户组/重置密码 7 项 + bootstrap 2 项(TASK-ID-019/020)
        Assert.Equal(66, all.Count);
        Assert.Contains(all, p => p.NId == PermissionCatalog.UserView);
        Assert.Contains(all, p => p.NId == PermissionCatalog.UserDelete);
        Assert.Contains(all, p => p.NId == PermissionCatalog.UserGroupRestore);
        Assert.Contains(all, p => p.NId == PermissionCatalog.PlatformMobileView);
        Assert.Contains(all, p => p.NId == PermissionCatalog.BootstrapView);
        Assert.Contains(all, p => p.NId == PermissionCatalog.BootstrapRecover);
        Assert.Contains(all, p => p.NId == PermissionCatalog.UserGroupView);
        Assert.Contains(all, p => p.NId == PermissionCatalog.UserGroupAssignRole);
        Assert.Contains(all, p => p.NId == PermissionCatalog.UserResetPassword);
    }
}
