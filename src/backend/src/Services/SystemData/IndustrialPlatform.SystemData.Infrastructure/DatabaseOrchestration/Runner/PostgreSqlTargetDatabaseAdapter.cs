using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.Topology;
using Npgsql;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// PostgreSQL 目标数据库适配器(05 方案 §7.1.5,裸 Npgsql)。inspect/provision/migrate/advisory lock
/// 全链路;目标 SQL 全参数化或经标识转义,绝不用字符串拼接拼接 Secret;记账走目标库自建账本表。
/// </summary>
public sealed class PostgreSqlTargetDatabaseAdapter : ITargetDatabaseInspector, ITargetDatabaseProvisioner, IMigrationExecutor, ITargetDatabaseAdvisoryLock
{
    /// <summary>目标服务角色名前缀。</summary>
    private const string RoleNamePrefix = "sd";

    /// <summary>advisory lock 轮询间隔(毫秒)。</summary>
    private const int LockPollIntervalMilliseconds = 200;

    /// <inheritdoc />
    public async Task<DatabaseTargetInspection> InspectAsync(
        ResolvedDatabaseTarget target,
        DatabaseTargetCredentials credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var connection = credentials.Migrator ?? credentials.Admin;
        if (connection is null)
        {
            return new DatabaseTargetInspection(DatabaseExists: false, CurrentVersion: null, null, []);
        }

        if (!await TryConnectAsync(connection, cancellationToken))
        {
            return new DatabaseTargetInspection(DatabaseExists: false, CurrentVersion: null, null, []);
        }

        if (!await LedgerTableExistsAsync(connection, cancellationToken))
        {
            return new DatabaseTargetInspection(DatabaseExists: true, CurrentVersion: null, null, []);
        }

        var currentVersion = await ReadScalarAsync<string?>(connection, MigrationLedgerContracts.LatestVersionSql, cancellationToken);
        var appliedIds = await ReadAppliedStepIdsAsync(connection, cancellationToken);
        return new DatabaseTargetInspection(DatabaseExists: true, currentVersion, null, appliedIds);
    }

