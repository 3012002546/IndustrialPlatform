using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 三层种子与内置 admin 引导集成测试(TASK-ID-019,§29A.4):
/// 首次/重复/并发语义、稳定 ADMIN/SYSTEM_ADMIN、随机临时密码策略与一次性领取、
/// admin 不强制改密、不覆盖既有 admin、异常 admin 不自动修复、
/// 明文密码不进入数据库/日志/审计/事件、紧急恢复门禁与种子 drift 拒绝。
/// (PostgreSQL 真实验证标记「待验收」。)
/// </summary>
[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class IdentitySeedRunnerTests : IDisposable
{
    private const string Tenant = IdentityTestDatabase.TestTenantNId;
    private const string AdminNId = "ADMIN";
    private const string AdminLoginName = "admin";

    static IdentitySeedRunnerTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;

    public IdentitySeedRunnerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-identity-seed-{Guid.NewGuid():N}.db");
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

    private Task<int> CountAsync(string sql) =>
        _dbContext.SqlSugar.Ado.GetIntAsync(sql);

    private async Task<string> GetStringAsync(string sql) =>
        await _dbContext.SqlSugar.Ado.GetStringAsync(sql);

    private BootstrapCredentialStore CredentialStore() => new(_dbContext);

    private BootstrapService CreateBootstrapService() => new(
        new BootstrapStore(_dbContext, new UserRepository(_dbContext)),
        CredentialStore(),
        new TemporaryPasswordGenerator(),
        new BcryptPasswordHasher());

    [Fact]
    public async Task FirstRun_CreatesThreeLayerSeeds_AndReturnsOneTimeCredential()
    {
        var result = await IdentityTestDatabase.ApplyFullAsync(_dbContext);

        // 三层种子账本:system-catalog / tenant-security / bootstrap-admin
        Assert.Equal(3, await CountAsync("SELECT COUNT(*) FROM identity_seed_ledger"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM identity_seed_ledger WHERE status = 'Applied' AND seed_n_id = 'identity.bootstrap-admin'"));

        // Identity 33 项 + SystemData 36 项，SYSTEM_ADMIN 自动获得完整目录。
        Assert.Equal(69, await CountAsync("SELECT COUNT(*) FROM identity_permission"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM identity_role WHERE n_id = 'SYSTEM_ADMIN'"));
        Assert.Equal(69, await CountAsync("SELECT COUNT(*) FROM identity_role_permission"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM identity_user"));

        // 一次性凭据:密码满足策略且长度 >= 20
        var credential = Assert.IsType<BootstrapSeedResult>(result.BootstrapAdmin);
        Assert.Equal(AdminNId, credential.UserNId);
        Assert.Equal(AdminLoginName, credential.LoginName);
        Assert.True(credential.TemporaryPassword.Length >= BootstrapSeedCatalog.BootstrapPasswordMinLength);
        PasswordPolicy.Validate(credential.TemporaryPassword, AdminLoginName, AdminNId);

        // 库中只存 BCrypt 哈希
        var storedHash = await GetStringAsync("SELECT password_hash FROM identity_user WHERE n_id = 'ADMIN'");
        Assert.NotEqual(credential.TemporaryPassword, storedHash);
        Assert.StartsWith("$2", storedHash, StringComparison.Ordinal);

        // 稳定内部 Id 与 MustChangePassword=false
        var storedId = await GetStringAsync("SELECT id FROM identity_user WHERE n_id = 'ADMIN'");
        Assert.Equal(BootstrapSeedCatalog.BootstrapAdminStableId.ToString(), storedId);
        Assert.Equal(0, await CountAsync("SELECT must_change_password FROM identity_user WHERE n_id = 'ADMIN'"));

        // 交付记录:状态 Delivered(已领取一次),只存引用哈希
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM identity_bootstrap_credential"));
        Assert.Equal((int)BootstrapDeliveryState.Delivered, await CountAsync("SELECT state FROM identity_bootstrap_credential"));
        var deliveryHash = await GetStringAsync("SELECT recovery_reference_hash FROM identity_bootstrap_credential");
        Assert.NotEqual(credential.RecoveryReference, deliveryHash);
        Assert.Equal(BootstrapHashing.HashReference(credential.RecoveryReference), deliveryHash);

        // admin 绑定 SYSTEM_ADMIN
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM identity_user_role ur JOIN identity_role r ON r.id = ur.role_id " +
            "WHERE r.n_id = 'SYSTEM_ADMIN' AND ur.is_deleted = 0 AND ur.user_is_deleted = 0"));
    }

    [Fact]
    public async Task RepeatRun_DoesNotOverwrite_AndNeverReissuesCredential()
    {
        var first = await IdentityTestDatabase.ApplyFullAsync(_dbContext);
        var firstHash = await GetStringAsync("SELECT password_hash FROM identity_user WHERE n_id = 'ADMIN'");
        var firstDeliveryCount = await CountAsync("SELECT COUNT(*) FROM identity_bootstrap_credential");

        var second = await IdentityTestDatabase.ApplyFullAsync(_dbContext);

        // 重复执行不重发凭据
        Assert.Null(second.BootstrapAdmin);
        Assert.Equal(firstHash, await GetStringAsync("SELECT password_hash FROM identity_user WHERE n_id = 'ADMIN'"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM identity_user"));
        Assert.Equal(3, await CountAsync("SELECT COUNT(*) FROM identity_seed_ledger"));
        Assert.Equal(firstDeliveryCount, await CountAsync("SELECT COUNT(*) FROM identity_bootstrap_credential"));
        Assert.Equal(0, await CountAsync("SELECT must_change_password FROM identity_user WHERE n_id = 'ADMIN'"));
    }

    [Fact]
    public async Task UpgradeFromVersionOne_AddsSystemDataPermissionsToSystemAdmin()
    {
        await IdentityTestDatabase.ApplyCatalogAsync(_dbContext);

        // 模拟已完成 1.0.0 的生产库：只有原 33 项 Identity 目录和对应 SYSTEM_ADMIN 绑定。
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "DELETE FROM identity_role_permission WHERE permission_id IN " +
            "(SELECT id FROM identity_permission WHERE n_id LIKE 'systemdata.%')");
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "DELETE FROM identity_permission WHERE n_id LIKE 'systemdata.%'");
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE identity_seed_ledger SET seed_version = '1.0.0'");

        Assert.Equal(33, await CountAsync("SELECT COUNT(*) FROM identity_permission"));

        await IdentityTestDatabase.ApplyCatalogAsync(_dbContext);

        Assert.Equal(69, await CountAsync("SELECT COUNT(*) FROM identity_permission"));
        Assert.Equal(36, await CountAsync("SELECT COUNT(*) FROM identity_permission WHERE n_id LIKE 'systemdata.%'"));
        Assert.Equal(69, await CountAsync("SELECT COUNT(*) FROM identity_role_permission"));
        Assert.Equal(4, await CountAsync("SELECT COUNT(*) FROM identity_seed_ledger"));
    }

    [Fact]
    public async Task DisabledAdmin_ThrowsRecoveryRequired_AndIsNotAutoFixed()
    {
        await IdentityTestDatabase.ApplyFullAsync(_dbContext);

        // admin 被禁用 → 普通种子失败(可诊断),绝不自动修复
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync("UPDATE identity_user SET status = 1 WHERE n_id = 'ADMIN'");

        var exception = await Assert.ThrowsAsync<BootstrapRecoveryRequiredException>(() =>
            IdentityTestDatabase.ApplyFullAsync(_dbContext));
        Assert.Equal("ID_BOOTSTRAP_RECOVERY_REQUIRED", exception.Code);

        // 仍为禁用,密码/状态未被覆盖
        Assert.Equal(1, await CountAsync("SELECT status FROM identity_user WHERE n_id = 'ADMIN'"));
    }

    [Fact]
    public async Task DeletedAdmin_ThrowsRecoveryRequired_AndIsNotAutoFixed()
    {
        await IdentityTestDatabase.ApplyFullAsync(_dbContext);

        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync("UPDATE identity_user SET is_deleted = 1 WHERE n_id = 'ADMIN'");

        await Assert.ThrowsAsync<BootstrapRecoveryRequiredException>(() =>
            IdentityTestDatabase.ApplyFullAsync(_dbContext));
        Assert.Equal(1, await CountAsync("SELECT is_deleted FROM identity_user WHERE n_id = 'ADMIN'"));
    }

    [Fact]
    public async Task TemporaryPassword_NeverPersisted_Anywhere()
    {
        var result = await IdentityTestDatabase.ApplyFullAsync(_dbContext);
        var password = result.BootstrapAdmin!.TemporaryPassword;

        // 明文密码不得出现在任何数据库表(哈希、引用、审计、Outbox、事件均不含明文)
        string[] tables = ["identity_user", "identity_seed_ledger", "identity_bootstrap_credential", "identity_operation_audit", "identity_login_audit", "identity_outbox"];
        foreach (var table in tables)
        {
            var content = await DumpTableAsync(table);
            Assert.DoesNotContain(password, content, StringComparison.Ordinal);
        }

        // 交付记录只含哈希,不含引用或密码
        var deliveryContent = await DumpTableAsync("identity_bootstrap_credential");
        Assert.DoesNotContain(password, deliveryContent, StringComparison.Ordinal);
        Assert.DoesNotContain(result.BootstrapAdmin.DeliveryReference, deliveryContent, StringComparison.Ordinal);
        Assert.DoesNotContain(result.BootstrapAdmin.RecoveryReference, deliveryContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClaimOnce_SecondClaim_ThrowsAlreadyRetrieved()
    {
        var result = await IdentityTestDatabase.ApplyFullAsync(_dbContext);
        var deliveryId = result.BootstrapAdmin!.DeliveryId;

        // 已领取(受保护响应产生即 Delivered)→ 再次领取拒绝
        var exception = await Assert.ThrowsAsync<BootstrapCredentialAlreadyRetrievedException>(() =>
            CredentialStore().ClaimAsync(deliveryId));
        Assert.Equal("ID_BOOTSTRAP_CREDENTIAL_ALREADY_RETRIEVED", exception.Code);
    }

    [Fact]
    public async Task RecoverAdmin_NewRandomPassword_OldInvalid_OldDeliveryRevoked()
    {
        var result = await IdentityTestDatabase.ApplyFullAsync(_dbContext);
        var credential = result.BootstrapAdmin!;
        var service = CreateBootstrapService();

        var recovery = await service.RecoverAdminAsync(Tenant, credential.RecoveryReference, "APPROVAL-DEPLOY-001");

        // 新密码随机且满足策略,与旧密码不同
        Assert.True(recovery.TemporaryPassword.Length >= BootstrapSeedCatalog.BootstrapPasswordMinLength);
        Assert.NotEqual(credential.TemporaryPassword, recovery.TemporaryPassword);
        PasswordPolicy.Validate(recovery.TemporaryPassword, AdminLoginName, AdminNId);

        // 旧密码失效,新密码可验证
        var hasher = new BcryptPasswordHasher();
        var storedHash = await GetStringAsync("SELECT password_hash FROM identity_user WHERE n_id = 'ADMIN'");
        Assert.False(hasher.Verify(storedHash, credential.TemporaryPassword));
        Assert.True(hasher.Verify(storedHash, recovery.TemporaryPassword));

        // 旧交付作废(Recovered),新交付已领取(Delivered)
        Assert.Equal((int)BootstrapDeliveryState.Recovered, await CountAsync("SELECT state FROM identity_bootstrap_credential WHERE id = '" + credential.DeliveryId + "'"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM identity_bootstrap_credential"));
        Assert.Equal((int)BootstrapDeliveryState.Delivered, await CountAsync("SELECT state FROM identity_bootstrap_credential WHERE id = '" + recovery.DeliveryId + "'"));
    }

    [Fact]
    public async Task RecoverAdmin_InvalidReference_IsRejected()
    {
        var result = await IdentityTestDatabase.ApplyFullAsync(_dbContext);
        var service = CreateBootstrapService();

        await Assert.ThrowsAsync<BootstrapRecoveryRejectedException>(() =>
            service.RecoverAdminAsync(Tenant, "wrong-reference", "APPROVAL-DEPLOY-001"));
    }

    [Fact]
    public async Task RecoverAdmin_MissingApproval_IsRejected()
    {
        var result = await IdentityTestDatabase.ApplyFullAsync(_dbContext);
        var service = CreateBootstrapService();

        await Assert.ThrowsAsync<BootstrapRecoveryRejectedException>(() =>
            service.RecoverAdminAsync(Tenant, result.BootstrapAdmin!.RecoveryReference, string.Empty));
    }

    [Fact]
    public async Task SeedDrift_SameVersionDifferentChecksum_IsRejected()
    {
        await IdentityTestDatabase.ApplyCatalogAsync(_dbContext);

        // 篡改账本 checksum(同版本不同内容)→ 再次执行判定 drift 拒绝
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE identity_seed_ledger SET checksum = @badChecksum " +
            "WHERE tenant_n_id = @tenant AND seed_n_id = @key AND seed_version = @version",
            new SugarParameter("@badChecksum", new string('0', 64)),
            new SugarParameter("@tenant", Tenant),
            new SugarParameter("@key", BootstrapSeedCatalog.SystemCatalogSeedKey),
            new SugarParameter("@version", BootstrapSeedCatalog.SeedVersion));

        var exception = await Assert.ThrowsAsync<SeedDriftException>(() =>
            IdentityTestDatabase.ApplyCatalogAsync(_dbContext));
        Assert.Equal("ID_SEED_DRIFT", exception.Code);
    }

    [Fact]
    public async Task RandomPasswords_DifferAcrossFreshDatabases()
    {
        using var second = CreateFreshDbContext(out var secondPath);
        try
        {
            var first = await IdentityTestDatabase.ApplyFullAsync(_dbContext);
            var secondResult = await IdentityTestDatabase.ApplyFullAsync(second);

            Assert.NotNull(first.BootstrapAdmin);
            Assert.NotNull(secondResult.BootstrapAdmin);
            Assert.NotEqual(first.BootstrapAdmin!.TemporaryPassword, secondResult.BootstrapAdmin!.TemporaryPassword);
        }
        finally
        {
            second.Dispose();
            try
            {
                if (File.Exists(secondPath))
                {
                    File.Delete(secondPath);
                }
            }
            catch (IOException)
            {
                // 忽略清理失败
            }
        }
    }

    [Fact]
    public async Task BootstrapStatus_AfterFullInit_IsReady_WithoutSecret()
    {
        await IdentityTestDatabase.ApplyFullAsync(_dbContext);

        var status = await CreateBootstrapService().GetStatusAsync(Tenant);

        Assert.Equal(BootstrapState.Ready, status.State);
        Assert.True(status.AdminExists);
        Assert.False(status.MustChangePassword);
        Assert.True(status.CredentialDelivered);
        Assert.NotEmpty(status.SchemaVersion);
        Assert.Equal(3, status.SeedVersions.Count);
    }

    [Fact]
    public async Task BootstrapStatus_WithoutInit_IsPending()
    {
        // 仅迁移 + 目录种子(无 SecretBootstrap)→ Pending
        await IdentityTestDatabase.ApplyCatalogAsync(_dbContext);

        var status = await CreateBootstrapService().GetStatusAsync(Tenant);

        Assert.Equal(BootstrapState.Pending, status.State);
        Assert.False(status.AdminExists);
    }

    [Fact]
    public async Task Readiness_AfterFullInit_IsReady_SystemDataCompatibleShape()
    {
        await IdentityTestDatabase.ApplyFullAsync(_dbContext);

        var readiness = await CreateBootstrapService().GetReadinessAsync(Tenant);

        Assert.True(readiness.Ready);
        Assert.True(readiness.MigrationReady);
        Assert.True(readiness.RequiredSeedReady);
        Assert.True(readiness.BootstrapReady);
        Assert.Equal(BootstrapState.Ready, readiness.BootstrapStatus);
        Assert.Equal("identity", readiness.ServiceKey);
        Assert.Equal("identity_db", readiness.LogicalDatabaseName);
        Assert.Equal(3, readiness.Seeds.Count);
    }

    [Fact]
    public async Task ConcurrentFullRuns_ConvergeToSingleAdmin_WithoutCorruption()
    {
        // 两个独立连接并发执行完整初始化(SQLite 短事务):账本唯一约束 + 幂等收敛保证单一结果。
        var sharedPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-identity-concurrent-{Guid.NewGuid():N}.db");
        using var ctxA = CreateDbContext(sharedPath);
        using var ctxB = CreateDbContext(sharedPath);
        try
        {
            // 迁移与不可变目录种子先串行落地(DDL 并发会命中迁移账本主键),再并发竞争 SecretBootstrap 层。
            await IdentityTestDatabase.ApplyCatalogAsync(ctxA);

            var taskA = RunWithBusyRetryAsync(() => IdentityTestDatabase.ApplyFullAsync(ctxA));
            var taskB = RunWithBusyRetryAsync(() => IdentityTestDatabase.ApplyFullAsync(ctxB));
            var results = await Task.WhenAll(taskA, taskB);

            // 至少一个执行器创建了 admin 并拿到一次性凭据;最终状态单一且无损坏
            Assert.Contains(results, r => r.BootstrapAdmin is not null);
            using var verify = CreateDbContext(sharedPath);
            Assert.Equal(1, await verify.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM identity_user"));
            Assert.Equal(3, await verify.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM identity_seed_ledger"));
            Assert.Equal(1, await verify.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM identity_bootstrap_credential"));
            Assert.Equal(1, await verify.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM identity_user_role"));
        }
        finally
        {
            ctxA.Dispose();
            ctxB.Dispose();
            try
            {
                if (File.Exists(sharedPath))
                {
                    File.Delete(sharedPath);
                }
            }
            catch (IOException)
            {
                // 忽略清理失败
            }
        }
    }

    [Fact]
    public async Task InitializationService_FullFlow_ReturnsReady_ThenIdempotent()
    {
        var initialization = new IdentityInitializationService(
            new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance),
            IdentityTestDatabase.CreateSeedRunner(_dbContext),
            new BootstrapStore(_dbContext, new UserRepository(_dbContext)));

        var first = await initialization.InitializeAsync(new IdentitySeedContext(Tenant, SystemDataOperationNId: "OP-001", TraceId: "trace-001"));

        Assert.Equal(BootstrapState.Ready, first.BootstrapStatus);
        Assert.NotNull(first.BootstrapAdmin);
        Assert.NotEmpty(first.SchemaVersion);
        Assert.Equal(3, first.SeedVersions.Count);

        // 重复初始化:幂等收敛,不重发凭据,状态仍 Ready
        var second = await initialization.InitializeAsync(new IdentitySeedContext(Tenant, SystemDataOperationNId: "OP-002", TraceId: "trace-002"));
        Assert.Equal(BootstrapState.Ready, second.BootstrapStatus);
        Assert.Null(second.BootstrapAdmin);
        Assert.Equal(first.SchemaVersion, second.SchemaVersion);
    }

    private static async Task<T> RunWithBusyRetryAsync<T>(Func<Task<T>> action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (
                ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase)
                && attempt < 30)
            {
                await Task.Delay(100);
            }
        }
    }

    private static SqlSugarDbContext CreateDbContext(string path) =>
        new(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={path};Foreign Keys=True;Default Timeout=30",
            DbType = DbType.Sqlite,
        }));

    private static SqlSugarDbContext CreateFreshDbContext(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"industrial-platform-identity-seed2-{Guid.NewGuid():N}.db");
        return new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={path};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));
    }

    /// <summary>整表内容序列化(供明文/引用泄漏扫描)。</summary>
    private async Task<string> DumpTableAsync(string table)
    {
        var columns = _dbContext.SqlSugar.DbMaintenance.GetColumnInfosByTableName(table);
        if (columns.Count == 0)
        {
            return string.Empty;
        }

        var selects = string.Join(" || '|' || ", columns.Select(c => $"IFNULL(CAST(\"{c.DbColumnName}\" AS TEXT), '')"));
        return await _dbContext.SqlSugar.Ado.GetStringAsync(
            $"SELECT GROUP_CONCAT(row) FROM (SELECT {selects} AS row FROM {table})");
    }
}
