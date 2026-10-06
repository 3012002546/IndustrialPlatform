using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SqlSugar;
using Microsoft.Data.SqlClient;

namespace IndustrialPlatform.Infrastructure.Database;

/// <summary>读取服务自有的不可变 SQL Server 脚本；2019/2022 使用同一契约，不在运行时转换其他数据库的 SQL。</summary>
public static class SqlServerMigrationArtifacts
{
    private static readonly ConcurrentDictionary<(Assembly Assembly, string Id), string> Scripts = new();

    /// <summary>数据库级会话锁覆盖读取账本至全部迁移完成；独立连接持锁，逐步骤事务不会释放它。</summary>
    public static Task<IAsyncDisposable?> AcquireLockAsync(ISqlSugarClient database, string resource, CancellationToken cancellationToken)
    {
        return database.CurrentConnectionConfig.DbType == DbType.SqlServer
            ? AcquireLockAsync(database.CurrentConnectionConfig.ConnectionString, resource, cancellationToken)
            : Task.FromResult<IAsyncDisposable?>(null);
    }

    public static async Task<IAsyncDisposable?> AcquireLockAsync(string? connectionString, string resource, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("SQL Server 初始化锁缺少连接配置。");
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource=@key,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=10000;
                SELECT @result;
                """;
            command.Parameters.AddWithValue("@key", resource);
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            if (result < 0) throw new InvalidOperationException($"SQL Server 初始化锁获取失败，返回码 {result}。");
            return new SessionLock(connection, resource);
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private sealed class SessionLock(SqlConnection connection, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "EXEC sys.sp_releaseapplock @Resource=@key,@LockOwner='Session';";
                command.Parameters.AddWithValue("@key", resource);
                await command.ExecuteNonQueryAsync();
            }
            finally { await connection.DisposeAsync(); }
        }
    }

    /// <summary>脚本内容也进入迁移说明的摘要，从而进入现有账本 checksum；其他 Provider 的账本不变。</summary>
    public static string Description(Assembly assembly, string id, string description) =>
        $"{description} [SqlServer:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Read(assembly, id)))).ToLowerInvariant()}]";

    public static string Read(Assembly assembly, string id) => Scripts.GetOrAdd((assembly, id), key =>
    {
        var name = key.Assembly.GetManifestResourceNames().SingleOrDefault(name => name.EndsWith($".SqlServer.{key.Id}.sql", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"缺少 SQL Server 迁移脚本:{key.Id}。");
        using var stream = key.Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    });

    public static async Task<int> ApplyAsync(ISqlSugarClient database, Assembly assembly, string id, CancellationToken cancellationToken)
    {
        var affected = 0;
        // GO 是脚本的编译批次边界；事务仍由迁移运行器统一管理。
        foreach (var batch in Regex.Split(Read(assembly, id), @"(?im)^\s*GO\s*$"))
            if (!string.IsNullOrWhiteSpace(batch))
                affected += await database.Ado.ExecuteCommandAsync(batch, parameters: null, cancellationToken: cancellationToken);
        return affected;
    }

    /// <summary>账本不是业务实体；明确其键长和 UTC 时间类型，避免 CodeFirst 按默认字符串类型建表。</summary>
    public static Task<int> EnsureLedgerAsync(ISqlSugarClient database, string table, CancellationToken cancellationToken)
    {
        if (table is not ("identity_schema_migrations" or "system_data_schema_migrations" or "collaboration_schema_migrations"))
            throw new ArgumentException("未知服务迁移账本。", nameof(table));
        return database.Ado.ExecuteCommandAsync($"""
            IF OBJECT_ID(N'dbo.{table}', N'U') IS NULL
            CREATE TABLE dbo.{table} (
                migration_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,
                description nvarchar(max) NOT NULL,
                checksum varchar(64) NULL,
                applied_on datetimeoffset(7) NOT NULL
            );
            """, parameters: null, cancellationToken: cancellationToken);
    }

    /// <summary>目录查询保持只读，兼容不存在的旧账本。</summary>
    public static HashSet<string> Columns(ISqlSugarClient database, string table) => database.Ado.GetDataTable(
        "SELECT c.name FROM sys.columns c WHERE c.object_id=OBJECT_ID(@table, 'U')",
        new SugarParameter("@table", "dbo." + table)).Rows.Cast<System.Data.DataRow>()
        .Select(row => (string)row["name"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
