using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 身份库迁移与不可变系统目录种子集成测试(§11/§21.2 + §29A.4):
/// 基于文件型 SQLite 验证建表、约束索引、复合外键级联、拒绝不匹配写入与初始化幂等。
/// 三层种子与内置 admin 引导由 IdentitySeedRunnerTests 单独覆盖。
/// (PostgreSQL 真实验证标记「待验收」。)
/// </summary>
[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class IdentityMigrationTests : IDisposable
{
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
        "identity_seed_ledger",
        "identity_bootstrap_credential",
        "identity_write_idempotency",
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

    private Task ApplyAsync() => IdentityTestDatabase.ApplyCatalogAsync(_dbContext);

    private Task<int> CountAsync(string sql) =>
        _dbContext.SqlSugar.Ado.GetIntAsync(sql);

    private async Task<string> GetSystemAdminRoleIdAsync() =>
        await _dbContext.SqlSugar.Ado.GetStringAsync("SELECT id FROM identity_role WHERE n_id = 'SYSTEM_ADMIN'");

    [Fact]
    public async Task ApplyPending_CreatesAllTables()
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
        await ApplyAsync();
        var first = await ReadSeedCountsAsync();

        await ApplyAsync();
        var second = await ReadSeedCountsAsync();

        // Identity 30 项 + SystemData 36 项、SYSTEM_ADMIN 系统角色、无默认用户;
        // 两个不可变目录种子账本记录,重复执行不新增。
        Assert.Equal(66, first.PermissionCount);
        Assert.Equal(1, first.RoleCount);
        Assert.Equal(66, first.RolePermissionCount);
        Assert.Equal(0, first.UserCount);
        Assert.Equal(2, first.LedgerCount);
        Assert.Equal(first, second);
    }

    private async Task<(int PermissionCount, int RoleCount, int RolePermissionCount, int UserCount, int LedgerCount)> ReadSeedCountsAsync()
    {
        return (
            await CountAsync("SELECT COUNT(*) FROM identity_permission"),
            await CountAsync("SELECT COUNT(*) FROM identity_role"),
            await CountAsync("SELECT COUNT(*) FROM identity_role_permission"),
            await CountAsync("SELECT COUNT(*) FROM identity_user"),
            await CountAsync("SELECT COUNT(*) FROM identity_seed_ledger"));
    }

    [Fact]
    public async Task ApplyPending_CatalogSeeds_DoNotCreateUsers()
    {
        await ApplyAsync();

        // 不可变目录种子绝不创建任何用户;内置 admin 只在显式初始化(SecretBootstrap)时创建。
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM identity_user"));
    }

    [Fact]
    public async Task MustChangePassword_ColumnExists_WithDefaultFalse()
    {
        await ApplyAsync();

        // 既有行(未显式赋值)默认 must_change_password=false(§29A.4 内置 admin 语义)。
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(InsertUserSql, UserParams("u-1", "alice.user", "ALICE"));
        var value = await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT must_change_password FROM identity_user WHERE id = 'u-1'");
        Assert.Equal(0, value);

        // 重复迁移:ALTER 守卫幂等,不因列已存在而失败。
        await ApplyAsync();
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
