using System.Globalization;
using System.Text.RegularExpressions;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Hangfire.SqlServer;
using Hangfire.Storage.SQLite;
using Microsoft.Data.Sqlite;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Scheduler;

/// <summary>固定包原生存储的受控初始化。运行构造前检查，不用运行开关自动修复缺表。</summary>
public static class SchedulerStorageSchema
{
    public const string SchemaName = "scheduler_hangfire";
    private static readonly IReadOnlyDictionary<string, string[]> SqliteColumns = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Job"] = ["Id", "StateId", "StateName", "InvocationData", "Arguments", "CreatedAt", "ExpireAt"],
        ["State"] = ["Id", "JobId", "Name", "Reason", "CreatedAt", "Data", "ExpireAt"],
        ["JobParameter"] = ["Id", "JobId", "Name", "Value", "ExpireAt"],
        ["JobQueue"] = ["Id", "JobId", "Queue", "FetchedAt"],
        ["Server"] = ["Id", "Data", "LastHeartbeat"],
        ["List"] = ["Id", "Key", "Value", "ExpireAt"],
        ["Set"] = ["Id", "Key", "Score", "Value", "ExpireAt"],
        ["Hash"] = ["Id", "Key", "Field", "Value", "ExpireAt"],
        ["Counter"] = ["Id", "Key", "Value", "ExpireAt"],
        ["AggregatedCounter"] = ["Key", "Value", "ExpireAt"],
        ["DistributedLock"] = ["Id", "Resource", "ResourceKey", "ExpireAt"],
    };

    public static string SqliteFile(string connectionString)
    {
        var source = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(source) || source == ":memory:") throw new InvalidOperationException("持久调度需要SQLite文件目标。");
        return Path.GetFullPath(source) + ".scheduler.db";
    }

    /// <summary>仅由服务迁移步骤调用；不可从请求或后台工作器调用。</summary>
    public static async Task InitializeAsync(ISqlSugarClient database, CancellationToken token)
    {
        var provider = database.CurrentConnectionConfig.DbType;
        if (provider == DbType.Sqlite)
        {
            var source = new SqliteConnectionStringBuilder(database.CurrentConnectionConfig.ConnectionString).DataSource;
            // 内存数据库仅用于不启用Scheduler的旧模块测试，不能作为持久调度运行目标。
            if (source == ":memory:") return;
            SQLitePCL.Batteries_V2.Init();
            using var storage = new SQLiteStorage(SqliteFile(database.CurrentConnectionConfig.ConnectionString), SQLiteOptions());
            return;
        }
        if (provider == DbType.SqlServer)
        {
            var script = Resource(typeof(SqlServerStorage).Assembly, "Hangfire.SqlServer.Install.sql").Replace("$(HangFireSchema)", SchemaName, StringComparison.Ordinal);
            foreach (var batch in Regex.Split(script, @"(?im)^\s*GO\s*$"))
                if (!string.IsNullOrWhiteSpace(batch)) await database.Ado.ExecuteCommandAsync(batch, parameters: null, cancellationToken: token);
            return;
        }
        if (provider != DbType.PostgreSQL) throw new NotSupportedException("不支持的调度存储Provider。");
        var previous = await database.Ado.GetStringAsync("SELECT current_setting('search_path')");
        try
        {
            var assembly = typeof(PostgreSqlStorage).Assembly;
            var resources = assembly.GetManifestResourceNames().Where(name => name.Contains(".Scripts.Install.v", StringComparison.Ordinal))
                .Select(name => (Name: name, Version: int.Parse(Regex.Match(name, @"\.v(\d+)\.sql$").Groups[1].Value, CultureInfo.InvariantCulture))).OrderBy(item => item.Version);
            foreach (var item in resources)
            {
                var exists = await database.Ado.GetStringAsync("SELECT to_regclass('scheduler_hangfire.schema')::text");
                var version = string.IsNullOrEmpty(exists) ? 0 : await database.Ado.GetIntAsync("SELECT max(version)::integer FROM scheduler_hangfire.schema");
                if (version >= item.Version) continue;
                var script = Resource(assembly, item.Name).Replace("hangfire", SchemaName, StringComparison.Ordinal);
                await database.Ado.ExecuteCommandAsync(script, parameters: null, cancellationToken: token);
                // 供应商脚本只负责DDL，版本递增由其安装器完成；受控调用同样在该迁移事务中登记实际已应用版本。
                await database.Ado.ExecuteCommandAsync("UPDATE scheduler_hangfire.schema SET version=@version", new SugarParameter("@version", item.Version));
            }
        }
        finally
        {
            await database.Ado.ExecuteCommandAsync("SELECT set_config('search_path', @previous, false)", new SugarParameter("@previous", previous));
        }
    }

    /// <summary>运行只验证既有对象；SQLite构造器没有DDL关闭选项，因此必须先拒绝缺失/漂移。</summary>
    public static JobStorage OpenExisting(DbType provider, string connectionString)
    {
        if (provider is DbType.PostgreSQL or DbType.SqlServer) ValidateRelational(provider, connectionString);
        if (provider == DbType.PostgreSQL)
        {
            var options = new PostgreSqlStorageOptions { SchemaName = SchemaName, PrepareSchemaIfNecessary = false, QueuePollInterval = TimeSpan.FromSeconds(1) };
            return new PostgreSqlStorage(new NpgsqlConnectionFactory(connectionString, options, null), options);
        }
        if (provider == DbType.SqlServer) return new SqlServerStorage(connectionString, new SqlServerStorageOptions
        {
            SchemaName = SchemaName, PrepareSchemaIfNecessary = false, TryAutoDetectSchemaDependentOptions = false,
            QueuePollInterval = TimeSpan.FromSeconds(1), SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        });
        if (provider != DbType.Sqlite) throw new NotSupportedException("不支持的调度Provider。");
        var file = SqliteFile(connectionString);
        if (!File.Exists(file)) throw new InvalidOperationException("调度原生库未受控初始化。");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        foreach (var expected in SqliteColumns)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{expected.Key}')";
            using var reader = command.ExecuteReader();
            var actual = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read()) actual.Add(reader.GetString(1));
            if (!actual.SetEquals(expected.Value)) throw new InvalidOperationException("调度原生表结构漂移:" + expected.Key);
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND sql IS NOT NULL";
            using var reader = command.ExecuteReader();
            var indexes = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read()) indexes.Add(reader.GetString(0));
            string[] required = ["DistributedLock_Resource", "IX_AggregatedCounter_ExpireAt", "IX_Counter_ExpireAt", "IX_Hash_ExpireAt", "IX_Hash_Field", "IX_Hash_Key", "IX_JobParameter_ExpireAt", "IX_JobParameter_JobId", "IX_JobParameter_Name", "IX_JobQueue_FetchedAt", "IX_JobQueue_Queue", "IX_Job_ExpireAt", "IX_Job_StateName", "IX_List_ExpireAt", "IX_List_Key", "IX_Set_ExpireAt", "IX_Set_Key", "IX_Set_Value", "IX_State_ExpireAt", "IX_State_JobId"];
            if (required.Any(name => !indexes.Contains(name)))
                throw new InvalidOperationException("调度原生索引缺失。");
        }
        return new SQLiteStorage(file, SQLiteOptions());
    }

    private static void ValidateRelational(DbType provider, string connectionString)
    {
        using var database = new SqlSugarClient(new ConnectionConfig { DbType = provider, ConnectionString = connectionString, IsAutoCloseConnection = true });
        var expected = provider == DbType.PostgreSQL ? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["aggregatedcounter"] = ["id", "key", "value", "expireat"], ["counter"] = ["id", "key", "value", "expireat"],
            ["hash"] = ["id", "key", "field", "value", "expireat", "updatecount"],
            ["job"] = ["id", "stateid", "statename", "invocationdata", "arguments", "createdat", "expireat", "updatecount"],
            ["jobparameter"] = ["id", "jobid", "name", "value", "updatecount"], ["jobqueue"] = ["id", "jobid", "queue", "fetchedat", "updatecount"],
            ["list"] = ["id", "key", "value", "expireat", "updatecount"], ["lock"] = ["resource", "updatecount", "acquired"], ["schema"] = ["version"],
            ["server"] = ["id", "data", "lastheartbeat", "updatecount"], ["set"] = ["id", "key", "score", "value", "expireat", "updatecount"],
            ["state"] = ["id", "jobid", "name", "reason", "createdat", "data", "updatecount"],
        } : new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["AggregatedCounter"] = ["Key", "Value", "ExpireAt"], ["Counter"] = ["Id", "Key", "Value", "ExpireAt"], ["Hash"] = ["Key", "Field", "Value", "ExpireAt"],
            ["Job"] = ["Id", "StateId", "StateName", "InvocationData", "Arguments", "CreatedAt", "ExpireAt"], ["JobParameter"] = ["JobId", "Name", "Value"],
            ["JobQueue"] = ["Id", "JobId", "Queue", "FetchedAt"], ["List"] = ["Id", "Key", "Value", "ExpireAt"], ["Schema"] = ["Version"],
            ["Server"] = ["Id", "Data", "LastHeartbeat"], ["Set"] = ["Key", "Score", "Value", "ExpireAt"], ["State"] = ["Id", "JobId", "Name", "Reason", "CreatedAt", "Data"],
        };
        var metadata = database.Ado.GetDataTable("SELECT table_name,column_name FROM information_schema.columns WHERE table_schema=@schema", new SugarParameter("@schema", SchemaName));
        foreach (var table in expected)
        {
            var columns = metadata.Rows.Cast<System.Data.DataRow>().Where(row => table.Key.Equals(row["table_name"]?.ToString(), StringComparison.OrdinalIgnoreCase))
                .Select(row => row["column_name"].ToString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!columns.SetEquals(table.Value)) throw new InvalidOperationException("调度原生表结构漂移:" + table.Key);
        }
        var version = database.Ado.GetInt(provider == DbType.PostgreSQL ? "SELECT max(version)::integer FROM scheduler_hangfire.schema" : "SELECT MAX(Version) FROM scheduler_hangfire.[Schema]");
        if (version != (provider == DbType.PostgreSQL ? 23 : 9)) throw new InvalidOperationException("调度原生版本不符。");
    }

    private static SQLiteStorageOptions SQLiteOptions() => new() { Prefix = SchemaName, QueuePollInterval = TimeSpan.FromSeconds(1), PoolSize = 4 };
    private static string Resource(System.Reflection.Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("原生安装脚本缺失。");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
