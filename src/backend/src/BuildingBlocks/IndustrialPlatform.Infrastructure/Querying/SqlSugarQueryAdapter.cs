using System.Globalization;
using IndustrialPlatform.Querying.Descriptors;
using SqlSugar;

namespace IndustrialPlatform.Infrastructure.Querying;

public sealed record SqlSugarQueryPlan(
    IReadOnlyList<string> FilterSql,
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyList<string> OrderBySql,
    int Skip,
    int PageSize);

/// <summary>
/// Appends a validated descriptor to a service-owned base query. The caller is
/// responsible for applying tenant, data scope, soft-delete and field permission first.
/// </summary>
public static class SqlSugarQueryAdapter
{
    public static SqlSugarQueryPlan BuildPlan<TEntity>(
        QueryDescriptor descriptor,
        SqlSugarQueryFieldMap<TEntity> fieldMap)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(fieldMap);
        var filters = new List<string>();
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var parameterIndex = 0;
        foreach (var filter in descriptor.Filters)
        {
            var column = fieldMap.Resolve(filter.Field);
            filters.Add(BuildFilterSql(filter, column, parameters, ref parameterIndex));
        }

        var orderBy = descriptor.OrderBy
            .Select(sort => $"{fieldMap.Resolve(sort.Field)} {(sort.Direction == QuerySortDirection.Desc ? "DESC" : "ASC")}")
            .ToList();
        if (!descriptor.OrderBy.Any(sort => sort.Field.Equals(fieldMap.TieBreaker, StringComparison.Ordinal)))
        {
            orderBy.Add($"{fieldMap.Resolve(fieldMap.TieBreaker)} ASC");
        }

        return new SqlSugarQueryPlan(
            filters,
            parameters,
            orderBy,
            checked((descriptor.PageIndex - 1) * descriptor.PageSize),
            descriptor.PageSize);
    }

    public static ISugarQueryable<TEntity> Apply<TEntity>(
        ISugarQueryable<TEntity> baseQuery,
        QueryDescriptor descriptor,
        SqlSugarQueryFieldMap<TEntity> fieldMap)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(baseQuery);
        var plan = BuildPlan(descriptor, fieldMap);
        var query = plan.FilterSql.Count == 0
            ? baseQuery
            : baseQuery.Where(string.Join(" AND ", plan.FilterSql), plan.Parameters);
        return query
            .OrderBy(string.Join(", ", plan.OrderBySql))
            .Skip(plan.Skip)
            .Take(plan.PageSize);
    }

    private static string BuildFilterSql(
        QueryFilter filter,
        string column,
        Dictionary<string, object?> parameters,
        ref int parameterIndex)
    {
        var name = $"p{parameterIndex++}";
        switch (filter.Operator)
        {
            case QueryOperator.Contains:
                parameters[name] = $"%{Convert.ToString(filter.Value, CultureInfo.InvariantCulture)}%";
                return $"{column} LIKE @{name}";
            case QueryOperator.StartsWith:
                parameters[name] = $"{Convert.ToString(filter.Value, CultureInfo.InvariantCulture)}%";
                return $"{column} LIKE @{name}";
            case QueryOperator.Eq:
                parameters[name] = filter.Value;
                return $"{column} = @{name}";
            case QueryOperator.Ne:
                parameters[name] = filter.Value;
                return $"{column} <> @{name}";
            case QueryOperator.Gt:
                parameters[name] = filter.Value;
                return $"{column} > @{name}";
            case QueryOperator.Ge:
                parameters[name] = filter.Value;
                return $"{column} >= @{name}";
            case QueryOperator.Lt:
                parameters[name] = filter.Value;
                return $"{column} < @{name}";
            case QueryOperator.Le:
                parameters[name] = filter.Value;
                return $"{column} <= @{name}";
            case QueryOperator.Between:
                return BuildBetweenSql(column, filter.Value, parameters, ref parameterIndex);
            case QueryOperator.In:
                return BuildInSql(column, filter.Value, parameters, ref parameterIndex);
            default:
                throw new ArgumentOutOfRangeException(nameof(filter));
        }
    }

    private static string BuildBetweenSql(
        string column,
        object? value,
        Dictionary<string, object?> parameters,
        ref int parameterIndex)
    {
        if (value is not IEnumerable<object?> values || values.Take(2).Count() != 2)
        {
            throw new ArgumentException("between 必须包含两个值。", nameof(value));
        }
        var pair = values.Take(2).ToArray();
        var start = $"p{parameterIndex++}";
        var end = $"p{parameterIndex++}";
        parameters[start] = pair[0];
        parameters[end] = pair[1];
        return $"{column} >= @{start} AND {column} <= @{end}";
    }

    private static string BuildInSql(
        string column,
        object? value,
        Dictionary<string, object?> parameters,
        ref int parameterIndex)
    {
        if (value is not System.Collections.IEnumerable values || value is string)
        {
            throw new ArgumentException("in 必须包含值集合。", nameof(value));
        }
        var names = new List<string>();
        foreach (var item in values)
        {
            var name = $"p{parameterIndex++}";
            parameters[name] = item;
            names.Add($"@{name}");
        }
        if (names.Count == 0) throw new ArgumentException("in 集合不能为空。", nameof(value));
        return $"{column} IN ({string.Join(", ", names)})";
    }
}
