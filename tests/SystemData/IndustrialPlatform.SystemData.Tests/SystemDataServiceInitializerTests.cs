using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task Inspect_legacy_three_column_ledger_reports_upgrade_without_writing()
    {
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            """
            CREATE TABLE system_data_schema_migrations (
                migration_id TEXT PRIMARY KEY NOT NULL,
                description TEXT NOT NULL,
                applied_on TEXT NOT NULL
            );
            INSERT INTO system_data_schema_migrations (migration_id, description, applied_on)
            VALUES ('SDM-001-01', 'legacy migration', '2026-01-01T00:00:00Z');
            """);

        var inspectedSql = new List<string>();
        _dbContext.SqlSugar.Aop.OnLogExecuting = (sql, _) => inspectedSql.Add(sql);

        var state = await _initializer.InspectAsync(CreateContext(), CancellationToken.None);

        Assert.False(state.Ready);
        Assert.Contains("checksum", state.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(inspectedSql, sql =>
        {
            var statement = sql.TrimStart();
            return statement.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("DROP", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(1, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM system_data_schema_migrations WHERE migration_id = 'SDM-001-01'"));
    }

    [Fact]
    public async Task Legacy_three_column_ledger_runs_full_inspect_apply_verify_and_repeat_startup()
    {
        var firstStep = SystemDataSchemaMigrations.All[0];
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            $"""
            CREATE TABLE system_data_schema_migrations (
                migration_id TEXT PRIMARY KEY NOT NULL,
                description TEXT NOT NULL,
                applied_on TEXT NOT NULL
            );
            INSERT INTO system_data_schema_migrations (migration_id, description, applied_on)
            VALUES ('{firstStep.Id}', '{firstStep.Description}', '2026-01-01T00:00:00Z');
            """);
        await firstStep.Apply(_dbContext.SqlSugar, CancellationToken.None);

        var initializer = new SystemDataServiceInitializer(
            new SchemaMigrationRunner(_dbContext, SystemDataSchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance),
            _dbContext);
        var context = CreateContext(SystemDataSchemaMigrations.All[^1].Id);

        var inspected = await initializer.InspectAsync(context, CancellationToken.None);
        Assert.False(inspected.Ready);
        var plan = await initializer.PlanAsync(context, inspected, CancellationToken.None);

        var applied = await initializer.ApplyAsync(context, plan, CancellationToken.None);
        Assert.True(applied.Ready, applied.Reason);
        Assert.Equal(SystemDataSchemaMigrations.All[^1].Id, applied.ObservedVersion);
        Assert.Equal("2026-01-01T00:00:00Z", await _dbContext.SqlSugar.Ado.GetStringAsync(
            "SELECT applied_on FROM system_data_schema_migrations WHERE migration_id = 'SDM-001-01'"));

        var repeatedInspection = await initializer.InspectAsync(context, CancellationToken.None);
        var repeatedPlan = await initializer.PlanAsync(context, repeatedInspection, CancellationToken.None);
        Assert.True(repeatedInspection.Ready, repeatedInspection.Reason);
        Assert.False(repeatedPlan.RequiresApply);
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

    [Fact]
    public async Task Inspect_rejects_a_partial_ledger_even_when_latest_id_is_present()
    {
        _dbContext.SqlSugar.CodeFirst.InitTables<SchemaMigrationRecord>();
        await _dbContext.SqlSugar.Insertable(new[]
        {
            new SchemaMigrationRecord
            {
                MigrationId = "SDM-016-01",
                Description = "current schema",
                AppliedOn = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero),
            },
            new SchemaMigrationRecord
            {
                MigrationId = "SDM-007-05",
                Description = "backfilled patch",
                AppliedOn = new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero),
            },
        }).ExecuteCommandAsync();

        var state = await _initializer.InspectAsync(
            CreateContext("SDM-016-01"),
            CancellationToken.None);

        Assert.False(state.Ready);
        Assert.Equal("SDM-016-01", state.ObservedVersion);
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

            foreach (var step in SystemDataSchemaMigrations.All)
            {
                await step.Apply(dbContext.SqlSugar, cancellationToken);
                await dbContext.SqlSugar.Insertable(new SchemaMigrationRecord
                {
                    MigrationId = step.Id,
                    Description = step.Description,
                    Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{step.Id}|{step.Description}"))).ToLowerInvariant(),
                    AppliedOn = DateTimeOffset.UtcNow,
                }).ExecuteCommandAsync(cancellationToken);
            }
        }
    }
}
