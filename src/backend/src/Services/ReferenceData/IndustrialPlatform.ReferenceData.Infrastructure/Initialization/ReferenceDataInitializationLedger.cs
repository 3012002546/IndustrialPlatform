using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Topology;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Initialization;

/// <summary>One explicit, checksummed migration stream for the ReferenceData service.</summary>
public sealed class ReferenceDataInitializationLedger(SqlSugarDbContext dbContext)
{
    private static readonly SemaphoreSlim MigrationGate = new(1, 1);
    private ISqlSugarClient Db => dbContext.SqlSugar;
    private bool PostgreSql => Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL;
    internal string Table(string name) => PostgreSql ? $"reference_data.{name}" : $"reference_data_{name}";

    public bool MatchesTarget(ServiceInitializationContext context)
    {
        var target = context.DatabaseTarget;
        if (context.ServiceKey != "referencedata" || context.ModuleKey != "referencedata"
            || target.ServiceKey != "referencedata" || target.LogicalDatabaseName != "referencedata_db"
            || target.EnvironmentName != context.EnvironmentName
            || (target.Mode == DatabaseTopologyMode.Shared && context.EnvironmentName != "Development")
            || target.IsSharedPhysicalDatabase != (target.Mode == DatabaseTopologyMode.Shared)
            || target.Provider != (PostgreSql ? DatabaseProvider.PostgreSQL : DatabaseProvider.Sqlite)) return false;
        var connection = new DbConnectionStringBuilder { ConnectionString = Db.CurrentConnectionConfig.ConnectionString };
        if (!connection.TryGetValue(PostgreSql ? "Database" : "Data Source", out var value)) return false;
        var actual = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return PostgreSql
            ? string.Equals(actual, target.PhysicalDatabaseName, StringComparison.Ordinal)
            : string.Equals(Path.GetFullPath(actual), Path.GetFullPath(target.PhysicalDatabaseName), StringComparison.OrdinalIgnoreCase);
    }

