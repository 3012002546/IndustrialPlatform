using System.Text.Json;
using System.Text.RegularExpressions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Domain.Scheduler;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.SystemData.Infrastructure.Scheduler;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Npgsql;
using SqlSugar;
using IndustrialPlatform.Tools.Pf07;

// 仅创建并删除本进程随机命名的隔离库。配置只在内存使用，不输出凭据或原始异常消息。
var provider = args.FirstOrDefault() ?? "PostgreSQL";
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
using var configuration = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "src/backend/appsettings.Development.local.json")));
var remote = configuration.RootElement.GetProperty("RemoteDevelopment");
var name = "pf07_probe_" + Guid.NewGuid().ToString("N");
if (!Regex.IsMatch(name, "^pf07_probe_[a-f0-9]{32}$")) throw new InvalidOperationException("隔离库名称无效。");
var sqlServer = provider == "SqlServer";
var connection = sqlServer ? configuration.RootElement.GetProperty("SqlServerAcceptance").GetProperty("2019").GetProperty("ConnectionString").GetString()!
    : new NpgsqlConnectionStringBuilder { Host = remote.GetProperty("Host").GetString(), Port = remote.GetProperty("PostgreSql").GetProperty("Port").GetInt32(), Username = remote.GetProperty("PostgreSql").GetProperty("UserName").GetString(), Password = remote.GetProperty("PostgreSql").GetProperty("Password").GetString(), Database = "postgres", Timeout = 10 }.ToString();
