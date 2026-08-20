using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

public sealed class SystemDataServiceInitializerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-systemdata-initializer-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext _dbContext;
    private readonly SystemDataServiceInitializer _initializer;

    public SystemDataServiceInitializerTests()
    {
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath}",
            DbType = DbType.Sqlite,
        }));
        _initializer = new SystemDataServiceInitializer(
            new NoopMigrationRunner(_dbContext),
            _dbContext);
    }

    [Fact]
    public async Task Inspect_maps_missing_local_ledger_to_not_ready_without_creating_it()
    {
        var state = await _initializer.InspectAsync(CreateContext(), CancellationToken.None);

        Assert.False(state.Ready);
        Assert.Contains("尚未创建", state.Reason, StringComparison.Ordinal);
        Assert.Equal(
            0,
            await _dbContext.SqlSugar.Ado.GetIntAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='system_data_schema_migrations'"));
    }

    [Fact]
    public async Task Inspect_propagates_cancellation_instead_of_reporting_missing_ledger()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _initializer.InspectAsync(CreateContext(), cancellation.Token));
    }

    [Fact]
    public async Task Apply_and_verify_then_plan_against_same_service_target_is_idempotent()
    {
        var context = CreateContext(SystemDataSchemaMigrations.All[^1].Id);

        var inspection = await _initializer.InspectAsync(context, CancellationToken.None);
        var plan = await _initializer.PlanAsync(context, inspection, CancellationToken.None);
        Assert.True(plan.RequiresApply);

        await _initializer.ApplyAsync(context, plan, CancellationToken.None);
        var verified = await _initializer.VerifyAsync(context, CancellationToken.None);
        Assert.True(verified.Ready);

        var secondInspection = await _initializer.InspectAsync(context, CancellationToken.None);
        var secondPlan = await _initializer.PlanAsync(context, secondInspection, CancellationToken.None);

        Assert.True(secondInspection.Ready);
        Assert.Equal(SystemDataSchemaMigrations.All[^1].Id, secondInspection.ObservedVersion);
        Assert.False(secondPlan.RequiresApply);
    }

    [Fact]
    public async Task Explicit_external_target_mismatch_remains_not_ready_for_apply()
    {
        var currentVersion = SystemDataSchemaMigrations.All[^1].Id;
        var context = CreateContext(currentVersion);
        var inspection = await _initializer.InspectAsync(context, CancellationToken.None);
        var plan = await _initializer.PlanAsync(context, inspection, CancellationToken.None);
        await _initializer.ApplyAsync(context, plan, CancellationToken.None);

        var mismatchContext = context with { DesiredVersion = "external-target-v1" };
        var mismatchInspection = await _initializer.InspectAsync(mismatchContext, CancellationToken.None);
        var mismatchPlan = await _initializer.PlanAsync(mismatchContext, mismatchInspection, CancellationToken.None);

        Assert.True(mismatchInspection.Ready);
        Assert.Equal(currentVersion, mismatchInspection.ObservedVersion);
        Assert.Equal("external-target-v1", mismatchPlan.DesiredVersion);
        Assert.True(mismatchPlan.RequiresApply);
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
            // SQLite 连接池可能短暂占用文件句柄，清理失败不影响断言。
        }
    }

    private static ServiceInitializationContext CreateContext(string? desiredVersion = null) => new(
        "Test",
        "tenant-1",
        "operation-1",
        "systemdata",
        "systemdata",
        new ResolvedDatabaseTarget(
            "Test",
            DatabaseTopologyMode.Shared,
            "systemdata",
            DatabaseProvider.Sqlite,
            "systemdata_db",
            "target",
            false),
        desiredVersion ?? "systemdata-v1",
        ServiceInitializationPolicy.Standard,
        "trace-1");

    private sealed class NoopMigrationRunner(SqlSugarDbContext dbContext) : ISchemaMigrationRunner
    {
        public async Task ApplyPendingAsync(CancellationToken cancellationToken = default)
        {
            dbContext.SqlSugar.CodeFirst.InitTables<SchemaMigrationRecord>();
            if (await dbContext.SqlSugar.Queryable<SchemaMigrationRecord>().AnyAsync(cancellationToken))
            {
                return;
            }

            var latest = SystemDataSchemaMigrations.All[^1];
            await dbContext.SqlSugar.Insertable(new SchemaMigrationRecord
            {
                MigrationId = latest.Id,
                Description = latest.Description,
                AppliedOn = DateTimeOffset.UtcNow,
            }).ExecuteCommandAsync(cancellationToken);
        }
    }
}
