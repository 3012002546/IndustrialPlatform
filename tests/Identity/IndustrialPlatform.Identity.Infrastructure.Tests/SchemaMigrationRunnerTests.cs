using System.Reflection;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
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

    private static IServiceScopeFactory CreateScopeFactory(ISchemaMigrationRunner runner)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runner);
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

        Assert.True(_dbContext.SqlSugar.DbMaintenance.IsAnyTable("identity_schema_migrations"));
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
        Assert.True(_dbContext.SqlSugar.DbMaintenance.IsAnyTable("migration_a"));
        Assert.True(_dbContext.SqlSugar.DbMaintenance.IsAnyTable("migration_b"));

        var ledger = await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>()
            .OrderBy(record => record.MigrationId)
            .ToListAsync();
        Assert.Equal(ExpectedMigrationIds, ledger.Select(record => record.MigrationId));
    }

    [Fact]
    public async Task ApplyPending_FailingStep_RollsBackItsOwnPartialWork_RetryReappliesOnlyFailedStep()
    {
        var runner = CreateRunner(
            CreateTableStep("mig-001", "migration_a"),
            PartialWorkThenFailStep("mig-002", "migration_partial"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ApplyPendingAsync());

        // 已提交的 mig-001 保留;失败步骤 mig-002 的部分工作被回滚且未记账
        Assert.True(_dbContext.SqlSugar.DbMaintenance.IsAnyTable("migration_a"));
        Assert.False(_dbContext.SqlSugar.DbMaintenance.IsAnyTable("migration_partial"));
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

        Assert.True(_dbContext.SqlSugar.DbMaintenance.IsAnyTable("migration_b"));
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

        Assert.True(_dbContext.SqlSugar.DbMaintenance.IsAnyTable("identity_schema_migrations"));
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
}