var created = false;
var phase = "create-isolated-database";
async Task AdminAsync(string sql)
{
    if (sqlServer)
    {
        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master", ConnectTimeout = 10 };
        await using var client = new SqlConnection(builder.ToString()); await client.OpenAsync();
        await using var command = new SqlCommand(sql, client) { CommandTimeout = 60 }; await command.ExecuteNonQueryAsync();
    }
    else
    {
        await using var client = new NpgsqlConnection(connection); await client.OpenAsync();
        await using var command = new NpgsqlCommand(sql, client) { CommandTimeout = 60 }; await command.ExecuteNonQueryAsync();
    }
}
try
{
    await AdminAsync(sqlServer ? $"CREATE DATABASE [{name}]" : $"CREATE DATABASE \"{name}\""); created = true;
    var target = sqlServer ? new SqlConnectionStringBuilder(connection) { InitialCatalog = name }.ToString() : new NpgsqlConnectionStringBuilder(connection) { Database = name }.ToString();
    using var database = new SqlSugarDbContext(Options.Create(new SqlSugarOptions { DbType = sqlServer ? DbType.SqlServer : DbType.PostgreSQL, ConnectionString = target }));
    foreach (var step in SchedulerSchemaMigrations.All) { phase = "first-" + step.Id; await step.Apply(database.SqlSugar, CancellationToken.None); }
    foreach (var step in SchedulerSchemaMigrations.All) { phase = "second-" + step.Id; await step.Apply(database.SqlSugar, CancellationToken.None); }
    phase = "business-crud";
    var store = new SqlSchedulerStore(database);
    var job = new SchedulerJob("tenant-a", "probe.job", "隔离存储真实测试", null, "probe", "{\"schemaVersion\":1}", "*/15 * * * *", "Etc/UTC", 30, 1, "NoAutomaticRecovery");
    await store.AddJobAsync(job, CancellationToken.None);
    if (await store.GetJobAsync("tenant-a", job.NId, CancellationToken.None) is null || await store.GetJobAsync("tenant-b", job.NId, CancellationToken.None) is not null) throw new InvalidOperationException("租户持久化校验失败。");
    var now = DateTimeOffset.UtcNow;
    var run = new SchedulerRun { TenantNId = "tenant-a", NId = "probe-run-a", JobId = job.Id, CreatedOn = now, AcceptedOn = now, InvocationKey = "probe-a", OperationKey = "probe-a", HandlerKey = "probe", JobNameSnapshot = job.Name, ParametersJson = job.ParametersJson, TraceId = "probe-trace", TimeoutSeconds = 30, MaxAttempts = 1, RetrySafety = "NoAutomaticRecovery", Source = "Manual" };
    await store.AddRunAsync(run, CancellationToken.None);
    await store.AddRunAsync(run with { Id = Guid.NewGuid(), NId = "probe-run-b", InvocationKey = "probe-b" }, CancellationToken.None);
    var second = (await store.GetRunAsync("tenant-a", "probe-run-b", CancellationToken.None))!;
    if (!await store.TryStartAsync(run, Guid.NewGuid(), "probe-worker", "native-a", now, CancellationToken.None) || await store.TryStartAsync(second, Guid.NewGuid(), "probe-worker", "native-b", now, CancellationToken.None)) throw new InvalidOperationException("唯一活动槽校验失败。");
    var active = (await store.GetRunAsync("tenant-a", run.NId, CancellationToken.None))!;
    await store.SaveRunAsync(active with { Status = "Succeeded", ActiveJobId = null, CompletedOn = now, StateVersion = active.StateVersion + 1 }, active.StateVersion, CancellationToken.None);
    var third = run with { Id = Guid.NewGuid(), NId = "probe-run-c", InvocationKey = "probe-c" };
    await store.AddRunAsync(third, CancellationToken.None);
    using var otherDatabase = new SqlSugarDbContext(Options.Create(new SqlSugarOptions { DbType = sqlServer ? DbType.SqlServer : DbType.PostgreSQL, ConnectionString = target }));
    var otherStore = new SqlSchedulerStore(otherDatabase);
    var competition = await Task.WhenAll(store.TryStartAsync(second, Guid.NewGuid(), "replica-one", "native-b", now, CancellationToken.None),
        otherStore.TryStartAsync(third, Guid.NewGuid(), "replica-two", "native-c", now, CancellationToken.None));
    if (competition.Count(won => won) != 1) throw new InvalidOperationException("双连接并发活动槽校验失败。");
    using var engine = new HangfireSchedulerEngine(Options.Create(new IndustrialPlatform.SystemData.Application.Scheduler.SchedulerOptions { Enabled = true }),
        database, Options.Create(new SqlSugarOptions { DbType = sqlServer ? DbType.SqlServer : DbType.PostgreSQL, ConnectionString = target }), store);
    foreach (var (suffix, expression) in new[] { ("spring", "30 2 14 3 *"), ("fall", "30 1 1 11 *") })
    {
        var scheduled = new SchedulerJob("tenant-a", "probe-dst-" + suffix, "DST边界预览一致性", null, "probe", "{}", expression, "America/New_York", 30, 1, "NoAutomaticRecovery");
        scheduled.SetEnabled(true); await store.AddJobAsync(scheduled, CancellationToken.None);
        var preview = engine.Preview(new(expression, scheduled.TimeZoneId));
        await engine.SynchronizeAsync(scheduled, CancellationToken.None);
        var native = await engine.GetPlanAsync(scheduled, CancellationToken.None);
        if (native.NextFireOn != preview.Occurrences[0].Utc) throw new InvalidOperationException("DST预览与原生下一次计划不一致。");
    }
    phase = "native-storage-and-worker";
    var storage = SchedulerStorageSchema.OpenExisting(sqlServer ? DbType.SqlServer : DbType.PostgreSQL, target);
    using var storageDisposal = storage as IDisposable;
    using var server = new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 1 }, storage);
    using var otherServer = new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 1 }, storage);
    var reference = new BackgroundJobClient(storage).Create(Job.FromExpression(() => NativeProbeTask.Execute()), new EnqueuedState("default"));
    var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
    while (DateTimeOffset.UtcNow < deadline && NativeProbeTask.Calls == 0) await Task.Delay(100);
    if (NativeProbeTask.Calls != 1) throw new InvalidOperationException("原生工作器未实际执行。");
    using var read = storage.GetConnection();
    while (DateTimeOffset.UtcNow < deadline && read.GetJobData(reference)?.State != "Succeeded") await Task.Delay(100);
    if (read.GetJobData(reference)?.State != "Succeeded") throw new InvalidOperationException("原生状态未提交。");
    await Task.Delay(500);
    if (NativeProbeTask.Calls != 1) throw new InvalidOperationException("双原生工作器出现重复实际执行。");
    var metadata = await database.SqlSugar.Ado.GetDataTableAsync(sqlServer
        ? "SELECT s.name schema_name,t.name table_name,c.name column_name,ty.name data_type,c.max_length,c.is_nullable FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON c.user_type_id=ty.user_type_id ORDER BY s.name,t.name,c.column_id"
        : "SELECT table_schema schema_name,table_name,column_name,data_type,character_maximum_length max_length,is_nullable FROM information_schema.columns WHERE table_schema IN ('public','scheduler_hangfire') ORDER BY table_schema,table_name,ordinal_position");
    var fields = metadata.Rows.Cast<System.Data.DataRow>().Select(row => metadata.Columns.Cast<System.Data.DataColumn>().ToDictionary(column => column.ColumnName, column => row[column] is DBNull ? null : row[column].ToString())).ToArray();
    await File.WriteAllTextAsync(Path.Combine(root, ".run/pf07/" + provider + "-physical-schema.json"), JsonSerializer.Serialize(fields, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(provider + ": PASS — 两次受控初始化、租户CRUD、双连接并发活动槽仅一方取得、春跳/秋回预览与原生计划一致、双原生工作器实际执行=1、Succeeded、物理字段导出。退出时清理本次隔离库。");
}
catch (Exception exception)
{
    Console.WriteLine(provider + ": FAIL — " + phase + " — " + exception.GetType().Name + (exception is PostgresException pg ? " SQLSTATE=" + pg.SqlState + " Constraint=" + pg.ConstraintName : exception is SqlException sql ? " Number=" + sql.Number : string.Empty)); Environment.ExitCode = 1;
}
finally
{
    if (created)
    {
        SqlConnection.ClearAllPools(); NpgsqlConnection.ClearAllPools();
        try { await AdminAsync(sqlServer ? $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]" : $"DROP DATABASE \"{name}\" WITH (FORCE)"); Console.WriteLine("本次隔离库清理完成。"); }
        catch (Exception exception) { Console.WriteLine("隔离库清理失败:" + exception.GetType().Name); Environment.ExitCode = 1; }
    }
}
namespace IndustrialPlatform.Tools.Pf07
{
public static class NativeProbeTask
{
    private static int _calls;
    public static int Calls => Volatile.Read(ref _calls);
    [AutomaticRetry(Attempts = 0)]
    public static void Execute() => Interlocked.Increment(ref _calls);
}
}
