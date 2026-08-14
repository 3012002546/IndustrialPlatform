using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 目标库种子账本帮助器(TASK-SD-004,蓝图 §5.3):Sqlite/PostgreSQL 双提供程序分支执行
/// <see cref="SeedLedgerContracts"/> 的建表/查询/记账。账本只含脱敏元数据(键/版本/校验和/作用域/状态/时间),
/// 不含 Seed/SQL/Secret 值。执行器先查后写:同版本同校验和幂等、同版本不同校验和拒绝(drift),
/// 失败从账本可验证边界重试,升级新增版本不原位覆盖。
/// </summary>
internal static class TargetSeedLedger
{
    /// <summary>在目标库建种子账本表(幂等)。</summary>
    public static async Task EnsureTableAsync(
        TargetDatabaseConnection connection,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        if (IsSqlite(connection))
        {
            await using var conn = new SqliteConnection(BuildSqliteConnectionString(connection));
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = SeedLedgerContracts.EnsureTableSql(moduleKey, "TEXT");
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await using var pg = new NpgsqlConnection(BuildPostgreSqlConnectionString(connection));
        await pg.OpenAsync(cancellationToken);
        await using var pgCmd = pg.CreateCommand();
        pgCmd.CommandText = SeedLedgerContracts.EnsureTableSql(moduleKey, "TIMESTAMPTZ");
        await pgCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>按 (seed_key, seed_version) 读取单条账本条目;不存在返回 <c>null</c>。</summary>
    public static async Task<SeedLedgerEntry?> FindAsync(
        TargetDatabaseConnection connection,
        string moduleKey,
        string seedKey,
        string seedVersion,
        CancellationToken cancellationToken)
    {
        if (IsSqlite(connection))
        {
            await using var conn = new SqliteConnection(BuildSqliteConnectionString(connection));
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = SeedLedgerContracts.FindSql(moduleKey);
            cmd.Parameters.AddWithValue("seedKey", seedKey);
            cmd.Parameters.AddWithValue("seedVersion", seedVersion);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            return await ReadEntryAsync(reader, cancellationToken);
        }

        await using var pg = new NpgsqlConnection(BuildPostgreSqlConnectionString(connection));
        await pg.OpenAsync(cancellationToken);
        await using var pgCmd = pg.CreateCommand();
        pgCmd.CommandText = SeedLedgerContracts.FindSql(moduleKey);
        pgCmd.Parameters.AddWithValue("seedKey", seedKey);
        pgCmd.Parameters.AddWithValue("seedVersion", seedVersion);
        await using var pgReader = await pgCmd.ExecuteReaderAsync(cancellationToken);
        return await ReadEntryAsync(pgReader, cancellationToken);
    }

    /// <summary>插入单条账本条目(ServiceInitializer 等无 SQL 步骤的执行器使用;单条插入原子)。</summary>
    public static async Task InsertAsync(
        TargetDatabaseConnection connection,
        string moduleKey,
        SeedLedgerWrite write,
        CancellationToken cancellationToken)
    {
        if (IsSqlite(connection))
        {
            await using var conn = new SqliteConnection(BuildSqliteConnectionString(connection));
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            SeedLedgerContracts.BindRecordParameters(
                cmd, moduleKey, write.SeedKey, write.SeedVersion, write.Checksum, write.Scope, write.Status, write.AppliedOn, write.OperationNId, write.TraceId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await using var pg = new NpgsqlConnection(BuildPostgreSqlConnectionString(connection));
        await pg.OpenAsync(cancellationToken);
        await using var pgCmd = pg.CreateCommand();
        SeedLedgerContracts.BindRecordParameters(
            pgCmd, moduleKey, write.SeedKey, write.SeedVersion, write.Checksum, write.Scope, write.Status, write.AppliedOn, write.OperationNId, write.TraceId);
        await pgCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>事务内执行签名 SQL 种子步骤并记账,全部成功才提交(失败回滚,不记账,可验证边界重试)。</summary>
    public static async Task<DateTimeOffset> ApplySqlBundleAsync(
        TargetDatabaseConnection connection,
        string moduleKey,
        IReadOnlyList<SeedArtifactStep> steps,
        SeedLedgerWrite write,
        CancellationToken cancellationToken)
    {
        if (IsSqlite(connection))
        {
            await using var conn = new SqliteConnection(BuildSqliteConnectionString(connection));
            await conn.OpenAsync(cancellationToken);
            await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var step in steps.OrderBy(step => step.Sequence))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = (SqliteTransaction)transaction;
                    cmd.CommandText = step.Sql;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await using var recordCmd = conn.CreateCommand();
                recordCmd.Transaction = (SqliteTransaction)transaction;
                SeedLedgerContracts.BindRecordParameters(
                    recordCmd, moduleKey, write.SeedKey, write.SeedVersion, write.Checksum, write.Scope, write.Status, write.AppliedOn, write.OperationNId, write.TraceId);
                await recordCmd.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return write.AppliedOn!.Value;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        await using var pg = new NpgsqlConnection(BuildPostgreSqlConnectionString(connection));
        await pg.OpenAsync(cancellationToken);
        await using var pgTransaction = await pg.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var step in steps.OrderBy(step => step.Sequence))
            {
                await using var cmd = pg.CreateCommand();
                cmd.Transaction = pgTransaction;
                cmd.CommandText = step.Sql;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var pgRecordCmd = pg.CreateCommand();
            pgRecordCmd.Transaction = pgTransaction;
            SeedLedgerContracts.BindRecordParameters(
                pgRecordCmd, moduleKey, write.SeedKey, write.SeedVersion, write.Checksum, write.Scope, write.Status, write.AppliedOn, write.OperationNId, write.TraceId);
            await pgRecordCmd.ExecuteNonQueryAsync(cancellationToken);
            await pgTransaction.CommitAsync(cancellationToken);
            return write.AppliedOn!.Value;
        }
        catch
        {
            await pgTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<SeedLedgerEntry?> ReadEntryAsync(System.Data.Common.DbDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SeedLedgerEntry(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            (SeedScope)reader.GetInt32(3),
            (SeedStatus)reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    private static bool IsSqlite(TargetDatabaseConnection connection) =>
        string.Equals(connection.Provider, "Sqlite", StringComparison.Ordinal);

    private static string BuildSqliteConnectionString(TargetDatabaseConnection connection) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = connection.Database,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

    private static string BuildPostgreSqlConnectionString(TargetDatabaseConnection connection)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            SearchPath = string.IsNullOrWhiteSpace(connection.Schema) ? "public" : connection.Schema,
            Username = connection.Username,
            Password = connection.Password,
            Timeout = 15,
        };
        return builder.ConnectionString;
    }
}

/// <summary>种子账本写入参数(纯内存;AppliedOn 为实际应用时间,账本只记脱敏元数据)。</summary>
internal sealed record SeedLedgerWrite(
    string SeedKey,
    string SeedVersion,
    string Checksum,
    SeedScope Scope,
    SeedStatus Status,
    DateTimeOffset? AppliedOn,
    string? OperationNId,
    string TraceId);
