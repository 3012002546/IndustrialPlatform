using System.Reflection;
using System.Security.Cryptography;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;
using Xunit;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 迁移执行框架集成测试,基于 SQLite 文件库验证账本创建、幂等、失败回滚与后台服务降级。
/// (PostgreSQL 真实验证标记「待验收」。)
/// </summary>
public sealed class SchemaMigrationRunnerTests : IDisposable
{
    private static readonly SchemaMigrationStep[] NoSteps = [];
    private static readonly string[] ExpectedMigrationIds = ["mig-001", "mig-002"];

    static SchemaMigrationRunnerTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;

    public SchemaMigrationRunnerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-migration-test-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath}",
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

    private SchemaMigrationRunner CreateRunner(params SchemaMigrationStep[] steps) =>
        new(_dbContext, steps, NullLogger<SchemaMigrationRunner>.Instance);

    private async Task<bool> TableExistsAsync(string tableName) =>
        await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name",
            new SugarParameter("@name", tableName)) == 1;

    private static SchemaMigrationStep CreateTableStep(string id, string tableName)
    {
        var ddl = $"CREATE TABLE IF NOT EXISTS {tableName} (id TEXT PRIMARY KEY, name TEXT NOT NULL)";
        return new SchemaMigrationStep(id, $"create table {tableName}", (sugar, _) => sugar.Ado.ExecuteCommandAsync(ddl));
    }

    private static SchemaMigrationStep FailingStep(string id = "mig-fail") =>
        new(id, "explode", (_, _) => Task.FromException(new InvalidOperationException("step failed")));

    private static SchemaMigrationStep PartialWorkThenFailStep(string id, string tableName) =>
        new(id, $"create {tableName} then fail", async (sugar, _) =>
        {
            var ddl = $"CREATE TABLE IF NOT EXISTS {tableName} (id TEXT PRIMARY KEY, name TEXT NOT NULL)";
            await sugar.Ado.ExecuteCommandAsync(ddl);
            throw new InvalidOperationException("step failed");
        });

    private async Task SeedLegacyIdentityDatabaseAsync(bool checksumColumnExists, string? description = null)
    {
        var step = IdentitySchemaMigrations.All.Single(step => step.Id == "ID-004-01");
        await step.Apply(_dbContext.SqlSugar, CancellationToken.None);
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            """
            INSERT INTO identity_user
              (id, is_frozen, is_locked, is_deleted, entity_type, created_on, last_updated_on,
               optimistic_version, concurrency_version, tenant_n_id, n_id, normalized_n_id,
               login_name, normalized_login_name, name, password_hash, email, phone, status,
               failed_login_count, locked_until, auth_version, last_login_on, must_change_password)
            VALUES
              ('legacy-user', 0, 0, 0, 'legacy.identity.User', '2026-01-01T00:00:00Z',
               '2026-01-01T00:00:00Z', 0, 'legacy-concurrency', 'development', 'legacy-user',
               'LEGACY-USER', 'legacy', 'LEGACY', 'Legacy user', 'legacy-hash', NULL, NULL,
               0, 0, NULL, 0, NULL, 0)
            """);

        var checksumColumn = checksumColumnExists ? ", checksum TEXT NULL" : string.Empty;
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync($"""
            CREATE TABLE identity_schema_migrations (
                migration_id TEXT PRIMARY KEY NOT NULL,
                description TEXT NOT NULL,
                applied_on TEXT NOT NULL{checksumColumn}
            )
            """);

        var appliedDescription = description ?? step.Description;
        var columns = checksumColumnExists
            ? "migration_id, description, applied_on, checksum"
            : "migration_id, description, applied_on";
        var values = checksumColumnExists
            ? "@migrationId, @description, @appliedOn, NULL"
            : "@migrationId, @description, @appliedOn";
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            $"INSERT INTO identity_schema_migrations ({columns}) VALUES ({values})",
            new SugarParameter[]
            {
                new SugarParameter("@migrationId", step.Id),
                new SugarParameter("@description", appliedDescription),
                new SugarParameter("@appliedOn", "2026-01-01T00:00:00Z"),
            });
    }

    private static string Checksum(SchemaMigrationStep step) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{step.Id}|{step.Description}"))).ToLowerInvariant();

    /// <summary>
    /// 构造后台服务作用域:注册迁移运行器与后台服务依赖的目录种子运行器/配置,
    /// 使 ExecuteAsync 真实执行「迁移 + 目录种子」路径(TASK-ID-019)。
    /// </summary>
    private IServiceScopeFactory CreateScopeFactory(ISchemaMigrationRunner runner)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runner);
        services.AddOptions<BootstrapOptions>().Configure(o => o.TenantNId = IdentityTestDatabase.TestTenantNId);
        services.AddSingleton<IPasswordHasher>(new BcryptPasswordHasher());
        services.AddSingleton<IBootstrapCredentialStore>(new BootstrapCredentialStore(_dbContext));
        services.AddSingleton(new IdentitySeedRunner(
            _dbContext,
            new BcryptPasswordHasher(),
            new BootstrapCredentialStore(_dbContext)));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>
    /// 通过反射调用受保护的 <see cref="BackgroundService.ExecuteAsync"/>,
    /// 验证异常被捕获、调用方不会收到抛出的异常。
    /// </summary>
    private static async Task InvokeExecuteAsync(BackgroundService service, CancellationToken cancellationToken)
    {
        var method = typeof(BackgroundService).GetMethod(
            "ExecuteAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = (Task)method!.Invoke(service, new object?[] { cancellationToken })!;
        await task;
    }

    [Fact]
    public async Task ApplyPending_NoSteps_CreatesLedgerTable()
    {
        var runner = CreateRunner();

        await runner.ApplyPendingAsync();

        Assert.True(await TableExistsAsync("identity_schema_migrations"));
        Assert.Equal(0, await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>().CountAsync());
    }

    [Fact]
    public async Task ApplyPending_RunTwice_AppliesEachStepOnceAndPersistsLedger()
    {
        var appliedCount = 0;
        SchemaMigrationStep CountingStep(string id, string tableName)
        {
            var ddl = $"CREATE TABLE IF NOT EXISTS {tableName} (id TEXT PRIMARY KEY, name TEXT NOT NULL)";
            return new SchemaMigrationStep(id, $"create table {tableName}", async (sugar, _) =>
            {
                Interlocked.Increment(ref appliedCount);
                await sugar.Ado.ExecuteCommandAsync(ddl);
            });
        }

        var runner = CreateRunner(CountingStep("mig-001", "migration_a"), CountingStep("mig-002", "migration_b"));

        await runner.ApplyPendingAsync();
        await runner.ApplyPendingAsync();

        // 两步各只应用一次;重复调用不再执行已记账步骤
        Assert.Equal(2, appliedCount);
        Assert.True(await TableExistsAsync("migration_a"));
        Assert.True(await TableExistsAsync("migration_b"));

        var ledger = await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>()
            .OrderBy(record => record.MigrationId)
            .ToListAsync();
        Assert.Equal(ExpectedMigrationIds, ledger.Select(record => record.MigrationId));
    }

    [Fact]
    public async Task StepUpGrantExtension_DoesNotExecuteDuplicateAlterStatementsWhenColumnsAlreadyExist()
    {
        var createStep = IdentitySchemaMigrations.All.Single(step => step.Id == "ID-021-02");
        var extensionStep = IdentitySchemaMigrations.All.Single(step => step.Id == "ID-021-03");
        await createStep.Apply(_dbContext.SqlSugar, CancellationToken.None);

        var executedSql = new List<string>();
        _dbContext.SqlSugar.Aop.OnLogExecuting = (sql, _) => executedSql.Add(sql);

        await extensionStep.Apply(_dbContext.SqlSugar, CancellationToken.None);

        Assert.DoesNotContain(executedSql, sql => sql.Contains("ALTER TABLE pf05_step_up_grant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StepUpGrantExtension_RecreatesMissingBaseTableBeforeExtendingLegacySchema()
    {
        var extensionStep = IdentitySchemaMigrations.All.Single(step => step.Id == "ID-021-03");

        await extensionStep.Apply(_dbContext.SqlSugar, CancellationToken.None);

        Assert.True(await TableExistsAsync("pf05_step_up_grant"));
        Assert.Equal(3, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM pragma_table_info('pf05_step_up_grant') WHERE name IN ('request_hash', 'consumed_by_service', 'consumed_receipt_n_id')"));
    }

    [Fact]
    public async Task ApplyPending_FailingStep_RollsBackItsOwnPartialWork_RetryReappliesOnlyFailedStep()
    {
        var runner = CreateRunner(
            CreateTableStep("mig-001", "migration_a"),
            PartialWorkThenFailStep("mig-002", "migration_partial"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ApplyPendingAsync());

        // 已提交的 mig-001 保留;失败步骤 mig-002 的部分工作被回滚且未记账
        Assert.True(await TableExistsAsync("migration_a"));
        Assert.False(await TableExistsAsync("migration_partial"));
        var recordedAfterFailure = await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>()
            .Select(record => record.MigrationId)
            .ToListAsync();
        var onlyRecorded = Assert.Single(recordedAfterFailure);
        Assert.Equal("mig-001", onlyRecorded);

        // 修复后重试:仅 mig-002 重新执行,账本补齐两步
        var fixedRunner = CreateRunner(
            CreateTableStep("mig-001", "migration_a"),
            CreateTableStep("mig-002", "migration_b"));

        await fixedRunner.ApplyPendingAsync();

        Assert.True(await TableExistsAsync("migration_b"));
        var recordedAfterRetry = await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>()
            .OrderBy(record => record.MigrationId)
            .Select(record => record.MigrationId)
            .ToListAsync();
        Assert.Equal(ExpectedMigrationIds, recordedAfterRetry);
    }

    [Fact]
    public async Task ExecuteAsync_StepFailure_LogsWarningAndDoesNotThrow()
    {
        var runner = CreateRunner(FailingStep());
        var service = new SchemaMigrationBackgroundService(
            CreateScopeFactory(runner),
            NullLogger<SchemaMigrationBackgroundService>.Instance);

        // 到达此处即证明异常已被捕获、未向调用方抛出(保持无 Docker 服务可运行基线)
        await InvokeExecuteAsync(service, CancellationToken.None);

        Assert.True(await TableExistsAsync("identity_schema_migrations"));
    }

    [Fact]
    public async Task ExecuteAsync_DatabaseUnavailable_DoesNotThrow()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            "industrial-platform-migration-missing",
            $"{Guid.NewGuid():N}.db");
        using var dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={missingPath}",
            DbType = DbType.Sqlite,
        }));
        var runner = new SchemaMigrationRunner(
            dbContext,
            NoSteps,
            NullLogger<SchemaMigrationRunner>.Instance);
        var service = new SchemaMigrationBackgroundService(
            CreateScopeFactory(runner),
            NullLogger<SchemaMigrationBackgroundService>.Instance);

        await InvokeExecuteAsync(service, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyPending_LegacyLedgerMissingChecksum_BackfillsItAndPreservesBusinessData(bool checksumColumnExists)
    {
        var step = IdentitySchemaMigrations.All.Single(step => step.Id == "ID-004-01");
        await SeedLegacyIdentityDatabaseAsync(checksumColumnExists);

        await CreateRunner(step).ApplyPendingAsync();

        Assert.Equal(
            Checksum(step),
            await _dbContext.SqlSugar.Ado.GetStringAsync(
                "SELECT checksum FROM identity_schema_migrations WHERE migration_id = 'ID-004-01'"));
        Assert.Equal(1, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM identity_user WHERE id = 'legacy-user'"));
    }

    [Fact]
    public async Task ApplyPending_LegacyNullChecksumWithDescriptionDrift_IsRejectedWithoutBackfill()
    {
        var step = IdentitySchemaMigrations.All.Single(step => step.Id == "ID-004-01");
        await SeedLegacyIdentityDatabaseAsync(checksumColumnExists: true, description: "changed description");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateRunner(step).ApplyPendingAsync());

        Assert.Contains("description", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>().SingleAsync()).Checksum);
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("perservice")]
    public async Task Full_sqlite_migration_matrix_is_idempotent_and_fails_closed_on_ledger_or_physical_drift(string topology)
    {
        var path = Path.Combine(Path.GetTempPath(), $"industrial-platform-identity-{topology}-{Guid.NewGuid():N}.db");
        using var context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions { ConnectionString = $"Data Source={path}", DbType = DbType.Sqlite }));
        var runner = new SchemaMigrationRunner(context, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance);

        await runner.ApplyPendingAsync();
        await runner.ApplyPendingAsync();
        Assert.Equal(IdentitySchemaMigrations.All.Count, await context.SqlSugar.Queryable<SchemaMigrationRecord>().CountAsync());

        await context.SqlSugar.Ado.ExecuteCommandAsync("UPDATE identity_schema_migrations SET checksum = 'drift' WHERE migration_id = 'ID-004-01'");
        var checksumDrift = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ApplyPendingAsync());
        Assert.Contains("checksum drift", checksumDrift.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("drift", await context.SqlSugar.Ado.GetStringAsync(
            "SELECT checksum FROM identity_schema_migrations WHERE migration_id = 'ID-004-01'"));

        var expectedChecksum = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("ID-004-01|create identity_user"))).ToLowerInvariant();
        await context.SqlSugar.Ado.ExecuteCommandAsync($"UPDATE identity_schema_migrations SET checksum = '{expectedChecksum}' WHERE migration_id = 'ID-004-01'");
        await context.SqlSugar.Ado.ExecuteCommandAsync("DROP INDEX ux_user_active_login_name");
        var physicalDrift = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ApplyPendingAsync());
        Assert.Contains("physical schema drift", physicalDrift.Message, StringComparison.OrdinalIgnoreCase);

        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
