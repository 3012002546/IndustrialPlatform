using System.Text.RegularExpressions;

namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>参数化SQL的结构防护；实际只读仍由只读账号/只读连接或事务共同保证。</summary>
public static class SqlReadOnlyPolicy
{
    private static readonly TimeSpan RegexBudget = TimeSpan.FromSeconds(1);
    private static readonly HashSet<string> PureFunctions = new(StringComparer.OrdinalIgnoreCase) { "SELECT", "WITH", "AS", "IN", "OVER", "FILTER", "VALUES", "COUNT", "SUM", "AVG", "MIN", "MAX", "ABS", "ROUND", "FLOOR", "CEILING", "CEIL", "COALESCE", "NULLIF", "ISNULL", "CAST", "CONVERT", "TRY_CAST", "TRY_CONVERT", "VARCHAR", "NVARCHAR", "CHAR", "NCHAR", "DECIMAL", "NUMERIC", "DATETIME2", "DATETIMEOFFSET", "BIGINT", "INTEGER", "INT", "SUBSTRING", "SUBSTR", "CONCAT", "CONCAT_WS", "UPPER", "LOWER", "TRIM", "LTRIM", "RTRIM", "LENGTH", "LEN", "REPLACE", "LEFT", "RIGHT", "DATEADD", "DATEDIFF", "DATEPART", "DATE_TRUNC", "TO_CHAR", "STRFTIME", "DATE", "DATETIME", "JULIANDAY", "ROW_NUMBER", "RANK", "DENSE_RANK", "LAG", "LEAD", "JSON_VALUE", "JSON_QUERY", "JSON_EXTRACT", "JSON_EACH", "OPENJSON" };
    public static string Validate(string? statement, IReadOnlyCollection<string> allowedObjects)
    {
        if (string.IsNullOrWhiteSpace(statement) || statement.Length > 65536) throw Error("查询为空或过长。");
        var text = Regex.Replace(statement, @"'(?:''|[^'])*'", "''", RegexOptions.CultureInvariant, RegexBudget);
        text = Regex.Replace(text, @"--[^\r\n]*|/\*[\s\S]*?\*/", " ", RegexOptions.CultureInvariant, RegexBudget).Trim();
        if (text.EndsWith(';')) text = text[..^1].TrimEnd();
        if (text.Contains(';') || text.Contains("/*", StringComparison.Ordinal) || !Regex.IsMatch(text, @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexBudget)) throw Error("只允许一个SELECT或只读CTE查询。");
        if (Regex.IsMatch(text, @"\bNEXT\s+VALUE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexBudget)) throw Error("不能读取并推进序列。");
        if (Regex.IsMatch(text, @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|INTO|GRANT|REVOKE|DENY|BACKUP|RESTORE|ATTACH|DETACH|PRAGMA|VACUUM|REINDEX|COPY|CALL|DO|SET|WAITFOR|KILL|DBCC|OPENROWSET|OPENQUERY|OPENDATASOURCE|load_extension|readfile|writefile|dblink|lo_import|lo_export|pg_read_file|pg_read_binary_file|pg_ls_dir|pg_sleep)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexBudget)) throw Error("查询包含写入、管理或未准入外部访问操作。");
        var identifiers = text.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);
        ValidateFunctions(identifiers);
        var ctes = Regex.Matches(identifiers, @"(?:WITH(?:\s+RECURSIVE)?|,)\s*([\p{L}_][\p{L}\p{N}_]*)\s*(?:\([^)]*\))?\s+AS\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexBudget).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fromScopes = new Dictionary<int, bool>();
        var expectedObjects = new Dictionary<int, bool>();
        var depth = 0;
        foreach (Match match in Regex.Matches(identifiers, @"[\p{L}_][\p{L}\p{N}_]*(?:\s*\.\s*[\p{L}_][\p{L}\p{N}_]*)*|[(),]", RegexOptions.CultureInvariant, RegexBudget))
        {
            var name = Regex.Replace(match.Value, @"\s+", "", RegexOptions.CultureInvariant, RegexBudget);
            if (name == "(") { expectedObjects[depth] = false; depth++; fromScopes[depth] = false; expectedObjects[depth] = false; continue; }
            if (name == ")") { depth--; continue; }
            if (name.Equals("FROM", StringComparison.OrdinalIgnoreCase) || name.Equals("JOIN", StringComparison.OrdinalIgnoreCase)) { fromScopes[depth] = true; expectedObjects[depth] = true; continue; }
            if (new[] { "WHERE", "GROUP", "ORDER", "HAVING", "UNION", "EXCEPT", "INTERSECT", "LIMIT", "OFFSET" }.Contains(name, StringComparer.OrdinalIgnoreCase)) { fromScopes[depth] = false; expectedObjects[depth] = false; continue; }
            if (name == ",") { if (fromScopes.GetValueOrDefault(depth)) expectedObjects[depth] = true; continue; }
            if (!expectedObjects.GetValueOrDefault(depth)) continue;
            expectedObjects[depth] = false;
            if (ctes.Contains(name) || name.Equals("json_each", StringComparison.OrdinalIgnoreCase) || name.Equals("openjson", StringComparison.OrdinalIgnoreCase)) continue;
            if (!allowedObjects.Contains(name, StringComparer.OrdinalIgnoreCase)) throw Error("查询对象不在来源白名单：" + name);
        }
        return statement;
    }
    public static void VerifyProcedureDefinition(string definition)
    {
        var text = Regex.Replace(definition, @"'(?:''|[^'])*'|--[^\r\n]*|/\*[\s\S]*?\*/", " ", RegexOptions.CultureInvariant, RegexBudget);
        var header = Regex.Match(text, @"\bAS\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexBudget);
        if (!header.Success) throw Error("无法核实只读过程主体。");
        var body = text[(header.Index + header.Length)..];
        body = Regex.Replace(body, @"\bSET\s+NOCOUNT\s+ON\s*;?", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexBudget);
        if (Regex.IsMatch(body, @"\b(INSERT|UPDATE|DELETE|MERGE|INTO|EXEC|EXECUTE|CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|DENY|TRANSACTION|COMMIT|ROLLBACK|OPENROWSET|OPENQUERY|OPENDATASOURCE|WAITFOR|COPY|CALL|DO|PERFORM|SET|NEXT\s+VALUE|dblink|lo_import|lo_export|pg_sleep)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexBudget)) throw Error("过程具有写入、动态执行或无法准入的副作用。");
        ValidateFunctions(body.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal));
    }
    private static void ValidateFunctions(string text)
    {
        // CTE column declarations are identifiers, never function invocations.
        text = Regex.Replace(text, @"(\bWITH(?:\s+RECURSIVE)?|,)\s+([\p{L}_][\p{L}\p{N}_]*)\s*\([^()]*\)\s+AS\s*\(", "$1 $2 AS (", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexBudget);
        foreach (Match function in Regex.Matches(text, @"([\p{L}_][\p{L}\p{N}_]*(?:\s*\.\s*[\p{L}_][\p{L}\p{N}_]*)*)\s*\(", RegexOptions.CultureInvariant, RegexBudget))
            if (!PureFunctions.Contains(function.Groups[1].Value) && !new[] { "FROM", "JOIN", "WHERE", "EXISTS", "NOT", "AND", "OR", "ON", "HAVING" }.Contains(function.Groups[1].Value, StringComparer.OrdinalIgnoreCase)) throw Error("函数不在无副作用白名单：" + function.Groups[1].Value);
    }
    private static StudioException Error(string message) => new("STUDIO_FORBIDDEN", message, 403, "query.statement");
}
