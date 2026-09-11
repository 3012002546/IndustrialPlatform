using IndustrialPlatform.Infrastructure.Database;
using System.Security.Cryptography;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.SystemData.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

/// <summary>
/// SystemData 库迁移执行框架集成测试,基于 SQLite 文件库验证账本创建、幂等与失败回滚。
/// (PostgreSQL 真实验证标记「待验收」。)
/// </summary>
public sealed class SchemaMigrationRunnerTests : IDisposable
{
    static SchemaMigrationRunnerTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;

    public SchemaMigrationRunnerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-systemdata-migration-test-{Guid.NewGuid():N}.db");
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

    private static SchemaMigrationStep CreateTableStep(string id, string tableName)
    {
        var ddl = $"CREATE TABLE IF NOT EXISTS {tableName} (id TEXT PRIMARY KEY, name TEXT NOT NULL)";
        return new SchemaMigrationStep(id, $"create table {tableName}", (sugar, _) => sugar.Ado.ExecuteCommandAsync(ddl));
    }

    private static SchemaMigrationStep FailingStep(string id = "mig-fail") =>
        new(id, "explode", (_, _) => Task.FromException(new InvalidOperationException("step failed")));

    private async Task SeedLegacySystemDataDatabaseAsync(bool checksumColumnExists, string? description = null)
    {
        var step = SystemDataSchemaMigrations.All.Single(step => step.Id == "SDM-001-01");
        await step.Apply(_dbContext.SqlSugar, CancellationToken.None);
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            """
            INSERT INTO system_data_database_environment_policy
              (id, is_frozen, is_locked, is_deleted, entity_type, created_on, last_updated_on,
               optimistic_version, concurrency_version, tenant_n_id, environment_n_id,
               environment_kind, approval_required, backup_required, plan_ttl_seconds,
               plan_timeout_seconds, apply_timeout_seconds, max_pre_migration_retries, policy_revision)
            VALUES
              ('legacy-policy', 0, 0, 0, 'legacy.system_data.EnvironmentPolicy', '2026-01-01T00:00:00Z',
               '2026-01-01T00:00:00Z', 0, 'legacy-concurrency', 'development', 'development',
               0, 0, 0, 3600, 3600, 3600, 3, 1)
            """);

