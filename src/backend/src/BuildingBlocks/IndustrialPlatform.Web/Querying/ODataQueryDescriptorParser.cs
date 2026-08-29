using System.Globalization;
using System.Text.RegularExpressions;
using IndustrialPlatform.Querying.Descriptors;
using IndustrialPlatform.Querying.Schema;
using IndustrialPlatform.Querying.Validation;
using Microsoft.AspNetCore.Http;

namespace IndustrialPlatform.Web.Querying;

/// <summary>
/// Parses the deliberately small OData surface into the platform descriptor.
/// It does not expose IQueryable and does not permit navigation into another resource.
/// </summary>
public sealed partial class ODataQueryDescriptorParser
{
    private static readonly HashSet<string> AllowedOptions =
        ["$filter", "$select", "$orderby", "$top", "$skip", "$count"];

    private readonly ODataQueryValidationSettings _settings;

    public ODataQueryDescriptorParser(ODataQueryValidationSettings? settings = null)
    {
        _settings = settings ?? new ODataQueryValidationSettings();
    }

    public QueryDescriptor Parse(IQueryCollection query, QueryResourceDefinition schema)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(schema);

        foreach (var key in query.Keys)
        {
            if (key.StartsWith('$') && !AllowedOptions.Contains(key))
            {
                throw Invalid("PLATFORM_QUERY_OPTION_NOT_ALLOWED", $"OData 查询选项不受支持：{key}。", key);
            }
        }

        var top = ParseRequiredPositiveInt(query, "$top", _settings.MaxTop);
        var skip = ParseNonNegativeInt(query, "$skip");
        if (skip % top != 0)
        {
            throw Invalid(
                "PLATFORM_QUERY_PAGING_ALIGNMENT_REQUIRED",
                "$skip 必须按 $top 对齐。",
                "$skip");
        }

        var filters = ParseFilters(Read(query, "$filter"), schema);
        var select = ParseSelect(Read(query, "$select"), schema);
        var orderBy = ParseOrderBy(Read(query, "$orderby"), schema);
        var includeCount = ParseBoolean(Read(query, "$count"), "$count");