    /// <inheritdoc />
    public async Task<DatabaseProvisionOutcome> EnsureDatabaseAsync(
        ResolvedDatabaseTarget target,
        TargetDatabaseConnection admin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(admin);

        var databaseName = target.PhysicalDatabaseName;
        if (await DatabaseExistsAsync(admin, databaseName, cancellationToken))
        {
            return new DatabaseProvisionOutcome(DatabaseCreated: false, DatabaseExisted: true);
        }

        await using var conn = new NpgsqlConnection(BuildSystemConnectionString(admin));
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)}";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return new DatabaseProvisionOutcome(DatabaseCreated: true, DatabaseExisted: false);
    }

    /// <inheritdoc />
    public async Task<ProvisionedRoles> EnsureRolesAsync(
        ResolvedDatabaseTarget target,
        TargetDatabaseConnection admin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(admin);

        var databaseName = target.PhysicalDatabaseName;
        var migratorName = $"{RoleNamePrefix}_{target.ServiceKey}_migrator";
        var runtimeName = $"{RoleNamePrefix}_{target.ServiceKey}_runtime";
        var migratorPassword = GeneratePassword();
        var runtimePassword = GeneratePassword();

        await using var conn = new NpgsqlConnection(BuildConnectionString(admin with { Database = databaseName }));
        await conn.OpenAsync(cancellationToken);

        await EnsureRoleAsync(conn, migratorName, migratorPassword, cancellationToken);
        await EnsureRoleAsync(conn, runtimeName, runtimePassword, cancellationToken);

        await GrantAsync(conn, $"GRANT CONNECT ON DATABASE {QuoteIdentifier(databaseName)} TO {QuoteIdentifier(migratorName)}, {QuoteIdentifier(runtimeName)}", cancellationToken);
        await GrantAsync(conn, $"GRANT USAGE ON SCHEMA public TO {QuoteIdentifier(migratorName)}, {QuoteIdentifier(runtimeName)}", cancellationToken);
        await GrantAsync(conn, $"GRANT CREATE ON SCHEMA public TO {QuoteIdentifier(migratorName)}", cancellationToken);
        await GrantAsync(conn, $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {QuoteIdentifier(runtimeName)}", cancellationToken);
        await GrantAsync(conn, $"GRANT SELECT, USAGE ON ALL SEQUENCES IN SCHEMA public TO {QuoteIdentifier(runtimeName)}", cancellationToken);

        return new ProvisionedRoles(
            new TargetDatabaseConnection("PostgreSQL", admin.Host, admin.Port, databaseName, "public", migratorName, migratorPassword),
            new TargetDatabaseConnection("PostgreSQL", admin.Host, admin.Port, databaseName, "public", runtimeName, runtimePassword));
    }

    /// <inheritdoc />
    public async Task<MigrationExecutionResult> ApplyAsync(
        ResolvedDatabaseTarget target,
        DatabaseMigrationArtifact artifact,
        TargetDatabaseConnection migrator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(migrator);

        await using var conn = new NpgsqlConnection(BuildConnectionString(migrator));
        await conn.OpenAsync(cancellationToken);
        await EnsureLedgerTableAsync(conn, cancellationToken);

        var appliedIds = await ReadAppliedStepIdsAsync(migrator, cancellationToken);
        var appliedSet = appliedIds.ToHashSet(StringComparer.Ordinal);
        var pending = artifact.Steps
            .Where(step => !appliedSet.Contains(step.StepId))
            .OrderBy(step => step.Sequence)
            .ToList();

        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            var appliedCount = 0;
            foreach (var step in pending)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = step.Sql;
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                await using var recordCmd = conn.CreateCommand();
                recordCmd.Transaction = transaction;
                recordCmd.CommandText = MigrationLedgerContracts.RecordStepSql;
                recordCmd.Parameters.AddWithValue("migrationId", step.StepId);
                recordCmd.Parameters.AddWithValue("version", artifact.Version);
                recordCmd.Parameters.AddWithValue("appliedOn", DateTimeOffset.UtcNow);
                await recordCmd.ExecuteNonQueryAsync(cancellationToken);
                appliedCount++;
            }

            await transaction.CommitAsync(cancellationToken);
            return new MigrationExecutionResult(appliedCount, artifact.Version);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IDisposable?> AcquireAsync(
        DatabaseTargetLockKey key,
        TargetDatabaseConnection connection,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(connection);

        var lockId = DeriveLockId(key);
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var conn = new NpgsqlConnection(BuildConnectionString(connection));
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@lockId)";
            cmd.Parameters.AddWithValue("lockId", lockId);
            var raw = await cmd.ExecuteScalarAsync(cancellationToken);
            if (raw is bool acquired && acquired)
            {
                return new PostgreSqlAdvisoryLockHandle(conn, lockId);
            }

            await conn.DisposeAsync();
            if (DateTimeOffset.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(LockPollIntervalMilliseconds, cancellationToken);
        }
    }

    // ===== 私有辅助 =====

    private static string BuildConnectionString(TargetDatabaseConnection connection)
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

    private static string BuildSystemConnectionString(TargetDatabaseConnection connection) =>
        BuildConnectionString(connection with { Database = "postgres" });

    private static async Task<bool> TryConnectAsync(TargetDatabaseConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = new NpgsqlConnection(BuildConnectionString(connection));
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task<bool> LedgerTableExistsAsync(TargetDatabaseConnection connection, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(BuildConnectionString(connection));
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @name";
        cmd.Parameters.AddWithValue("name", MigrationLedgerContracts.TableName);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        return count > 0;
    }

    private static async Task<T?> ReadScalarAsync<T>(TargetDatabaseConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(BuildConnectionString(connection));
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync(cancellationToken);
        return raw is null or DBNull ? default : (T)raw;
    }

    private static async Task<IReadOnlyList<string>> ReadAppliedStepIdsAsync(TargetDatabaseConnection connection, CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        await using var conn = new NpgsqlConnection(BuildConnectionString(connection));
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = MigrationLedgerContracts.AppliedIdsSql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            ids.Add(id);
        }

        return ids;
    }

    private static async Task EnsureLedgerTableAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = MigrationLedgerContracts.EnsureTableSql("TIMESTAMPTZ");
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> DatabaseExistsAsync(TargetDatabaseConnection admin, string databaseName, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(BuildSystemConnectionString(admin));
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
        cmd.Parameters.AddWithValue("name", databaseName);
        return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task EnsureRoleAsync(NpgsqlConnection conn, string roleName, string password, CancellationToken cancellationToken)
    {
        await using var existsCmd = conn.CreateCommand();
        existsCmd.CommandText = "SELECT 1 FROM pg_roles WHERE rolname = @name";
        existsCmd.Parameters.AddWithValue("name", roleName);
        if (await existsCmd.ExecuteScalarAsync(cancellationToken) is not null)
        {
            // 角色已存在:复用现有凭据,不重置密码(避免破坏运行中服务的访问)。
            return;
        }

        await using var createCmd = conn.CreateCommand();
        createCmd.CommandText = $"CREATE ROLE {QuoteIdentifier(roleName)} WITH LOGIN PASSWORD {QuoteLiteral(password)}";
        await createCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task GrantAsync(NpgsqlConnection conn, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GeneratePassword() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private static long DeriveLockId(DatabaseTargetLockKey key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToCanonical()));
        return BitConverter.ToInt64(bytes, 0);
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string QuoteLiteral(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>advisory lock 释放句柄:Dispose 时解锁并关闭连接。</summary>
    private sealed class PostgreSqlAdvisoryLockHandle : IDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly long _lockId;

        public PostgreSqlAdvisoryLockHandle(NpgsqlConnection connection, long lockId)
        {
            _connection = connection;
            _lockId = lockId;
        }

        public void Dispose()
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT pg_advisory_unlock(@lockId)";
                cmd.Parameters.AddWithValue("lockId", _lockId);
                cmd.ExecuteNonQuery();
            }
            finally
            {
                _connection.Dispose();
            }
        }
    }
}
