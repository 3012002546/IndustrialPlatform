using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using Npgsql;
using SqlSugar;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;

/// <summary>初始化读取账本到提交迁移期间的跨进程目标锁；崩溃后连接/文件句柄自动释放。</summary>
internal static class StudioInitializationLock
{
    public static async Task<IAsyncDisposable> AcquireAsync(StudioDatabase database, CancellationToken cancellationToken)
    {
        if (database.Provider == DbType.SqlServer) return await SqlServerMigrationArtifacts.AcquireLockAsync(database.ConnectionString, "studio-initialize:" + database.TargetIdentity, cancellationToken) ?? throw new InvalidOperationException("SQL Server初始化锁不可用。");
        if (database.Provider == DbType.Sqlite)
        {
            var file = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(database.ConnectionString).DataSource + ".studio.init.lock";
            var until = DateTimeOffset.UtcNow.AddSeconds(10);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { return new FileLease(new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
                catch (IOException) when (DateTimeOffset.UtcNow < until) { await Task.Delay(50, cancellationToken); }
            }
        }
        var connection = new NpgsqlConnection(database.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var key = BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(database.TargetIdentity)));
            var until = DateTimeOffset.UtcNow.AddSeconds(10);
            while (true)
            {
                await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
                command.Parameters.AddWithValue("key", key);
                if (await command.ExecuteScalarAsync(cancellationToken) is true) return new PostgresLease(connection, key);
                if (DateTimeOffset.UtcNow >= until) throw new TimeoutException("Studio初始化目标锁获取超时。");
                await Task.Delay(50, cancellationToken);
            }
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    private sealed class FileLease(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
    private sealed class PostgresLease(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection); command.Parameters.AddWithValue("key", key); await command.ExecuteScalarAsync(); }
            finally { await connection.DisposeAsync(); }
        }
    }
}
