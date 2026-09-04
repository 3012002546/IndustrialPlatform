using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
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
        Assert.True(await TableExistsAsync("reference_data_schema_migrations"));
        Assert.True(await TableExistsAsync("reference_data_seed_ledger"));
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

    private static ServiceInitializationContext CreateContext() => new(
        "Test",
        "tenant-1",
        "operation-1",
        "referencedata",
        "referencedata",
        new IndustrialPlatform.SharedKernel.Topology.ResolvedDatabaseTarget(
            "Test",
            IndustrialPlatform.SharedKernel.Topology.DatabaseTopologyMode.Shared,
            "referencedata",
            IndustrialPlatform.SharedKernel.Topology.DatabaseProvider.Sqlite,
            "referencedata_db",
            "target",
            false),
        ReferenceDataServiceInitializer.BaselineVersion,
        ServiceInitializationPolicy.Standard,
        "trace-1");
}
