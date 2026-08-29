using System.Text.RegularExpressions;

namespace IndustrialPlatform.Infrastructure.Querying;

/// <summary>
/// Service-owned API-field to SQL-column map. Client field names never reach SQL.
/// </summary>
public sealed partial class SqlSugarQueryFieldMap<TEntity>
    where TEntity : class
{
    public SqlSugarQueryFieldMap(
        IReadOnlyDictionary<string, string> columns,
        string tieBreaker = "NId")
    {
        if (columns.Count == 0) throw new ArgumentException("字段映射不能为空。", nameof(columns));
        if (string.IsNullOrWhiteSpace(tieBreaker)) throw new ArgumentException("tie-breaker 不能为空。", nameof(tieBreaker));
        if (columns.Keys.Any(string.IsNullOrWhiteSpace) || columns.Values.Any(value => !SqlIdentifier().IsMatch(value)))
        {
            throw new ArgumentException("字段映射必须使用服务端注册的安全 SQL 标识符。", nameof(columns));
        }

        Columns = new Dictionary<string, string>(columns, StringComparer.Ordinal);
        TieBreaker = tieBreaker;
    }

    public IReadOnlyDictionary<string, string> Columns { get; }

    public string TieBreaker { get; }

    public string Resolve(string field)
        => Columns.TryGetValue(field, out var column)
            ? column
            : throw new KeyNotFoundException($"查询字段未注册：{field}。");

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SqlIdentifier();
}
