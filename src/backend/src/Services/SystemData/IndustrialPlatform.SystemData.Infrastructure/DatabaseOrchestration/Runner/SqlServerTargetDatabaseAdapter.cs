using System.Data;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using Microsoft.Data.SqlClient;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>SQL Server 2019/2022 的目标库检查、建库、角色隔离、事务迁移和会话锁；凭据仅在内存传递。</summary>
public sealed class SqlServerTargetDatabaseAdapter : ITargetDatabaseInspector, ITargetDatabaseProvisioner, IMigrationExecutor, ITargetDatabaseAdvisoryLock
{
    public async Task<DatabaseTargetInspection> InspectAsync(ResolvedDatabaseTarget target,
        DatabaseTargetCredentials credentials, string moduleKey, CancellationToken cancellationToken)
    {
        ValidateTarget(target);
        var source = credentials.Migrator ?? credentials.Admin;
        if (source is null) return new(false, null, null, []);
        await using var connection = new SqlConnection(BuildConnectionString(source with { Database = target.PhysicalDatabaseName }));
        try { await connection.OpenAsync(cancellationToken); }
        catch (SqlException exception) when (exception.Number == 4060) { return new(false, null, null, []); }
        var ledger = LedgerName(moduleKey);
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE object_id=OBJECT_ID(@table,'U')";
        exists.Parameters.AddWithValue("@table", "dbo." + ledger);
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) == 0)
            return new(true, null, null, []);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT migration_id,version FROM dbo.{Quote(ledger)} ORDER BY applied_sequence";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var ids = new List<string>(); string? version = null;
        while (await reader.ReadAsync(cancellationToken)) { ids.Add(reader.GetString(0)); version = reader.GetString(1); }
        return new(true, version, null, ids);
    }

    public async Task<DatabaseProvisionOutcome> EnsureDatabaseAsync(ResolvedDatabaseTarget target,
        TargetDatabaseConnection admin, CancellationToken cancellationToken)
    {
        ValidateTarget(target);
        await using var connection = new SqlConnection(BuildConnectionString(admin with { Database = "master" }));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name=@database";
        command.Parameters.AddWithValue("@database", target.PhysicalDatabaseName);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) > 0)
            return new(false, true);
        command.Parameters.Clear();
        command.CommandText = $"CREATE DATABASE {Quote(target.PhysicalDatabaseName)}";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new(true, false);
    }

    public async Task<ProvisionedRoles> EnsureRolesAsync(ResolvedDatabaseTarget target,
        TargetDatabaseConnection admin, CancellationToken cancellationToken)
    {
        ValidateTarget(target);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(target.PhysicalDatabaseName + "|" + target.ServiceKey)))[..16];
        var migrator = admin with { Database = target.PhysicalDatabaseName, Schema = "dbo", Username = $"sd_{key}_migrator", Password = Password() };
        var runtime = admin with { Database = target.PhysicalDatabaseName, Schema = "dbo", Username = $"sd_{key}_runtime", Password = Password() };
        await EnsureRoleAsync(admin, migrator, true, cancellationToken);
        await EnsureRoleAsync(admin, runtime, false, cancellationToken);
        return new(migrator, runtime);
    }

    private static async Task EnsureRoleAsync(TargetDatabaseConnection admin, TargetDatabaseConnection role,
        bool migrationRole, CancellationToken cancellationToken)
    {
        await using (var connection = new SqlConnection(BuildConnectionString(admin with { Database = "master" })))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            // LOGIN 的密码不支持普通语句参数：在服务端对参数执行 QUOTENAME，不把密码拼入应用 SQL/日志。
            command.CommandText = """
                DECLARE @sql nvarchar(max);
                IF EXISTS (SELECT 1 FROM sys.sql_logins WHERE name=@name)
                    SET @sql=N'ALTER LOGIN '+QUOTENAME(@name)+N' WITH PASSWORD='+QUOTENAME(@password,'''');
                ELSE
                    SET @sql=N'CREATE LOGIN '+QUOTENAME(@name)+N' WITH PASSWORD='+QUOTENAME(@password,'''')+N', CHECK_POLICY=ON';
                EXEC sys.sp_executesql @sql;
                """;
            command.Parameters.AddWithValue("@name", role.Username!);
            command.Parameters.AddWithValue("@password", role.Password!);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var database = new SqlConnection(BuildConnectionString(admin with { Database = role.Database }));
        await database.OpenAsync(cancellationToken);
        await using var grants = database.CreateCommand();
        var name = Quote(role.Username!);
        grants.CommandText = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name=@name)
                CREATE USER {name} FOR LOGIN {name} WITH DEFAULT_SCHEMA=dbo;
            GRANT CONNECT TO {name};
            """ + (migrationRole
            ? $"GRANT CONTROL ON SCHEMA::dbo TO {name}; GRANT CREATE TABLE,CREATE VIEW,CREATE PROCEDURE,CREATE FUNCTION TO {name};"
            : $"GRANT SELECT,INSERT,UPDATE,DELETE ON SCHEMA::dbo TO {name}; DENY ALTER ON SCHEMA::dbo TO {name}; DENY CREATE TABLE,CREATE VIEW,CREATE PROCEDURE,CREATE FUNCTION TO {name};");
        grants.Parameters.AddWithValue("@name", role.Username!);
        await grants.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MigrationExecutionResult> ApplyAsync(ResolvedDatabaseTarget target,
        DatabaseMigrationArtifact artifact, TargetDatabaseConnection migrator, string moduleKey, CancellationToken cancellationToken)
    {
        ValidateTarget(target);
        await using var connection = new SqlConnection(BuildConnectionString(migrator with { Database = target.PhysicalDatabaseName }));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await AcquireTransactionLockAsync(connection, transaction, "migration:" + moduleKey, cancellationToken);
            var table = LedgerName(moduleKey);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                IF OBJECT_ID(N'dbo.{table}',N'U') IS NULL
                CREATE TABLE dbo.{Quote(table)} (
                    applied_sequence bigint NOT NULL,
                    migration_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,
                    version nvarchar(128) NOT NULL, applied_on datetimeoffset(7) NOT NULL,
                    script_checksum varchar(64) NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            var appliedCount = 0;
            foreach (var step in artifact.Steps.OrderBy(step => step.Sequence))
            {
                var checksum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(step.Sql.ReplaceLineEndings("\n"))));
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@id", step.StepId);
                command.CommandText = $"SELECT script_checksum FROM dbo.{Quote(table)} WHERE migration_id=@id";
                var existing = await command.ExecuteScalarAsync(cancellationToken);
                if (existing is string saved)
                {
                    if (!string.Equals(saved, checksum, StringComparison.Ordinal))
                        throw new InvalidOperationException($"SQL Server migration checksum drift:{step.StepId}。必须追加新迁移。");
                    continue;
                }
                command.Parameters.Clear();
                command.CommandText = step.Sql;
                await command.ExecuteNonQueryAsync(cancellationToken);
                command.CommandText = $"""
                    INSERT INTO dbo.{Quote(table)} (applied_sequence,migration_id,version,applied_on,script_checksum)
                    VALUES ((SELECT COALESCE(MAX(applied_sequence),0)+1 FROM dbo.{Quote(table)}),@id,@version,@now,@checksum)
                    """;
                command.Parameters.AddWithValue("@id", step.StepId);
                command.Parameters.AddWithValue("@version", artifact.Version);
                command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);
                command.Parameters.AddWithValue("@checksum", checksum);
                await command.ExecuteNonQueryAsync(cancellationToken);
                appliedCount++;
            }
            await transaction.CommitAsync(cancellationToken);
            return new(appliedCount, artifact.Version);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<IDisposable?> AcquireAsync(DatabaseTargetLockKey key, TargetDatabaseConnection connection,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var database = new SqlConnection(BuildConnectionString(connection));
        try
        {
            await database.OpenAsync(cancellationToken);
            await using var command = database.CreateCommand();
            var resource = "industrial-platform:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToCanonical())));
            command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=@timeout; SELECT @r;";
            command.Parameters.AddWithValue("@resource", resource);
            command.Parameters.AddWithValue("@timeout", (int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue));
            command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds) + 5);
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            if (result >= 0) return new LockHandle(database, resource);
            await database.DisposeAsync();
            return null;
        }
        catch { await database.DisposeAsync(); throw; }
    }

    internal static async Task AcquireTransactionLockAsync(SqlConnection connection, SqlTransaction transaction,
        string resource, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @r;";
        command.Parameters.AddWithValue("@resource", "industrial-platform:" + resource);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0) throw new TimeoutException("SQL Server 目标事务锁未取得。");
    }

    internal static string BuildConnectionString(TargetDatabaseConnection connection)
    {
        if (connection.Provider != "SqlServer") throw new ArgumentException("连接 Provider 必须为 SqlServer。", nameof(connection));
        if (connection.Schema is not null and not "dbo") throw new ArgumentException("SQL Server 仅支持 dbo。", nameof(connection));
        if (connection.Port is < 1 or > 65535) throw new ArgumentException("SQL Server 端口无效。", nameof(connection));
        return new SqlConnectionStringBuilder
        {
            DataSource = $"{connection.Host},{connection.Port}", InitialCatalog = connection.Database,
            UserID = connection.Username, Password = connection.Password,
            Encrypt = connection.Encrypt, TrustServerCertificate = connection.TrustServerCertificate, ConnectTimeout = 15,
        }.ConnectionString;
    }

    private static void ValidateTarget(ResolvedDatabaseTarget target)
    {
        if (target.Provider != DatabaseProvider.SqlServer || target.Schema is not null and not "dbo")
            throw new ArgumentException("SQL Server 目标必须为 SqlServer/dbo。", nameof(target));
    }
    private static string LedgerName(string module) => MigrationLedgerContracts.TableName(module);
    private static string Quote(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 128) throw new ArgumentException("SQL Server 标识长度应为 1～128。", nameof(identifier));
        return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }
    private static string Password() => "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private sealed class LockHandle(SqlConnection connection, string resource) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "EXEC sys.sp_releaseapplock @Resource=@resource,@LockOwner='Session';";
                command.Parameters.AddWithValue("@resource", resource);
                command.ExecuteNonQuery();
            }
            finally { connection.Dispose(); }
        }
    }
}
