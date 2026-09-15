using System.Data.Common;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Persistence;

/// <summary>Resolves the one PostgreSQL schema projected by the host connection.</summary>
public static class ReferenceDataSqlNames
{
    public static string Table(ISqlSugarClient database, string name)
    {
        if (database.CurrentConnectionConfig.DbType != DbType.PostgreSQL)
            return $"reference_data_{name}";

        var schema = Schema(database);
        return $"{schema}.{name}";
    }

    public static string Schema(ISqlSugarClient database)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = database.CurrentConnectionConfig.ConnectionString };
        foreach (var key in builder.Keys)
        {
            var keyText = key?.ToString();
            if (!string.Equals(keyText, "Search Path", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(keyText, "SearchPath", StringComparison.OrdinalIgnoreCase))
                continue;
            var value = Convert.ToString(builder[keyText!], System.Globalization.CultureInfo.InvariantCulture)?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(item => item is not "$user" && item.Length > 0);
            if (!string.IsNullOrWhiteSpace(value) && value.All(character => char.IsLetterOrDigit(character) || character == '_'))
                return value;
        }

        return "reference_data";
    }
}
