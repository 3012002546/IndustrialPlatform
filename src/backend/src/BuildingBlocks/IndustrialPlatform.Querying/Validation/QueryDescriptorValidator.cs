using IndustrialPlatform.Querying.Descriptors;
using IndustrialPlatform.Querying.Schema;

namespace IndustrialPlatform.Querying.Validation;

public static class QueryDescriptorValidator
{
    public static QueryDescriptor Validate(QueryDescriptor descriptor, QueryResourceDefinition schema)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(schema);

        if (descriptor.PageIndex < 1 || descriptor.PageSize is < 1 or > QueryLimits.MaxPageSize)
        {
            throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "分页参数超出平台限制。");
        }

        if (descriptor.Filters.Count > QueryLimits.MaxFilterNodes)
        {
            throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "过滤条件超过平台限制。");
        }

        if (descriptor.OrderBy.Count > QueryLimits.MaxSortFields)
        {
            throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "排序字段超过平台限制。");
        }

        foreach (var field in descriptor.Select)
        {
            EnsureField(schema, field, field => field.Selectable);
        }

        foreach (var filter in descriptor.Filters)
        {
            EnsureField(schema, filter.Field, field => field.Filterable);
        }

        foreach (var sort in descriptor.OrderBy)
        {
            EnsureField(schema, sort.Field, field => field.Sortable);
        }

        return descriptor;
    }

    private static void EnsureField(
        QueryResourceDefinition schema,
        string name,
        Func<QueryFieldDefinition, bool> capability)
    {
        if (!schema.TryGetField(name, out var definition) || !capability(definition))
        {
            throw Invalid("PLATFORM_QUERY_FIELD_NOT_ALLOWED", $"查询字段未注册或不可用：{name}。", name);
        }
    }

    private static QueryValidationException Invalid(string code, string message, string? parameter = null)
        => new(new QueryValidationError(code, message, parameter));
}
