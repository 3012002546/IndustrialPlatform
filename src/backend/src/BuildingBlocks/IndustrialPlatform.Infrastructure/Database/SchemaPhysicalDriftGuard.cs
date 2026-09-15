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
        var columns = dbType == DbType.Sqlite
            ? Names(sugar.Ado.GetDataTable($"SELECT name FROM pragma_table_info('{tableName}')"), "name")
            : Names(sugar.Ado.GetDataTable($"SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = '{tableName}'"), "column_name");
        var indexes = dbType == DbType.Sqlite
            ? Names(sugar.Ado.GetDataTable($"SELECT name FROM pragma_index_list('{tableName}')"), "name")
            : Names(sugar.Ado.GetDataTable($"SELECT indexname FROM pg_indexes WHERE schemaname = current_schema() AND tablename = '{tableName}'"), "indexname");

        if (columns.Count == 0)
            throw new InvalidOperationException($"physical schema drift: table '{tableName}' is missing.");
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
