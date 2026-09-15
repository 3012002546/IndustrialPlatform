using IndustrialPlatform.Collaboration.EmbeddedHost;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.Extensions.Options;
using SqlSugar;

if (args.Length == 2 && string.Equals(args[0], "inspect", StringComparison.OrdinalIgnoreCase))
{
    var databasePath = Path.GetFullPath(args[1]);
    using var db = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
    {
        DbType = DbType.Sqlite,
        ConnectionString = $"Data Source={databasePath};Pooling=False",
        IsAutoCloseConnection = true,
    }));
    var tables = db.SqlSugar.DbMaintenance.GetTableInfoList()
        .Select(item => item.Name)
        .OrderBy(item => item, StringComparer.Ordinal)
        .ToArray();
    Console.WriteLine($"INSPECT|database={databasePath}|table-count={tables.Length}");
    foreach (var table in tables)
    {
        var count = db.SqlSugar.Ado.GetInt($"SELECT COUNT(*) FROM [{table.Replace("]", "]]", StringComparison.Ordinal)}]");
        Console.WriteLine($"TABLE|{table}|rows={count}");
    }
    foreach (var table in new[] { "system_data_outbox", "system_data_seed_ledger" })
    {
        if (!tables.Contains(table, StringComparer.Ordinal))
            continue;
        var columns = db.SqlSugar.DbMaintenance.GetColumnInfosByTableName(table)
            .Select(item => item.DbColumnName)
            .ToArray();
        Console.WriteLine($"COLUMNS|{table}|{string.Join(',', columns)}");
        var rows = db.SqlSugar.Ado.GetDataTable($"SELECT * FROM [{table}]");
        foreach (System.Data.DataRow row in rows.Rows)
        {
            var values = columns.Select(column => $"{column}={row[column]}");
            Console.WriteLine($"ROW|{table}|{string.Join(';', values)}");
        }
    }
    return 0;
}

if (args.Length != 3 || !string.Equals(args[0], "child", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("usage: pf06-standalone-lock-probe child <absolute-db-path> <hold-ms> | inspect <absolute-db-path>");
    return 2;
}

var database = Path.GetFullPath(args[1]);
if (!int.TryParse(args[2], out var holdMilliseconds) || holdMilliseconds < 0)
{
    Console.Error.WriteLine("hold-ms must be a non-negative integer");
    return 2;
}

var target = new ResolvedDatabaseTarget(
    "Production",
    DatabaseTopologyMode.Shared,
    "probe",
    DatabaseProvider.Sqlite,
    "probe_db",
    database,
    IsSharedPhysicalDatabase: true,
    Schema: "public",
    IsStandalone: true);
var locker = new StandaloneInitializationLock(Options.Create(new SqlSugarOptions
{
    DbType = DbType.Sqlite,
    ConnectionString = $"Data Source={database}",
}));

await using var handle = await locker.AcquireAsync(target, CancellationToken.None);
Console.WriteLine($"ACQUIRED|pid={Environment.ProcessId}|cwd={Environment.CurrentDirectory}|utc={DateTimeOffset.UtcNow:O}");
Console.Out.Flush();
await Task.Delay(holdMilliseconds);
return 0;
