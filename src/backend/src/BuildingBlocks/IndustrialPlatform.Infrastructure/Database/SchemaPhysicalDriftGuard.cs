using SqlSugar;

namespace IndustrialPlatform.Infrastructure.Database;

/// <summary>
/// Validates the physical shape that a migration ledger claims to have applied.
/// A ledger hit is not sufficient when an operator has removed a column or index.
/// </summary>
public static class SchemaPhysicalDriftGuard
{
    public static void Validate(ISqlSugarClient sugar, string tableName, IReadOnlyCollection<string> requiredColumns, IReadOnlyCollection<string> requiredIndexes)
    {
        ArgumentNullException.ThrowIfNull(sugar);
        var dbType = sugar.CurrentConnectionConfig.DbType;
        var (columns, indexes) = dbType switch
        {
            DbType.Sqlite => (
                Names(sugar.Ado.GetDataTable($"SELECT name FROM pragma_table_info('{tableName}')"), "name"),
                Names(sugar.Ado.GetDataTable($"SELECT name FROM pragma_index_list('{tableName}')"), "name")),
            DbType.PostgreSQL => (
                Names(sugar.Ado.GetDataTable($"SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = '{tableName}'"), "column_name"),
                Names(sugar.Ado.GetDataTable($"SELECT indexname FROM pg_indexes WHERE schemaname = current_schema() AND tablename = '{tableName}'"), "indexname")),
            DbType.SqlServer => (
                Names(sugar.Ado.GetDataTable(
                    "SELECT c.name FROM sys.columns AS c JOIN sys.tables AS t ON t.object_id = c.object_id JOIN sys.schemas AS s ON s.schema_id = t.schema_id WHERE s.name = 'dbo' AND t.name = @tableName",
                    new SugarParameter("@tableName", tableName)), "name"),
                Names(sugar.Ado.GetDataTable(
                    "SELECT i.name FROM sys.indexes AS i JOIN sys.tables AS t ON t.object_id = i.object_id JOIN sys.schemas AS s ON s.schema_id = t.schema_id WHERE s.name = 'dbo' AND t.name = @tableName AND i.name IS NOT NULL",
                    new SugarParameter("@tableName", tableName)), "name")),
            _ => throw new NotSupportedException($"不支持的目标数据库类型:{dbType}。"),
        };

        if (columns.Count == 0)
            throw new InvalidOperationException(dbType == DbType.SqlServer
                ? $"physical schema drift: table 'dbo.{tableName}' is missing or metadata is not visible to the current login."
                : $"physical schema drift: table '{tableName}' is missing.");
        var missingColumns = requiredColumns.Where(column => !columns.Contains(column)).ToArray();
        var missingIndexes = requiredIndexes
            .Where(index =>
            {
                var physicalName = dbType == DbType.PostgreSQL
                    ? PostgreSqlName(sugar, index)
                    : index;
                return physicalName is null || !indexes.Contains(physicalName);
            })
            .ToArray();
        if (missingColumns.Length > 0 || missingIndexes.Length > 0)
            throw new InvalidOperationException($"physical schema drift: table '{tableName}' missing columns [{string.Join(',', missingColumns)}] or indexes [{string.Join(',', missingIndexes)}].");
    }

    private static string? PostgreSqlName(ISqlSugarClient sugar, string expectedName)
    {
        var result = sugar.Ado.GetDataTable(
            "SELECT CAST(@indexName AS pg_catalog.name)::text AS name",
            new SugarParameter("@indexName", expectedName));

        return Names(result, "name").FirstOrDefault();
    }

    private static HashSet<string> Names(System.Data.DataTable table, string columnName) =>
        table.Rows.Cast<System.Data.DataRow>()
            .Select(row => row[columnName]?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