        var descriptor = new QueryDescriptor(
            filters,
            orderBy,
            select,
            skip / top + 1,
            top,
            IncludeCount: includeCount);
        return QueryDescriptorValidator.Validate(descriptor, schema);
    }

    private List<QueryFilter> ParseFilters(string? raw, QueryResourceDefinition schema)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        if (raw.Contains(" any", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains(" all", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains('/', StringComparison.Ordinal) ||
            CastExpression().IsMatch(raw) ||
            raw.Contains(" or ", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("PLATFORM_QUERY_INVALID", "过滤表达式包含禁用的导航、逻辑或算术语法。", "$filter");
        }

        var functionCount = FunctionExpression().Count(raw);
        if (functionCount > _settings.MaxFunctionDepth)
        {
            throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "过滤函数嵌套超过平台限制。", "$filter");
        }

        var terms = SplitTopLevel(raw, "and");
        if (terms.Count > _settings.MaxFilterNodes)
        {
            throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "过滤节点超过平台限制。", "$filter");
        }

        var filters = new List<QueryFilter>(terms.Count);
        foreach (var term in terms)
        {
            var function = FunctionFilter().Match(term.Trim());
            if (function.Success)
            {
                var name = function.Groups[1].Value.ToLowerInvariant();
                var field = function.Groups[2].Value;
                EnsureField(schema, field, definition => definition.Filterable);
                filters.Add(new QueryFilter(
                    field,
                    name == "contains" ? QueryOperator.Contains : QueryOperator.StartsWith,
                    Unquote(function.Groups[3].Value)));
                continue;
            }

            var inMatch = InFilter().Match(term.Trim());
            if (inMatch.Success)
            {
                var field = inMatch.Groups[1].Value;
                EnsureField(schema, field, definition => definition.Filterable);
                var values = SplitComma(inMatch.Groups[2].Value).Select(ParseLiteral).ToArray();
                if (values.Length == 0) throw Invalid("PLATFORM_QUERY_INVALID", "in 集合不能为空。", "$filter");
                filters.Add(new QueryFilter(field, QueryOperator.In, values));
                continue;
            }

            var binary = BinaryFilter().Match(term.Trim());
            if (!binary.Success)
            {
                throw Invalid("PLATFORM_QUERY_INVALID", "无法解析 $filter。", "$filter");
            }

            var binaryField = binary.Groups[1].Value;
            EnsureField(schema, binaryField, definition => definition.Filterable);
            filters.Add(new QueryFilter(
                binaryField,
                ParseOperator(binary.Groups[2].Value),
                ParseLiteral(binary.Groups[3].Value.Trim())));
        }

        return filters;
    }

    private static List<string> ParseSelect(string? raw, QueryResourceDefinition schema)
    {
        var fields = string.IsNullOrWhiteSpace(raw)
            ? schema.Fields.Where(pair => pair.Value.Selectable).Select(pair => pair.Key).ToList()
            : SplitComma(raw).Select(value => value.Trim()).ToList();
        if (fields.Count == 0 || fields.Any(string.IsNullOrWhiteSpace))
        {
            throw Invalid("PLATFORM_QUERY_INVALID", "$select 不能为空。", "$select");
        }

        if (fields.Distinct(StringComparer.Ordinal).Count() != fields.Count)
        {
            throw Invalid("PLATFORM_QUERY_INVALID", "$select 不得包含重复字段。", "$select");
        }

        foreach (var field in fields) EnsureField(schema, field, definition => definition.Selectable);
        return fields;
    }

    private List<QuerySort> ParseOrderBy(string? raw, QueryResourceDefinition schema)
    {
        var fields = string.IsNullOrWhiteSpace(raw)
            ? []
            : SplitComma(raw).Select(value => value.Trim()).ToList();
        if (fields.Count > _settings.MaxSortFields)
        {
            throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "排序字段超过平台限制。", "$orderby");
        }

        var result = new List<QuerySort>(fields.Count + 1);
        foreach (var field in fields)
        {
            var match = SortField().Match(field);
            if (!match.Success) throw Invalid("PLATFORM_QUERY_INVALID", "无法解析 $orderby。", "$orderby");
            var name = match.Groups[1].Value;
            EnsureField(schema, name, definition => definition.Sortable);
            result.Add(new QuerySort(
                name,
                string.Equals(match.Groups[2].Value, "desc", StringComparison.OrdinalIgnoreCase)
                    ? QuerySortDirection.Desc
                    : QuerySortDirection.Asc));
        }

        if (!string.IsNullOrWhiteSpace(schema.TieBreaker) &&
            !result.Any(item => item.Field.Equals(schema.TieBreaker, StringComparison.Ordinal)))
        {
            if (result.Count >= _settings.MaxSortFields)
            {
                throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "排序字段必须为稳定 tie-breaker 留出位置。", "$orderby");
            }
            EnsureField(schema, schema.TieBreaker, definition => definition.Sortable);
            result.Add(new QuerySort(schema.TieBreaker, QuerySortDirection.Asc));
        }

        return result;
    }

    private static string? Read(IQueryCollection query, string key)
        => query.TryGetValue(key, out var value) ? value.ToString() : null;

    private static int ParseRequiredPositiveInt(IQueryCollection query, string key, int max)
    {
        var value = Read(query, key);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < 1)
        {
            throw Invalid("PLATFORM_QUERY_INVALID", $"{key} 必须为正整数。", key);
        }
        if (result > max) throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", $"{key} 超过平台限制。", key);
        return result;
    }

    private static int ParseNonNegativeInt(IQueryCollection query, string key)
    {
        var value = Read(query, key);
        if (value is null) return 0;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < 0)
        {
            throw Invalid("PLATFORM_QUERY_INVALID", $"{key} 必须为非负整数。", key);
        }
        return result;
    }

    private static bool ParseBoolean(string? value, string key)
    {
        if (value is null) return false;
        if (bool.TryParse(value, out var result)) return result;
        throw Invalid("PLATFORM_QUERY_INVALID", $"{key} 必须为 true 或 false。", key);
    }

    private static object ParseLiteral(string raw)
    {
        raw = raw.Trim();
        if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'') return Unquote(raw);
        if (bool.TryParse(raw, out var boolean)) return boolean;
        if ((raw.Contains('-', StringComparison.Ordinal) || raw.Contains('T', StringComparison.Ordinal)) &&
            DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)) return date;
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) return number;
        throw Invalid("PLATFORM_QUERY_INVALID", "过滤值必须是受支持的 OData 字面量。", "$filter");
    }

    private static string Unquote(string value)
    {
        var unquoted = value.Length >= 2 && value[0] == '\'' && value[^1] == '\''
            ? value[1..^1]
            : value;
        return unquoted.Replace("''", "'", StringComparison.Ordinal);
    }

    private static QueryOperator ParseOperator(string value)
        => value.ToLowerInvariant() switch
        {
            "eq" => QueryOperator.Eq,
            "ne" => QueryOperator.Ne,
            "gt" => QueryOperator.Gt,
            "ge" => QueryOperator.Ge,
            "lt" => QueryOperator.Lt,
            "le" => QueryOperator.Le,
            _ => throw Invalid("PLATFORM_QUERY_INVALID", "不支持的过滤操作符。", "$filter"),
        };

    private static void EnsureField(
        QueryResourceDefinition schema,
        string name,
        Func<QueryFieldDefinition, bool> capability)
    {
        if (name.Contains('.', StringComparison.Ordinal) ||
            !schema.TryGetField(name, out var definition) ||
            !capability(definition))
        {
            throw Invalid("PLATFORM_QUERY_FIELD_NOT_ALLOWED", $"查询字段未注册或不可用：{name}。", name);
        }
    }

    private static List<string> SplitTopLevel(string value, string separator)
    {
        var result = new List<string>();
        var depth = 0;
        var quoted = false;
        var start = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\'' && (index + 1 >= value.Length || value[index + 1] != '\'')) quoted = !quoted;
            if (quoted) continue;
            if (value[index] == '(') depth++;
            else if (value[index] == ')') depth--;
            var tokenLength = separator.Length + 2;
            if (depth == 0 && index + tokenLength <= value.Length &&
                value.AsSpan(index, tokenLength).Equals($" {separator} ", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(value[start..index]);
                start = index + separator.Length + 2;
                index += separator.Length + 1;
            }
        }
        result.Add(value[start..]);
        return result;
    }

    private static IEnumerable<string> SplitComma(string value)
    {
        var start = 0;
        var depth = 0;
        var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\'' && (index + 1 >= value.Length || value[index + 1] != '\'')) quoted = !quoted;
            if (quoted) continue;
            if (value[index] == '(') depth++;
            else if (value[index] == ')') depth--;
            else if (value[index] == ',' && depth == 0)
            {
                yield return value[start..index];
                start = index + 1;
            }
        }
        yield return value[start..];
    }

    private static QueryValidationException Invalid(string code, string message, string? parameter)
        => new(new QueryValidationError(code, message, parameter));

    [GeneratedRegex(@"^(contains|startswith)\(([A-Za-z_][A-Za-z0-9_]*)\s*,\s*('(?:''|[^'])*')\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FunctionFilter();

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_]*)\s+in\s*\((.*)\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InFilter();

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_]*)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BinaryFilter();

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_]*)(?:\s+(asc|desc))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SortField();

    [GeneratedRegex(@"\b(?:contains|startswith)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FunctionExpression();

    [GeneratedRegex(@"\bcast\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CastExpression();

}