        var checksumColumn = checksumColumnExists ? ", checksum TEXT NULL" : string.Empty;
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync($"""
            CREATE TABLE system_data_schema_migrations (
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
            $"INSERT INTO system_data_schema_migrations ({columns}) VALUES ({values})",
            new SugarParameter[]
            {
                new SugarParameter("@migrationId", step.Id),
                new SugarParameter("@description", appliedDescription),
                new SugarParameter("@appliedOn", "2026-01-01T00:00:00Z"),
            });
    }

    private static string Checksum(SchemaMigrationStep step) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{step.Id}|{step.Description}"))).ToLowerInvariant();

    /// <summary>
    /// 直接查询 sqlite_master 校验表存在性。SqlSugar 的 <c>DbMaintenance.IsAnyTable</c>
    /// 依赖进程级表清单缓存,多个测试各自使用独立 SQLite 文件库时会读到陈旧结果,
    /// 故此处绕过缓存,直接读库(与 <see cref="ApplyPending_PartialWorkThenFail_RollsBackEntireStep"/> 一致)。
    /// </summary>
    private static async Task AssertTableExistsAsync(ISqlSugarClient sugar, string tableName)
    {
        var count = await sugar.Ado.GetIntAsync(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'");
        Assert.True(count == 1, $"表 {tableName} 应存在,实际 sqlite_master 命中 {count} 条。");
    }

    [Fact]
    public async Task ApplyPending_NoSteps_CreatesLedgerTable()
    {
        var runner = CreateRunner();

        await runner.ApplyPendingAsync();

        await AssertTableExistsAsync(_dbContext.SqlSugar, "system_data_schema_migrations");
        Assert.Equal(0, await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>().CountAsync());
    }

    [Fact]
    public async Task Trusted_service_nonce_is_replay_safe_across_two_store_instances_and_retains_recent_expiry()
    {
        await new SchemaMigrationRunner(_dbContext, SystemDataSchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance)
            .ApplyPendingAsync();
        using var secondContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath}",
            DbType = DbType.Sqlite,
        }));
        var first = new SystemDataTrustedServiceCallNonceStore(_dbContext);
        var second = new SystemDataTrustedServiceCallNonceStore(secondContext);

        Assert.True(await first.TryRegisterAsync("collaboration", "nonce-replay", DateTimeOffset.UtcNow.AddSeconds(-1), CancellationToken.None));
        Assert.False(await second.TryRegisterAsync("collaboration", "nonce-replay", DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None));
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

        Assert.Equal(2, appliedCount);
        await AssertTableExistsAsync(_dbContext.SqlSugar, "migration_a");
        await AssertTableExistsAsync(_dbContext.SqlSugar, "migration_b");
        Assert.Equal(2, await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>().CountAsync());
    }

    [Fact]
    public async Task ApplyPending_FailingStep_RollsBackWithoutRecording()
    {
        var runner = CreateRunner(CreateTableStep("mig-001", "migration_c"), FailingStep());

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ApplyPendingAsync());

        await AssertTableExistsAsync(_dbContext.SqlSugar, "migration_c");
        var applied = await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>()
            .Select(record => record.MigrationId).ToListAsync();
        Assert.Equal(["mig-001"], applied);
    }

    [Fact]
    public async Task ApplyPending_PartialWorkThenFail_RollsBackEntireStep()
    {
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "CREATE TABLE IF NOT EXISTS half_done (id TEXT PRIMARY KEY, name TEXT NOT NULL)");

        var failing = new SchemaMigrationStep("mig-half", "insert then fail", async (sugar, _) =>
        {
            await sugar.Ado.ExecuteCommandAsync("INSERT INTO half_done (id, name) VALUES ('a', 'b')");
            throw new InvalidOperationException("step failed");
        });

        var runner = CreateRunner(failing);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ApplyPendingAsync());

        Assert.Equal(0, await _dbContext.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM half_done"));
        Assert.Equal(0, await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>().CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyPending_LegacyLedgerMissingChecksum_BackfillsItAndPreservesBusinessData(bool checksumColumnExists)
    {
        var step = SystemDataSchemaMigrations.All.Single(step => step.Id == "SDM-001-01");
        await SeedLegacySystemDataDatabaseAsync(checksumColumnExists);

        await CreateRunner(step).ApplyPendingAsync();

        Assert.Equal(
            Checksum(step),
            await _dbContext.SqlSugar.Ado.GetStringAsync(
                "SELECT checksum FROM system_data_schema_migrations WHERE migration_id = 'SDM-001-01'"));
        Assert.Equal(1, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM system_data_database_environment_policy WHERE id = 'legacy-policy'"));
    }

    [Fact]
    public async Task ApplyPending_LegacyNullChecksumWithDescriptionDrift_IsRejectedWithoutBackfill()
    {
        var step = SystemDataSchemaMigrations.All.Single(step => step.Id == "SDM-001-01");
        await SeedLegacySystemDataDatabaseAsync(checksumColumnExists: true, description: "changed description");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateRunner(step).ApplyPendingAsync());

        Assert.Contains("description", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>().SingleAsync()).Checksum);
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("perservice")]
    public async Task Full_sqlite_migration_matrix_is_idempotent_and_fails_closed_on_ledger_or_physical_drift(string topology)
    {
        var path = Path.Combine(Path.GetTempPath(), $"industrial-platform-systemdata-{topology}-{Guid.NewGuid():N}.db");
        using var context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions { ConnectionString = $"Data Source={path}", DbType = DbType.Sqlite }));
        var runner = new SchemaMigrationRunner(context, SystemDataSchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance);

        await runner.ApplyPendingAsync();
        await runner.ApplyPendingAsync();
        Assert.Equal(SystemDataSchemaMigrations.All.Count, await context.SqlSugar.Queryable<SchemaMigrationRecord>().CountAsync());

        await context.SqlSugar.Ado.ExecuteCommandAsync("UPDATE system_data_schema_migrations SET checksum = 'drift' WHERE migration_id = 'PF05-002-02'");
        var checksumDrift = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ApplyPendingAsync());
        Assert.Contains("checksum drift", checksumDrift.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("drift", await context.SqlSugar.Ado.GetStringAsync(
            "SELECT checksum FROM system_data_schema_migrations WHERE migration_id = 'PF05-002-02'"));

        var expectedChecksum = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("PF05-002-02|system_collaboration_file_hold"))).ToLowerInvariant();
        await context.SqlSugar.Ado.ExecuteCommandAsync($"UPDATE system_data_schema_migrations SET checksum = '{expectedChecksum}' WHERE migration_id = 'PF05-002-02'");
        await context.SqlSugar.Ado.ExecuteCommandAsync("DROP INDEX ix_system_collaboration_file_hold_file_status");
        var physicalDrift = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ApplyPendingAsync());
        Assert.Contains("physical schema drift", physicalDrift.Message, StringComparison.OrdinalIgnoreCase);

        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