    public string LocalTargetIdentity
    {
        get
        {
            var connection = new DbConnectionStringBuilder { ConnectionString = Db.CurrentConnectionConfig.ConnectionString };
            var physical = Convert.ToString(connection[PostgreSql ? "Database" : "Data Source"], CultureInfo.InvariantCulture)!;
            if (!PostgreSql) physical = Path.GetFullPath(physical).ToUpperInvariant();
            return Hash($"referencedata_db|{Db.CurrentConnectionConfig.DbType}|{physical}");
        }
    }

    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<ReferenceDataSchemaMigrationRecord?> GetMigrationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await TableExistsAsync("schema_migrations")) return null;
        var ado = Db.Ado;
        try
        {
            var rows = await ado.SqlQueryAsync<ReferenceDataSchemaMigrationRecord>(
                $"SELECT migration_id AS MigrationId, applied_on AS AppliedOn FROM {Table("schema_migrations")} ORDER BY applied_on DESC, migration_id DESC LIMIT 1");
            return rows.FirstOrDefault();
        }
        catch
        {
            // SqlSugar 5.1.4 leaves failed reader connections open. Close this provider, not another async scope.
            if (ado.Transaction is null) ado.Connection.Close();
            throw;
        }
    }

    public async Task<ReferenceDataSeedLedgerRecord?> GetSeedAsync(string seedKey, string seedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await TableExistsAsync("seed_ledger")) return null;
        var scope = HasColumn("seed_ledger", "scope") ? "scope" : "NULL";
        var ado = Db.Ado;
        try
        {
            var rows = await ado.SqlQueryAsync<ReferenceDataSeedLedgerRecord>(
                $"SELECT seed_key AS SeedKey, seed_version AS SeedVersion, checksum AS Checksum, {scope} AS Scope, applied_on AS AppliedOn, operation_n_id AS OperationNId, trace_id AS TraceId FROM {Table("seed_ledger")} WHERE seed_key=@key AND seed_version=@version",
                new SugarParameter("@key", seedKey), new SugarParameter("@version", seedVersion));
            return rows.FirstOrDefault();
        }
        catch
        {
            if (ado.Transaction is null) ado.Connection.Close();
            throw;
        }
    }

    private async Task<bool> TableExistsAsync(string name) => PostgreSql
        ? await Db.Ado.GetIntAsync("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='reference_data' AND table_name=@name", new SugarParameter("@name", name)) == 1
        : await Db.Ado.GetIntAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name", new SugarParameter("@name", Table(name))) == 1;

    public async Task<bool> MigrationValidAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasColumn("schema_migrations", "checksum") || !HasColumn("schema_migrations", "target_identity")) return false;
        if (!await MigrationChecksumMatchesAsync(
                ReferenceDataServiceInitializer.BaselineVersion, SchemaSql, cancellationToken)) return false;
        foreach (var script in Persistence.ReferenceDataMigrations.Scripts(PostgreSql))
            if (!await MigrationChecksumMatchesAsync(script.Version, script.Sql, cancellationToken)) return false;
        return true;
    }

    public async Task ApplyAsync(ServiceInitializationContext context, CancellationToken cancellationToken)
    {
        await MigrationGate.WaitAsync(cancellationToken);
        try
        {
            if (!PostgreSql) await Db.Ado.ExecuteCommandAsync("PRAGMA busy_timeout=10000");
            await Db.Ado.BeginTranAsync();
            try
            {
                if (PostgreSql)
                {
                    await Db.Ado.ExecuteCommandAsync("SELECT pg_advisory_xact_lock(hashtext('industrial-platform-schema-migration'))");
                    await Db.Ado.ExecuteCommandAsync("CREATE SCHEMA IF NOT EXISTS reference_data");
                }
                await Db.Ado.ExecuteCommandAsync(SchemaSql);
                await AddLegacyColumnAsync("schema_migrations", "checksum");
                await AddLegacyColumnAsync("schema_migrations", "target_identity");
                await AddLegacyColumnAsync("seed_ledger", "scope");
                await ImportPostgresLegacyAsync();
                var exists = await Db.Ado.GetIntAsync($"SELECT COUNT(*) FROM {Table("schema_migrations")} WHERE migration_id=@version",
                    new SugarParameter("@version", ReferenceDataServiceInitializer.BaselineVersion));
                if (exists > 0 && !await MigrationChecksumMatchesAsync(
                        ReferenceDataServiceInitializer.BaselineVersion, SchemaSql, cancellationToken))
                    throw new InvalidOperationException("REF-INITIALIZATION-DRIFT");
                if (exists == 0)
                    await Db.Ado.ExecuteCommandAsync(
                        $"INSERT INTO {Table("schema_migrations")} (migration_id,checksum,target_identity,applied_on) VALUES (@version,@checksum,@target,@now)",
                        new SugarParameter("@version", ReferenceDataServiceInitializer.BaselineVersion), new SugarParameter("@checksum", SchemaChecksum),
                        new SugarParameter("@target", LocalTargetIdentity), new SugarParameter("@now", DateTimeOffset.UtcNow));
                foreach (var script in Persistence.ReferenceDataMigrations.Scripts(PostgreSql))
                {
                    var count = await Db.Ado.GetIntAsync($"SELECT COUNT(*) FROM {Table("schema_migrations")} WHERE migration_id=@version", new SugarParameter("@version", script.Version));
                    if (count > 0)
                    {
                        if (!await MigrationChecksumMatchesAsync(
                                script.Version, script.Sql, cancellationToken))
                            throw new InvalidOperationException("REF-INITIALIZATION-DRIFT");
                        continue;
                    }
                    await Db.Ado.ExecuteCommandAsync(script.Sql);
                    await Db.Ado.ExecuteCommandAsync($"INSERT INTO {Table("schema_migrations")} (migration_id,checksum,target_identity,applied_on) VALUES (@version,@checksum,@target,@now)",
                        new SugarParameter("@version", script.Version), new SugarParameter("@checksum", MigrationChecksum(script.Sql)),
                        new SugarParameter("@target", LocalTargetIdentity), new SugarParameter("@now", DateTimeOffset.UtcNow));
                }
                var seed = await GetSeedAsync(ReferenceDataServiceInitializer.BaselineSeedKey, ReferenceDataServiceInitializer.BaselineVersion, cancellationToken);
                if (seed is null)
                    await Db.Ado.ExecuteCommandAsync(
                        $"INSERT INTO {Table("seed_ledger")} (seed_key,seed_version,checksum,scope,applied_on,operation_n_id,trace_id) VALUES (@key,@version,@checksum,'System',@now,@operation,@trace)",
                        new SugarParameter("@key", ReferenceDataServiceInitializer.BaselineSeedKey), new SugarParameter("@version", ReferenceDataServiceInitializer.BaselineVersion),
                        new SugarParameter("@checksum", ReferenceDataServiceInitializer.BaselineChecksum), new SugarParameter("@now", DateTimeOffset.UtcNow),
                        new SugarParameter("@operation", context.OperationNId), new SugarParameter("@trace", context.TraceId));
                else if (seed.Checksum == ReferenceDataServiceInitializer.BaselineVersion
                    || (seed.Checksum == ReferenceDataServiceInitializer.BaselineChecksum && string.IsNullOrWhiteSpace(seed.Scope)))
                    await Db.Ado.ExecuteCommandAsync(
                        $"UPDATE {Table("seed_ledger")} SET checksum=@checksum,scope='System' WHERE seed_key=@key AND seed_version=@version",
                        new SugarParameter("@checksum", ReferenceDataServiceInitializer.BaselineChecksum),
                        new SugarParameter("@key", seed.SeedKey), new SugarParameter("@version", seed.SeedVersion));
                cancellationToken.ThrowIfCancellationRequested();
                await Db.Ado.CommitTranAsync();
            }
            catch
            {
                await Db.Ado.RollbackTranAsync();
                throw;
            }
        }
        finally { MigrationGate.Release(); }
    }

    private string SchemaSql => $"""
        CREATE TABLE IF NOT EXISTS {Table("schema_migrations")} (
            migration_id TEXT PRIMARY KEY NOT NULL, checksum TEXT NULL, target_identity TEXT NULL,
            applied_on {(PostgreSql ? "timestamptz" : "TEXT")} NOT NULL
        );
        CREATE TABLE IF NOT EXISTS {Table("seed_ledger")} (
            seed_key TEXT NOT NULL, seed_version TEXT NOT NULL, checksum TEXT NOT NULL, scope TEXT NULL,
            applied_on {(PostgreSql ? "timestamptz" : "TEXT")} NOT NULL, operation_n_id TEXT NOT NULL, trace_id TEXT NOT NULL,
            PRIMARY KEY (seed_key,seed_version)
        );
        """;
    private string SchemaChecksum => MigrationChecksum(SchemaSql);

    private async Task<bool> MigrationChecksumMatchesAsync(
        string version, string sql, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var canonical = MigrationChecksum(sql);
        var windows = Hash(sql.ReplaceLineEndings("\r\n"));
        var raw = Hash(sql);
        return await Db.Ado.GetIntAsync(
            $"SELECT COUNT(*) FROM {Table("schema_migrations")} "
            + "WHERE migration_id=@version AND target_identity=@target "
            + "AND (checksum=@canonical OR checksum=@windows OR checksum=@raw)",
            new SugarParameter("@version", version),
            new SugarParameter("@target", LocalTargetIdentity),
            new SugarParameter("@canonical", canonical),
            new SugarParameter("@windows", windows),
            new SugarParameter("@raw", raw)) == 1;
    }

    private static string MigrationChecksum(string sql) => Hash(sql.ReplaceLineEndings("\n"));

    private bool HasColumn(string table, string column)
    {
        var rows = PostgreSql
            ? Db.Ado.GetDataTable("SELECT column_name AS name FROM information_schema.columns WHERE table_schema='reference_data' AND table_name=@table", new SugarParameter("@table", table))
            : Db.Ado.GetDataTable($"PRAGMA table_info('{Table(table)}')");
        return rows.Rows.Cast<System.Data.DataRow>().Any(row => string.Equals(row["name"]?.ToString(), column, StringComparison.Ordinal));
    }

    private async Task AddLegacyColumnAsync(string table, string column)
    {
        if (!HasColumn(table, column)) await Db.Ado.ExecuteCommandAsync($"ALTER TABLE {Table(table)} ADD COLUMN {column} TEXT NULL");
    }

    private async Task ImportPostgresLegacyAsync()
    {
        if (!PostgreSql) return;
        // Preserve the original ledger; import only its known structural columns.
        if (await Db.Ado.GetIntAsync("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_name='reference_data_schema_migrations'") == 1)
            await Db.Ado.ExecuteCommandAsync("INSERT INTO reference_data.schema_migrations (migration_id,applied_on) SELECT migration_id,applied_on FROM public.reference_data_schema_migrations ON CONFLICT DO NOTHING");
        if (await Db.Ado.GetIntAsync("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_name='reference_data_seed_ledger'") == 1)
        {
            var hasScope = await Db.Ado.GetIntAsync("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='reference_data_seed_ledger' AND column_name='scope'") == 1;
            await Db.Ado.ExecuteCommandAsync($"INSERT INTO reference_data.seed_ledger (seed_key,seed_version,checksum,scope,applied_on,operation_n_id,trace_id) SELECT seed_key,seed_version,checksum,{(hasScope ? "scope" : "NULL")},applied_on,operation_n_id,trace_id FROM public.reference_data_seed_ledger ON CONFLICT DO NOTHING");
        }
    }
}

public sealed class ReferenceDataSchemaMigrationRecord
{
    public string MigrationId { get; set; } = string.Empty;
    public DateTimeOffset AppliedOn { get; set; }
}

public sealed class ReferenceDataSeedLedgerRecord
{
    public string SeedKey { get; set; } = string.Empty;
    public string SeedVersion { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public string? Scope { get; set; }
    public DateTimeOffset AppliedOn { get; set; }
    public string OperationNId { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
}
