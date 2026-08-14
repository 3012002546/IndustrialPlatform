using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.Topology;
using Microsoft.Data.Sqlite;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// SQLite 目标数据库本地替身适配器(05 方案 §7.1.5 本地开发基线):物理库文件即目标,无账号/角色;
/// 迁移账本同 PostgreSQL 实现,便于替身与真库共用 Runner 编排逻辑。advisory lock 用独立锁文件互斥。
/// </summary>
public sealed class SqliteTargetDatabaseAdapter : ITargetDatabaseInspector, ITargetDatabaseProvisioner, IMigrationExecutor, ITargetDatabaseAdvisoryLock
{
    /// <summary>advisory lock 轮询间隔(毫秒)。</summary>
    private const int LockPollIntervalMilliseconds = 200;

    /// <inheritdoc />
    public async Task<DatabaseTargetInspection> InspectAsync(
        ResolvedDatabaseTarget target,
        DatabaseTargetCredentials credentials,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var connection = credentials.Migrator ?? credentials.Admin;
        if (connection is null || string.IsNullOrWhiteSpace(connection.Database))
        {
            return new DatabaseTargetInspection(DatabaseExists: false, CurrentVersion: null, null, []);
        }

        var file = connection.Database;
        if (!File.Exists(file))
        {
            return new DatabaseTargetInspection(DatabaseExists: false, CurrentVersion: null, null, []);
        }

        if (!await LedgerTableExistsAsync(file, moduleKey, cancellationToken))
        {
            return new DatabaseTargetInspection(DatabaseExists: true, CurrentVersion: null, null, []);
        }

        var currentVersion = await ReadScalarAsync<string?>(file, MigrationLedgerContracts.LatestVersionSql(moduleKey), cancellationToken);
        var appliedIds = await ReadAppliedStepIdsAsync(file, moduleKey, cancellationToken);
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

        var file = admin.Database;
        if (string.IsNullOrWhiteSpace(file))
        {
            throw new DatabaseOrchestrationRunnerException(500, DatabaseOrchestrationRunnerErrors.ProvisionFailed, "目标数据库文件路径为空。");
        }

        if (File.Exists(file))
        {
            return new DatabaseProvisionOutcome(DatabaseCreated: false, DatabaseExisted: true);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var conn = new SqliteConnection(BuildConnectionString(file));
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        await cmd.ExecuteScalarAsync(cancellationToken);
        return new DatabaseProvisionOutcome(DatabaseCreated: true, DatabaseExisted: false);
    }

    /// <inheritdoc />
    public Task<ProvisionedRoles> EnsureRolesAsync(
        ResolvedDatabaseTarget target,
        TargetDatabaseConnection admin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(admin);

        // SQLite 无数据库角色概念:migrator/runtime 均指向同一物理文件。
        var file = admin.Database;
        var migrator = new TargetDatabaseConnection("Sqlite", string.Empty, 0, file, null, null, null);
        var runtime = new TargetDatabaseConnection("Sqlite", string.Empty, 0, file, null, null, null);
        return Task.FromResult(new ProvisionedRoles(migrator, runtime));
    }

    /// <inheritdoc />
    public async Task<MigrationExecutionResult> ApplyAsync(
        ResolvedDatabaseTarget target,
        DatabaseMigrationArtifact artifact,
        TargetDatabaseConnection migrator,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(migrator);

        var file = migrator.Database;
        if (string.IsNullOrWhiteSpace(file))
        {
            throw new DatabaseOrchestrationRunnerException(500, DatabaseOrchestrationRunnerErrors.ProvisionFailed, "目标数据库文件路径为空。");
        }

        await using var conn = new SqliteConnection(BuildConnectionString(file));
        await conn.OpenAsync(cancellationToken);
        await EnsureLedgerTableAsync(conn, moduleKey, cancellationToken);

        var appliedIds = await ReadAppliedStepIdsAsync(file, moduleKey, cancellationToken);
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
                cmd.Transaction = (SqliteTransaction)transaction;
                cmd.CommandText = step.Sql;
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                await using var recordCmd = conn.CreateCommand();
                recordCmd.Transaction = (SqliteTransaction)transaction;
                recordCmd.CommandText = MigrationLedgerContracts.RecordStepSql(moduleKey);
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

        var lockFilePath = Path.Combine(
            Path.GetTempPath(),
            $"target-lock-{DatabaseTopologyFingerprint.Sha256Hex(key.ToCanonical())}.lock");
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            try
            {
                var stream = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new FileLockHandle(stream);
            }
            catch (IOException)
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    return null;
                }

                await Task.Delay(LockPollIntervalMilliseconds, cancellationToken);
            }
        }
    }

    // ===== 私有辅助 =====

    private static string BuildConnectionString(string file) =>
        new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();

    private static async Task<bool> LedgerTableExistsAsync(string file, string moduleKey, CancellationToken cancellationToken)
    {
        await using var conn = new SqliteConnection(BuildConnectionString(file));
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = $name";
        cmd.Parameters.AddWithValue("name", MigrationLedgerContracts.TableName(moduleKey));
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        return count > 0;
    }

    private static async Task<T?> ReadScalarAsync<T>(string file, string sql, CancellationToken cancellationToken)
    {
        await using var conn = new SqliteConnection(BuildConnectionString(file));
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync(cancellationToken);
        return raw is null or DBNull ? default : (T)raw;
    }

    private static async Task<IReadOnlyList<string>> ReadAppliedStepIdsAsync(string file, string moduleKey, CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        await using var conn = new SqliteConnection(BuildConnectionString(file));
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = MigrationLedgerContracts.AppliedIdsSql(moduleKey);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static async Task EnsureLedgerTableAsync(SqliteConnection conn, string moduleKey, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = MigrationLedgerContracts.EnsureTableSql(moduleKey, "TEXT");
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>文件锁释放句柄:Dispose 释放独占流(锁文件保留,避免删除竞争)。</summary>
    private sealed class FileLockHandle : IDisposable
    {
        private readonly FileStream _stream;

        public FileLockHandle(FileStream stream)
        {
            _stream = stream;
        }

        public void Dispose() => _stream.Dispose();
    }
}
