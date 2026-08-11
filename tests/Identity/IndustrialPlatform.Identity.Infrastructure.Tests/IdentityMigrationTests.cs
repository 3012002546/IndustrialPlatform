using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 身份库迁移与种子集成测试(§11/§21.2),基于文件型 SQLite 验证九张表创建、
/// 约束索引、复合外键级联、拒绝不匹配写入与初始化幂等。
/// (PostgreSQL 真实验证标记「待验收」。)
/// </summary>
[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class IdentityMigrationTests : IDisposable
{
    private const string BootstrapTenantNId = "development";
    private const string BootstrapUserNId = "admin";
    private const string BootstrapLoginName = "admin";
    private const string BootstrapPassword = "Admin!2026xyz";

    private static readonly string[] AllTableNames =
    [
        "identity_user",
        "identity_role",
        "identity_permission",
        "identity_user_role",
        "identity_role_permission",
        "identity_refresh_session",
        "identity_login_audit",
        "identity_operation_audit",
        "identity_outbox",
    ];

    private static readonly string[] BootstrapEnvNames =
    [
        "IDENTITY_BOOTSTRAP_TENANT_NID",
        "IDENTITY_BOOTSTRAP_USER_NID",
        "IDENTITY_BOOTSTRAP_LOGIN_NAME",
        "IDENTITY_BOOTSTRAP_PASSWORD",
    ];

    private const string InsertUserSql = """
        INSERT INTO identity_user
          (id, is_frozen, is_locked, is_deleted, entity_type, created_on, last_updated_on,
           optimistic_version, concurrency_version,
           tenant_n_id, n_id, normalized_n_id, login_name, normalized_login_name, name, password_hash,
           email, phone, status, failed_login_count, locked_until, auth_version, last_login_on)
        VALUES
          (@id, @isFrozen, @isLocked, @isDeleted, @entityType, @createdOn, @lastUpdatedOn,
           0, @concurrencyVersion,
           @tenantNId, @nId, @normalizedNId, @loginName, @normalizedLoginName, @name, @passwordHash,
           NULL, NULL, @status, @failedLoginCount, NULL, @authVersion, NULL)
        """;

    private const string InsertPermissionSql = """
        INSERT INTO identity_permission
          (id, is_frozen, is_locked, is_deleted, entity_type, created_on, last_updated_on,
           optimistic_version, concurrency_version,
           n_id, normalized_n_id, name, type, parent_permission_n_id,
           parent_permission_id, parent_permission_is_deleted, description, status)
        VALUES
          (@id, 0, 0, 0, @entityType, @createdOn, @lastUpdatedOn, 0, @concurrencyVersion,
           @nId, @normalizedNId, @name, @type, NULL, NULL, NULL, NULL, @status)
        """;

    private const string InsertUserRoleSql = """
        INSERT INTO identity_user_role
          (id, is_frozen, is_locked, is_deleted, entity_type, created_on, last_updated_on,
           optimistic_version, concurrency_version,
           tenant_n_id, user_id, user_is_deleted, role_id, role_is_deleted)
        VALUES
          (@id, 0, 0, 0, @entityType, @createdOn, @lastUpdatedOn, 0, @concurrencyVersion,
           @tenantNId, @userId, @userIsDeleted, @roleId, @roleIsDeleted)
        """;

    static IdentityMigrationTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;

    public IdentityMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-identity-migration-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));
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

    private static string NowIso() => DateTimeOffset.UtcNow.ToString("O");

    private static void ClearBootstrapEnv()
    {
        foreach (var name in BootstrapEnvNames)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private static SugarParameter[] UserParams(string id, string nId, string normalizedLoginName)
    {
        return
        [
            new SugarParameter("@id", id),
            new SugarParameter("@isFrozen", 0),
            new SugarParameter("@isLocked", 0),
            new SugarParameter("@isDeleted", 0),
            new SugarParameter("@entityType", "IndustrialPlatform.Identity.Domain.Users.User"),
            new SugarParameter("@createdOn", NowIso()),
            new SugarParameter("@lastUpdatedOn", NowIso()),
            new SugarParameter("@concurrencyVersion", Guid.NewGuid().ToString()),
            new SugarParameter("@tenantNId", "development"),
            new SugarParameter("@nId", nId),
            new SugarParameter("@normalizedNId", nId.ToUpperInvariant()),
            new SugarParameter("@loginName", nId),
            new SugarParameter("@normalizedLoginName", normalizedLoginName),
            new SugarParameter("@name", "测试用户"),
            new SugarParameter("@passwordHash", "$2a$12$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123"),
            new SugarParameter("@status", 0),
            new SugarParameter("@failedLoginCount", 0),
            new SugarParameter("@authVersion", 0),
        ];
    }

    private static SugarParameter[] PermissionParams(string nId, string normalizedNId, string name)
    {
        return
        [
            new SugarParameter("@id", Guid.NewGuid().ToString()),
            new SugarParameter("@entityType", "IndustrialPlatform.Identity.Domain.Permissions.Permission"),
            new SugarParameter("@createdOn", NowIso()),
            new SugarParameter("@lastUpdatedOn", NowIso()),
            new SugarParameter("@concurrencyVersion", Guid.NewGuid().ToString()),
            new SugarParameter("@nId", nId),
            new SugarParameter("@normalizedNId", normalizedNId),
            new SugarParameter("@name", name),
            new SugarParameter("@type", 1),
            new SugarParameter("@status", 0),
        ];
    }

    private static SugarParameter[] UserRoleParams(string userId, string roleId, int userIsDeleted, int roleIsDeleted)
    {
        return
        [
            new SugarParameter("@id", Guid.NewGuid().ToString()),
            new SugarParameter("@entityType", "IndustrialPlatform.Identity.Domain.Users.UserRole"),
            new SugarParameter("@createdOn", NowIso()),
            new SugarParameter("@lastUpdatedOn", NowIso()),
            new SugarParameter("@concurrencyVersion", Guid.NewGuid().ToString()),
            new SugarParameter("@tenantNId", "development"),
            new SugarParameter("@userId", userId),
            new SugarParameter("@userIsDeleted", userIsDeleted),
            new SugarParameter("@roleId", roleId),
            new SugarParameter("@roleIsDeleted", roleIsDeleted),
        ];
    }

    private Task ApplyAsync() =>
        new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance)
            .ApplyPendingAsync();

    private Task<int> CountAsync(string sql) =>
        _dbContext.SqlSugar.Ado.GetIntAsync(sql);

    private async Task<string> GetSystemAdminRoleIdAsync() =>
        await _dbContext.SqlSugar.Ado.GetStringAsync("SELECT id FROM identity_role WHERE n_id = 'SYSTEM_ADMIN'");

    [Fact]
    public async Task ApplyPending_CreatesAllNineTables()
    {
        await ApplyAsync();

        // 直接查询 sqlite_master 验证建表:同一进程内多 SqlSugarScope 实例时
        // DbMaintenance.IsAnyTable 的客户端解析可能串到其他实例,改用 Ado 直查稳定可靠。
        foreach (var table in AllTableNames)
        {
            var exists = await _dbContext.SqlSugar.Ado.GetIntAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name",
                new SugarParameter("@name", table));
            Assert.Equal(1, exists);
        }
    }

    [Fact]
    public async Task ApplyPending_RunTwice_IsIdempotent_SeedCountsStable()
    {
        ClearBootstrapEnv();

        await ApplyAsync();
        var first = await ReadSeedCountsAsync();

        await ApplyAsync();
        var second = await ReadSeedCountsAsync();

        // 权限目录 17 项、SYSTEM_ADMIN 系统角色、角色权限 17 条、无默认用户
        Assert.Equal(17, first.PermissionCount);
        Assert.Equal(1, first.RoleCount);
        Assert.Equal(17, first.RolePermissionCount);
        Assert.Equal(0, first.UserCount);
        Assert.Equal(first, second);
    }

    private async Task<(int PermissionCount, int RoleCount, int RolePermissionCount, int UserCount)> ReadSeedCountsAsync()
    {
        return (
            await CountAsync("SELECT COUNT(*) FROM identity_permission"),
            await CountAsync("SELECT COUNT(*) FROM identity_role"),
            await CountAsync("SELECT COUNT(*) FROM identity_role_permission"),
            await CountAsync("SELECT COUNT(*) FROM identity_user"));
    }

    [Fact]
    public async Task ApplyPending_NoBootstrapEnv_CreatesNoDefaultUser()
    {
        ClearBootstrapEnv();

        await ApplyAsync();

        // 生产缺少显式初始化配置时不得创建固定默认账号
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM identity_user"));
    }

    [Fact]
    public async Task ApplyPending_WithBootstrapEnv_CreatesAdminWithSystemAdminRole()
    {
        ClearBootstrapEnv();
        try
        {
            Environment.SetEnvironmentVariable("IDENTITY_BOOTSTRAP_TENANT_NID", BootstrapTenantNId);
            Environment.SetEnvironmentVariable("IDENTITY_BOOTSTRAP_USER_NID", BootstrapUserNId);
            Environment.SetEnvironmentVariable("IDENTITY_BOOTSTRAP_LOGIN_NAME", BootstrapLoginName);
            Environment.SetEnvironmentVariable("IDENTITY_BOOTSTRAP_PASSWORD", BootstrapPassword);

            await ApplyAsync();

            Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM identity_user"));

            // 存储值必须是哈希,不能是明文
            var storedHash = await _dbContext.SqlSugar.Ado.GetStringAsync("SELECT password_hash FROM identity_user");
            Assert.NotEqual(BootstrapPassword, storedHash);
            Assert.StartsWith("$2", storedHash, StringComparison.Ordinal);

            // 管理员被分配 SYSTEM_ADMIN 系统角色(活动关系)
            var adminRoleCount = await CountAsync(
                "SELECT COUNT(*) FROM identity_user_role ur " +
                "JOIN identity_role r ON r.id = ur.role_id " +
                "WHERE r.n_id = 'SYSTEM_ADMIN' AND ur.is_deleted = 0 AND ur.user_is_deleted = 0");
            Assert.Equal(1, adminRoleCount);
        }
        finally
        {
            ClearBootstrapEnv();
        }
    }

    [Fact]
    public async Task ApplyPending_WithBootstrapEnv_RunTwice_CreatesExactlyOneUser()
    {
        ClearBootstrapEnv();
        try
        {
            Environment.SetEnvironmentVariable("IDENTITY_BOOTSTRAP_TENANT_NID", BootstrapTenantNId);
            Environment.SetEnvironmentVariable("IDENTITY_BOOTSTRAP_USER_NID", BootstrapUserNId);
            Environment.SetEnvironmentVariable("IDENTITY_BOOTSTRAP_LOGIN_NAME", BootstrapLoginName);
            Environment.SetEnvironmentVariable("IDENTITY_BOOTSTRAP_PASSWORD", BootstrapPassword);

            await ApplyAsync();
            await ApplyAsync();

            Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM identity_user"));
        }
        finally
        {
            ClearBootstrapEnv();
        }
    }

    [Fact]
    public async Task DuplicateNormalizedNId_IsRejected_ByGlobalUniqueIndex()
    {
        await ApplyAsync();

        // 与种子 identity.user.view 同 normalized_n_id(不同大小写) → 全局唯一冲突
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
                InsertPermissionSql,
                PermissionParams("identity.user.view.extra", "IDENTITY.USER.VIEW", "重复权限")));
    }

    [Fact]
    public async Task SoftDeletedLoginName_CanBeReused_ByPartialUniqueIndex()
    {
        ClearBootstrapEnv();
        await ApplyAsync();

        // 活动记录 (development, ALICE) 已存在 → 同登录名的另一活动用户被拒绝
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(InsertUserSql, UserParams("u-1", "alice.user", "ALICE"));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _dbContext.SqlSugar.Ado.ExecuteCommandAsync(InsertUserSql, UserParams("u-2", "alice2.user", "ALICE")));

        // 软删除第一个用户后,登录名可按活动记录部分唯一规则复用
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE identity_user SET is_deleted = 1 WHERE id = @id",
            new SugarParameter("@id", "u-1"));
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(InsertUserSql, UserParams("u-2", "alice2.user", "ALICE"));

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM identity_user WHERE id = 'u-2' AND is_deleted = 0"));
    }

    [Fact]
    public async Task ParentSoftDelete_CascadesChildShadowColumns()
    {
        ClearBootstrapEnv();
        await ApplyAsync();

        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(InsertUserSql, UserParams("u-1", "alice.user", "ALICE"));
        var roleId = await GetSystemAdminRoleIdAsync();
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            InsertUserRoleSql,
            UserRoleParams("u-1", roleId, userIsDeleted: 0, roleIsDeleted: 0));

        // 父软删除 → 复合外键 ON UPDATE CASCADE 同步子表父级影子列
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE identity_user SET is_deleted = 1 WHERE id = @id",
            new SugarParameter("@id", "u-1"));

        var childShadow = await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT user_is_deleted FROM identity_user_role WHERE user_id = @userId",
            new SugarParameter("@userId", "u-1"));
        Assert.Equal(1, childShadow);
    }

    [Fact]
    public async Task MismatchedParentShadowWrite_IsRejected_ByCompositeForeignKey()
    {
        ClearBootstrapEnv();
        await ApplyAsync();

        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(InsertUserSql, UserParams("u-1", "alice.user", "ALICE"));
        var roleId = await GetSystemAdminRoleIdAsync();

        // 父行当前 (id, is_deleted) = (u-1, false),子行 user_is_deleted=1 无父级可引用 → 外键拒绝
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
                InsertUserRoleSql,
                UserRoleParams("u-1", roleId, userIsDeleted: 1, roleIsDeleted: 0)));
    }
}
