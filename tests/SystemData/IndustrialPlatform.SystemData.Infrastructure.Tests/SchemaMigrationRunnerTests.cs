using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
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
}
