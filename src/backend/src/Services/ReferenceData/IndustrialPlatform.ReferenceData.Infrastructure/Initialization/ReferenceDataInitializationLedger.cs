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

    public void EnsureTables()
    {
        _dbContext.SqlSugar.CodeFirst.InitTables<ReferenceDataSchemaMigrationRecord>();
        _dbContext.SqlSugar.CodeFirst.InitTables<ReferenceDataSeedLedgerRecord>();
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
        var record = _dbContext.SqlSugar.Queryable<ReferenceDataSeedLedgerRecord>()
            .Where(item => item.SeedKey == seedKey && item.SeedVersion == seedVersion)
            .First();
        return Task.FromResult((ReferenceDataSeedLedgerRecord?)record);
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
            Checksum = seedVersion,
            AppliedOn = DateTimeOffset.UtcNow,
            OperationNId = operationNId,
            TraceId = traceId,
        }).ExecuteCommandAsync(cancellationToken);
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

    [SugarColumn(ColumnName = "applied_on")]
    public DateTimeOffset AppliedOn { get; set; }

    [SugarColumn(ColumnName = "operation_n_id")]
    public string OperationNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "trace_id")]
    public string TraceId { get; set; } = string.Empty;
}
