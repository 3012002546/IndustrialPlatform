using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.ReferenceData.Infrastructure.Persistence;
using IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class ReferenceDataServiceInitializerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-referencedata-initializer-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext _dbContext;
    private readonly ReferenceDataServiceInitializer _initializer;

    public ReferenceDataServiceInitializerTests()
    {
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath}",
            DbType = DbType.Sqlite,
        }));
        _initializer = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(_dbContext));
    }

    [Fact]
    public async Task Wrong_physical_target_is_rejected_without_writing_initialization_tables()
    {
        var context = CreateContext();
        context = context with { DatabaseTarget = context.DatabaseTarget with { PhysicalDatabaseName = "another-target.db" } };
        var inspection = await _initializer.InspectAsync(context, CancellationToken.None);
        var plan = await _initializer.PlanAsync(context, inspection, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _initializer.ApplyAsync(context, plan, CancellationToken.None));
        Assert.False(await TableExistsAsync("reference_data_schema_migrations"));
    }

    [Fact]
    public async Task Unknown_desired_version_is_rejected_without_writing_initialization_tables()
    {
        var context = CreateContext() with { DesiredVersion = "unknown-future-version" };
        var inspection = await _initializer.InspectAsync(context, CancellationToken.None);
        var plan = await _initializer.PlanAsync(context, inspection, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _initializer.ApplyAsync(context, plan, CancellationToken.None));
        Assert.False(await TableExistsAsync("reference_data_schema_migrations"));
    }

    [Fact]
    public async Task Inspect_does_not_create_tables_and_apply_then_verify_is_ready()
    {
        var context = CreateContext();

        var inspection = await _initializer.InspectAsync(context, CancellationToken.None);

        Assert.False(inspection.Ready);
        Assert.False(await TableExistsAsync("reference_data_schema_migrations"));
        Assert.False(await TableExistsAsync("reference_data_seed_ledger"));

        var plan = await _initializer.PlanAsync(context, inspection, CancellationToken.None);
        await _initializer.ApplyAsync(context, plan, CancellationToken.None);
        var verified = await _initializer.VerifyAsync(context, CancellationToken.None);

        Assert.True(verified.Ready);
        var seed = await new ReferenceDataInitializationLedger(_dbContext).GetSeedAsync(
            ReferenceDataServiceInitializer.BaselineSeedKey,
            ReferenceDataServiceInitializer.BaselineVersion,
            CancellationToken.None);
        Assert.Equal(ReferenceDataServiceInitializer.BaselineChecksum, seed!.Checksum);
        Assert.Equal("System", seed.Scope);
        var unitSeed = await new ReferenceDataInitializationLedger(_dbContext).GetSeedAsync(
            UnitOfMeasureSystemSeed.SeedKey,
            UnitOfMeasureSystemSeed.SeedVersion,
            CancellationToken.None);
        Assert.Equal(UnitOfMeasureSystemSeed.Checksum, unitSeed!.Checksum);
        Assert.Equal("System", unitSeed.Scope);
        Assert.Equal(6, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM reference_data_unit_of_measure_dimension"));
        Assert.Equal(12, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM reference_data_unit_of_measure_unit"));
        Assert.True(await TableExistsAsync("reference_data_schema_migrations"));
        Assert.True(await TableExistsAsync("reference_data_seed_ledger"));
        Assert.Equal(1, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='dictionary_draft_uq'"));
        Assert.Equal(ReferenceDataServiceInitializer.CurrentVersion,
            await _dbContext.SqlSugar.Ado.GetStringAsync(
                "SELECT migration_id FROM reference_data_schema_migrations "
                + "ORDER BY applied_on DESC,migration_id DESC LIMIT 1"));
    }

    [Fact]
    public async Task Apply_replays_missing_system_units_without_duplicate_seed_ledger_rows()
    {
        var context = CreateContext();
        var inspection = await _initializer.InspectAsync(context, CancellationToken.None);
        var plan = await _initializer.PlanAsync(context, inspection, CancellationToken.None);
        await _initializer.ApplyAsync(context, plan, CancellationToken.None);

        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "DELETE FROM reference_data_unit_of_measure_unit WHERE n_id='G'");
        Assert.False((await _initializer.InspectAsync(context, CancellationToken.None)).Ready);

        var repairInspection = await _initializer.InspectAsync(context, CancellationToken.None);
        await _initializer.ApplyAsync(
            context,
            await _initializer.PlanAsync(context, repairInspection, CancellationToken.None),
            CancellationToken.None);
        await _initializer.ApplyAsync(context, plan, CancellationToken.None);

        Assert.True((await _initializer.VerifyAsync(context, CancellationToken.None)).Ready);
        Assert.Equal(1, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM reference_data_seed_ledger "
            + "WHERE seed_key='reference-data.unit-of-measure.system' AND seed_version='1'"));
        Assert.Equal(1, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM reference_data_unit_of_measure_unit WHERE n_id='G'"));
    }

    [Fact]
    public async Task Verify_accepts_legacy_migration_checksums_that_only_differ_by_line_endings()
    {
        var context = CreateContext();
        var inspection = await _initializer.InspectAsync(context, CancellationToken.None);
        var plan = await _initializer.PlanAsync(context, inspection, CancellationToken.None);
        await _initializer.ApplyAsync(context, plan, CancellationToken.None);

        foreach (var lineEnding in new[] { "\r\n", "\n" })
        {
            var checksum = ReferenceDataInitializationLedger.Hash(
                UnitOfMeasureMigration.Sql(false).ReplaceLineEndings(lineEnding));
            await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
                "UPDATE reference_data_schema_migrations SET checksum=@checksum WHERE migration_id=@version",
                new SugarParameter("@checksum", checksum),
                new SugarParameter("@version", UnitOfMeasureMigration.Version));

            Assert.True((await _initializer.VerifyAsync(context, CancellationToken.None)).Ready);
        }
    }

    [Fact]
    public async Task Integrity_migration_disables_older_duplicate_drafts_before_creating_the_unique_index()
    {
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync("""
            CREATE TABLE reference_data_dictionary_definition (
                id TEXT PRIMARY KEY NOT NULL,
                tenant_nid TEXT NULL,
                n_id TEXT NOT NULL,
                revision INTEGER NOT NULL,
                status TEXT NOT NULL,
                is_deleted INTEGER NOT NULL,
                last_updated_on TEXT NOT NULL
            );
            INSERT INTO reference_data_dictionary_definition
                (id,tenant_nid,n_id,revision,status,is_deleted,last_updated_on)
            VALUES ('old','TENANT-A','STATUS',1,'Draft',0,'2026-09-04T00:00:00Z'),
                   ('new','TENANT-A','STATUS',2,'Draft',0,'2026-09-05T00:00:00Z');
            """);

        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(ReferenceDataIntegrityMigration.Sql(false));

        Assert.Equal("Disabled", await _dbContext.SqlSugar.Ado.GetStringAsync(
            "SELECT status FROM reference_data_dictionary_definition WHERE id='old'"));
        Assert.Equal("Draft", await _dbContext.SqlSugar.Ado.GetStringAsync(
            "SELECT status FROM reference_data_dictionary_definition WHERE id='new'"));
        Assert.Equal(1, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='dictionary_draft_uq'"));
        await Assert.ThrowsAnyAsync<Exception>(() => _dbContext.SqlSugar.Ado.ExecuteCommandAsync("""
            INSERT INTO reference_data_dictionary_definition
                (id,tenant_nid,n_id,revision,status,is_deleted,last_updated_on)
            VALUES ('third','TENANT-A','STATUS',3,'Draft',0,'2026-09-05T00:00:00Z')
            """));
    }

    [Fact]
    public async Task Inspect_old_schema_is_read_only_and_apply_upgrades_then_normalizes_known_legacy_seed()
    {
        await CreateLegacySchemaAsync(ReferenceDataServiceInitializer.BaselineVersion);
        var context = CreateContext();

        var inspection = await _initializer.InspectAsync(context, CancellationToken.None);

        Assert.False(inspection.Ready);
        Assert.False(HasScopeColumn());
        var plan = await _initializer.PlanAsync(context, inspection, CancellationToken.None);

        await _initializer.ApplyAsync(context, plan, CancellationToken.None);
        var verified = await _initializer.VerifyAsync(context, CancellationToken.None);

        Assert.True(verified.Ready);
        Assert.True(HasScopeColumn());
        var seed = await new ReferenceDataInitializationLedger(_dbContext).GetSeedAsync(
            ReferenceDataServiceInitializer.BaselineSeedKey,
            ReferenceDataServiceInitializer.BaselineVersion,
            CancellationToken.None);
        Assert.Equal(ReferenceDataServiceInitializer.BaselineChecksum, seed!.Checksum);
        Assert.Equal("System", seed.Scope);
    }

    [Fact]
    public async Task Apply_old_schema_does_not_overwrite_unknown_seed_checksum()
    {
        const string unknownChecksum = "unknown-legacy-checksum";
        await CreateLegacySchemaAsync(unknownChecksum);
        var context = CreateContext();

        var inspection = await _initializer.InspectAsync(context, CancellationToken.None);
        var plan = await _initializer.PlanAsync(context, inspection, CancellationToken.None);
        var applied = await _initializer.ApplyAsync(context, plan, CancellationToken.None);

        Assert.False(applied.Ready);
        var seed = await new ReferenceDataInitializationLedger(_dbContext).GetSeedAsync(
            ReferenceDataServiceInitializer.BaselineSeedKey,
            ReferenceDataServiceInitializer.BaselineVersion,
            CancellationToken.None);
        Assert.Equal(unknownChecksum, seed!.Checksum);
        Assert.Null(seed.Scope);
    }

    [Theory]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    [InlineData("PrOdUcTiOn")]
    public async Task Production_requires_advanced_policy_regardless_of_environment_name_casing(
        string environmentName)
    {
        var context = CreateContext();
        context = context with
        {
            EnvironmentName = environmentName,
            DatabaseTarget = context.DatabaseTarget with { EnvironmentName = environmentName },
        };
        var inspection = await _initializer.InspectAsync(context, CancellationToken.None);
        var plan = await _initializer.PlanAsync(context, inspection, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _initializer.ApplyAsync(context, plan, CancellationToken.None));

        Assert.Equal("REF-INITIALIZATION-ADVANCED-REQUIRED", exception.Message);
        Assert.False(await TableExistsAsync("reference_data_schema_migrations"));
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

    private async Task<bool> TableExistsAsync(string tableName) =>
        await _dbContext.SqlSugar.Ado.GetIntAsync(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'") == 1;

    private bool HasScopeColumn() =>
        _dbContext.SqlSugar.Ado
            .GetDataTable("PRAGMA table_info('reference_data_seed_ledger')")
            .Rows
            .Cast<System.Data.DataRow>()
            .Any(row => string.Equals(row["name"]?.ToString(), "scope", StringComparison.OrdinalIgnoreCase));

    private async Task CreateLegacySchemaAsync(string checksum)
    {
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync("""
            CREATE TABLE reference_data_schema_migrations (
                migration_id TEXT PRIMARY KEY NOT NULL,
                applied_on TEXT NOT NULL
            );
            CREATE TABLE reference_data_seed_ledger (
                seed_key TEXT NOT NULL,
                seed_version TEXT NOT NULL,
                checksum TEXT NOT NULL,
                applied_on TEXT NOT NULL,
                operation_n_id TEXT NOT NULL,
                trace_id TEXT NOT NULL,
                PRIMARY KEY (seed_key, seed_version)
            );
            INSERT INTO reference_data_schema_migrations (migration_id, applied_on)
            VALUES ('reference-data-baseline-v1', '2026-09-03T00:00:00.0000000+00:00');
            """);
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "INSERT INTO reference_data_seed_ledger (seed_key, seed_version, checksum, applied_on, operation_n_id, trace_id) "
            + "VALUES (@seedKey, @seedVersion, @checksum, @appliedOn, @operationNId, @traceId);",
            new SugarParameter("@seedKey", ReferenceDataServiceInitializer.BaselineSeedKey),
            new SugarParameter("@seedVersion", ReferenceDataServiceInitializer.BaselineVersion),
            new SugarParameter("@checksum", checksum),
            new SugarParameter("@appliedOn", "2026-09-03T00:00:00.0000000+00:00"),
            new SugarParameter("@operationNId", "legacy-operation"),
            new SugarParameter("@traceId", "legacy-trace"));
    }

    private ServiceInitializationContext CreateContext() => new(
        "Test",
        "tenant-1",
        "operation-1",
        "referencedata",
        "referencedata",
        new IndustrialPlatform.SharedKernel.Topology.ResolvedDatabaseTarget(
            "Test",
            IndustrialPlatform.SharedKernel.Topology.DatabaseTopologyMode.PerService,
            "referencedata",
            IndustrialPlatform.SharedKernel.Topology.DatabaseProvider.Sqlite,
            "referencedata_db",
            _dbPath,
            false),
        ReferenceDataServiceInitializer.CurrentVersion,
        ServiceInitializationPolicy.Standard,
        "trace-1");
}
