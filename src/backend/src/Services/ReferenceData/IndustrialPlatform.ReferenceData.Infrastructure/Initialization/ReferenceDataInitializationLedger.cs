using IndustrialPlatform.Infrastructure.Database;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Initialization;

/// <summary>ReferenceData 服务级 Migration/Seed Ledger；不创建五个业务模块账本。</summary>
public sealed class ReferenceDataInitializationLedger
{
    private readonly SqlSugarDbContext _dbContext;

    public ReferenceDataInitializationLedger(SqlSugarDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task EnsureTablesAsync(CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        sugar.CodeFirst.InitTables<ReferenceDataSchemaMigrationRecord>();
        sugar.CodeFirst.InitTables<ReferenceDataSeedLedgerRecord>();

        var hasScope = HasScopeColumn(sugar);
        if (!hasScope)
        {
            var ifNotExists = sugar.CurrentConnectionConfig.DbType == DbType.PostgreSQL
                ? " IF NOT EXISTS"
                : string.Empty;
            cancellationToken.ThrowIfCancellationRequested();
            await sugar.Ado.ExecuteCommandAsync(
                $"ALTER TABLE reference_data_seed_ledger ADD COLUMN{ifNotExists} scope TEXT NULL;");
        }
    }

    public Task<ReferenceDataSchemaMigrationRecord?> GetMigrationAsync(CancellationToken cancellationToken)
    {
        var record = _dbContext.SqlSugar.Queryable<ReferenceDataSchemaMigrationRecord>()
            .OrderByDescending(item => item.AppliedOn)
            .First();
        return Task.FromResult((ReferenceDataSchemaMigrationRecord?)record);
    }

    public Task<ReferenceDataSeedLedgerRecord?> GetSeedAsync(string seedKey, string seedVersion, CancellationToken cancellationToken)
    {
        try
        {
            var record = _dbContext.SqlSugar.Queryable<ReferenceDataSeedLedgerRecord>()
                .Where(item => item.SeedKey == seedKey && item.SeedVersion == seedVersion)
                .First();
            return Task.FromResult((ReferenceDataSeedLedgerRecord?)record);
        }
        catch (Exception exception) when (IsMissingScopeColumn(exception))
        {
            var legacy = _dbContext.SqlSugar.Queryable<ReferenceDataSeedLedgerLegacyRecord>()
                .Where(item => item.SeedKey == seedKey && item.SeedVersion == seedVersion)
                .First();
            return Task.FromResult(legacy is null ? null : new ReferenceDataSeedLedgerRecord
            {
                SeedKey = legacy.SeedKey,
                SeedVersion = legacy.SeedVersion,
                Checksum = legacy.Checksum,
                AppliedOn = legacy.AppliedOn,
                OperationNId = legacy.OperationNId,
                TraceId = legacy.TraceId,
            });
        }
    }

    public async Task RecordMigrationAsync(string version, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Insertable(new ReferenceDataSchemaMigrationRecord
        {
            MigrationId = version,
            AppliedOn = DateTimeOffset.UtcNow,
        }).ExecuteCommandAsync(cancellationToken);
    }

    public async Task RecordSeedAsync(
        string seedKey,
        string seedVersion,
        string operationNId,
        string traceId,
        CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Insertable(new ReferenceDataSeedLedgerRecord
        {
            SeedKey = seedKey,
            SeedVersion = seedVersion,
            Checksum = ReferenceDataServiceInitializer.BaselineChecksum,
            Scope = "System",
            AppliedOn = DateTimeOffset.UtcNow,
            OperationNId = operationNId,
            TraceId = traceId,
        }).ExecuteCommandAsync(cancellationToken);
    }

    public async Task NormalizeLegacySeedAsync(
        string seedKey,
        string seedVersion,
        string checksum,
        string scope,
        CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Updateable<ReferenceDataSeedLedgerRecord>()
            .SetColumns(item => new ReferenceDataSeedLedgerRecord
            {
                Checksum = checksum,
                Scope = scope,
            })
            .Where(item => item.SeedKey == seedKey && item.SeedVersion == seedVersion)
            .ExecuteCommandAsync(cancellationToken);
    }

    private static bool IsMissingScopeColumn(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("scope", StringComparison.OrdinalIgnoreCase)
                && (message.Contains("no such column", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasScopeColumn(ISqlSugarClient sugar)
    {
        var table = sugar.CurrentConnectionConfig.DbType == DbType.Sqlite
            ? sugar.Ado.GetDataTable("PRAGMA table_info('reference_data_seed_ledger')")
            : sugar.Ado.GetDataTable(
                "SELECT column_name FROM information_schema.columns "
                + "WHERE table_schema = current_schema() AND table_name = 'reference_data_seed_ledger'");
        return table.Rows.Cast<System.Data.DataRow>().Any(row =>
            string.Equals(
                row[sugar.CurrentConnectionConfig.DbType == DbType.Sqlite ? "name" : "column_name"]?.ToString(),
                "scope",
                StringComparison.OrdinalIgnoreCase));
    }
}

[SugarTable("reference_data_schema_migrations")]
public sealed class ReferenceDataSchemaMigrationRecord
{
    [SugarColumn(IsPrimaryKey = true, ColumnName = "migration_id")]
    public string MigrationId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "applied_on")]
    public DateTimeOffset AppliedOn { get; set; }
}

[SugarTable("reference_data_seed_ledger")]
public sealed class ReferenceDataSeedLedgerRecord
{
    [SugarColumn(IsPrimaryKey = true, ColumnName = "seed_key")]
    public string SeedKey { get; set; } = string.Empty;

    [SugarColumn(IsPrimaryKey = true, ColumnName = "seed_version")]
    public string SeedVersion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "checksum")]
    public string Checksum { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "scope", IsNullable = true)]
    public string? Scope { get; set; }

    [SugarColumn(ColumnName = "applied_on")]
    public DateTimeOffset AppliedOn { get; set; }

    [SugarColumn(ColumnName = "operation_n_id")]
    public string OperationNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "trace_id")]
    public string TraceId { get; set; } = string.Empty;
}

[SugarTable("reference_data_seed_ledger")]
internal sealed class ReferenceDataSeedLedgerLegacyRecord
{
    [SugarColumn(IsPrimaryKey = true, ColumnName = "seed_key")]
    public string SeedKey { get; set; } = string.Empty;

    [SugarColumn(IsPrimaryKey = true, ColumnName = "seed_version")]
    public string SeedVersion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "checksum")]
    public string Checksum { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "applied_on")]
    public DateTimeOffset AppliedOn { get; set; }

    [SugarColumn(ColumnName = "operation_n_id")]
    public string OperationNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "trace_id")]
    public string TraceId { get; set; } = string.Empty;
}
