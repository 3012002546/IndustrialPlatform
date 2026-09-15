using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IndustrialPlatform.Collaboration.EmbeddedHost;

public interface IStandaloneInitializationLock
{
    Task<IAsyncDisposable> AcquireAsync(ResolvedDatabaseTarget target, CancellationToken cancellationToken);
}

/// <summary>
/// Coordinates the complete standalone initialization sequence across processes.
/// SQLite uses an adjacent OS file lock; PostgreSQL uses a session-scoped advisory
/// lock on the actual physical database, so read/insert migration races cannot
/// produce duplicate ledger rows.
/// </summary>
public sealed class StandaloneInitializationLock(IOptions<SqlSugarOptions> sqlSugarOptions) : IStandaloneInitializationLock
{
    public async Task<IAsyncDisposable> AcquireAsync(ResolvedDatabaseTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var key = BuildKey(target);
        return target.Provider switch
        {
            DatabaseProvider.Sqlite => await AcquireSqliteAsync(target.PhysicalDatabaseName, cancellationToken),
            DatabaseProvider.PostgreSQL => await AcquirePostgresAsync(key, cancellationToken),
            _ => throw new InvalidOperationException($"不支持独立初始化锁提供程序: {target.Provider}。"),
        };
    }

    public static string BuildKey(ResolvedDatabaseTarget target) =>
        $"industrial-platform:standalone:init:{target.Provider}:{target.PhysicalDatabaseName}:{target.Schema ?? "public"}";

    private static async Task<IAsyncDisposable> AcquireSqliteAsync(string physicalFile, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(physicalFile));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var lockPath = Path.GetFullPath(physicalFile) + ".industrial-platform.initialization.lock";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileInitializationLock(new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
            }
            catch (IOException)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    private async Task<IAsyncDisposable> AcquirePostgresAsync(string key, CancellationToken cancellationToken)
    {
        var connectionString = sqlSugarOptions.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("独立 PostgreSQL 初始化锁缺少 SqlSugar 连接字符串。");
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(hashtext(@key));", connection);
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteScalarAsync(cancellationToken);
            return new PostgresInitializationLock(connection, key);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class FileInitializationLock(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }

    private sealed class PostgresInitializationLock(NpgsqlConnection connection, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtext(@key));", connection);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
