using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Collaboration.Infrastructure;
using IndustrialPlatform.Collaboration.Infrastructure.Persistence;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Infrastructure_CollaborationServiceInitializerTests : IDisposable
{
    static Infrastructure_CollaborationServiceInitializerTests() => Batteries_V2.Init();

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-initializer-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext _dbContext;

    public Infrastructure_CollaborationServiceInitializerTests()
    {
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={_databasePath}",
            IsAutoCloseConnection = true,
        }));
    }

    [Fact]
    public async Task Apply_is_idempotent_and_rejects_migration_checksum_drift()
    {
        var initializer = new CollaborationServiceInitializer(_dbContext);
        var context = CreateContext();

        var initial = await initializer.InspectAsync(context, CancellationToken.None);
        Assert.False(initial.Ready);

        var firstPlan = await initializer.PlanAsync(context, initial, CancellationToken.None);
        Assert.True(firstPlan.RequiresApply);
        var applied = await initializer.ApplyAsync(context, firstPlan, CancellationToken.None);
        Assert.True(applied.Ready);
        Assert.Equal(CollaborationSchemaMigrations.All[^1].Id, applied.ObservedVersion);
        Assert.Equal(CollaborationSchemaMigrations.All.Count, await _dbContext.SqlSugar.Queryable<CollaborationSchemaMigrationRecord>().CountAsync());

        var repeatInspection = await initializer.InspectAsync(context, CancellationToken.None);
        var repeatPlan = await initializer.PlanAsync(context, repeatInspection, CancellationToken.None);
        Assert.True(repeatInspection.Ready);
        Assert.False(repeatPlan.RequiresApply);
        await initializer.ApplyAsync(context, repeatPlan, CancellationToken.None);
        Assert.Equal(CollaborationSchemaMigrations.All.Count, await _dbContext.SqlSugar.Queryable<CollaborationSchemaMigrationRecord>().CountAsync());

        await _dbContext.SqlSugar.Updateable<CollaborationSchemaMigrationRecord>()
            .SetColumns(record => new CollaborationSchemaMigrationRecord { Checksum = "drift" })
            .Where(record => record.MigrationId == CollaborationSchemaMigrations.All[0].Id)
            .ExecuteCommandAsync();

        var drift = await initializer.InspectAsync(context, CancellationToken.None);
        Assert.False(drift.Ready);
        Assert.Contains("迁移校验失败", drift.Reason, StringComparison.Ordinal);
        var driftPlan = await initializer.PlanAsync(context, drift, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.ApplyAsync(context, driftPlan, CancellationToken.None));
        Assert.Contains("checksum drift", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("drift", await _dbContext.SqlSugar.Ado.GetStringAsync(
            $"SELECT checksum FROM collaboration_schema_migrations WHERE migration_id = '{CollaborationSchemaMigrations.All[0].Id}'"));
    }

    [Fact]
    public async Task Inspect_LegacyThreeColumnLedger_BackfillsChecksumsAndPreservesSchema()
    {
        var initializer = new CollaborationServiceInitializer(_dbContext);
        var context = CreateContext();
        var initial = await initializer.InspectAsync(context, CancellationToken.None);
        var plan = await initializer.PlanAsync(context, initial, CancellationToken.None);
        await initializer.ApplyAsync(context, plan, CancellationToken.None);
        var legacyAppliedOn = await _dbContext.SqlSugar.Ado.GetStringAsync(
            "SELECT applied_on FROM collaboration_schema_migrations WHERE migration_id = 'PF05-001'");
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            """
            INSERT INTO collaboration_conversation
              (id, is_frozen, is_locked, is_deleted, entity_type, created_on, last_updated_on,
               optimistic_version, concurrency_version, tenant_n_id, n_id,
               participant_low_user_n_id, participant_high_user_n_id, status,
               last_message_sequence, last_message_n_id, last_message_on, retention_floor_sequence)
            VALUES
              ('00000000-0000-0000-0000-000000000001', 0, 0, 0, 'legacy.collaboration.Conversation', '2026-01-01T00:00:00Z',
               '2026-01-01T00:00:00Z', 0, '00000000-0000-0000-0000-000000000002', 'tenant-1', 'legacy-conversation',
               'alice', 'bob', 'Active', 0, NULL, NULL, 0)
            """);

        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            """
            CREATE TABLE collaboration_schema_migrations_legacy (
                migration_id TEXT PRIMARY KEY NOT NULL,
                description TEXT NOT NULL,
                applied_on TEXT NOT NULL
            )
            """);
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            """
            INSERT INTO collaboration_schema_migrations_legacy (migration_id, description, applied_on)
            SELECT migration_id, description, applied_on
            FROM collaboration_schema_migrations
            """);
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync("DROP TABLE collaboration_schema_migrations");
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "ALTER TABLE collaboration_schema_migrations_legacy RENAME TO collaboration_schema_migrations");

        var inspectedSql = new List<string>();
        _dbContext.SqlSugar.Aop.OnLogExecuting = (sql, _) => inspectedSql.Add(sql);
        var upgraded = await initializer.InspectAsync(context, CancellationToken.None);

        Assert.False(upgraded.Ready);
        Assert.Contains("checksum", upgraded.Reason, StringComparison.OrdinalIgnoreCase);
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

        var upgradePlan = await initializer.PlanAsync(context, upgraded, CancellationToken.None);
        await initializer.ApplyAsync(context, upgradePlan, CancellationToken.None);
        var verified = await initializer.VerifyAsync(context, CancellationToken.None);

        Assert.True(verified.Ready, verified.Reason);
        Assert.Equal(
            Checksum(CollaborationSchemaMigrations.All[0]),
            await _dbContext.SqlSugar.Ado.GetStringAsync(
                $"SELECT checksum FROM collaboration_schema_migrations WHERE migration_id = '{CollaborationSchemaMigrations.All[0].Id}'"));
        Assert.Equal(legacyAppliedOn, await _dbContext.SqlSugar.Ado.GetStringAsync(
            "SELECT applied_on FROM collaboration_schema_migrations WHERE migration_id = 'PF05-001'"));
        Assert.Equal(CollaborationSchemaMigrations.All.Count, await _dbContext.SqlSugar.Queryable<CollaborationSchemaMigrationRecord>().CountAsync());
        Assert.Equal(1, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM collaboration_conversation"));
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("perservice")]
    public async Task Isolated_sqlite_targets_fail_closed_when_a_claimed_index_is_missing(string topology)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-{topology}-{Guid.NewGuid():N}.db");
        using var context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions { DbType = DbType.Sqlite, ConnectionString = $"Data Source={path}" }));
        var initializer = new CollaborationServiceInitializer(context);
        var serviceContext = CreateContext(topology);
        var state = await initializer.ApplyAsync(serviceContext, await initializer.PlanAsync(serviceContext, await initializer.InspectAsync(serviceContext, CancellationToken.None), CancellationToken.None), CancellationToken.None);
        Assert.True(state.Ready);

        await context.SqlSugar.Ado.ExecuteCommandAsync("DROP INDEX ix_collaboration_conversation_member_user");
        var drift = await initializer.InspectAsync(serviceContext, CancellationToken.None);
        Assert.False(drift.Ready);
        Assert.Contains("physical schema drift", drift.Reason, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        try
        {
            if (File.Exists(_databasePath))
                File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // SqlSugar's connection pool can briefly retain the SQLite file.
        }
    }

    private static ServiceInitializationContext CreateContext(string topology = "shared") => new(
        "Test",
        "tenant-1",
        "operation-1",
        "collaboration",
        "collaboration",
        new ResolvedDatabaseTarget(
            "Test",
            string.Equals(topology, "perservice", StringComparison.OrdinalIgnoreCase) ? DatabaseTopologyMode.PerService : DatabaseTopologyMode.Shared,
            "collaboration",
            DatabaseProvider.Sqlite,
            "collaboration_db",
            "target",
            false),
        "PF05-005",
        ServiceInitializationPolicy.Standard,
        "trace-1");

    private static string Checksum(CollaborationMigrationStep step) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{step.Id}|{step.Description}"))).ToLowerInvariant();
}
