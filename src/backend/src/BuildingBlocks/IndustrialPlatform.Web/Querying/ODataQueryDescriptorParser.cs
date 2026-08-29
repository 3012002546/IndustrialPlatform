using IndustrialPlatform.Querying.Descriptors;
using IndustrialPlatform.Querying.Schema;
using IndustrialPlatform.Querying.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Query.Validator;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace IndustrialPlatform.Web.Querying;

/// <summary>
/// Converts the deliberately small OData surface into the platform descriptor.
/// The official OData URI parser and AST validator own protocol syntax; this
/// adapter owns the platform capability and execution contract.
/// </summary>
public sealed class ODataQueryDescriptorParser
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

        var model = BuildModel(schema);
        var options = ReadODataOptions(query);
        var parser = new ODataQueryOptionParser(model.Model, model.EntityType, model.EntitySet, options);
        var context = new ODataQueryContext(
            model.Model,
            model.EntityType,
            new ODataPath(new EntitySetSegment(model.EntitySet)));
        var validation = BuildValidationSettings(schema);

        var top = ParseTop(parser);
        var skip = ParseSkip(parser);
        if (skip % top != 0)
        {
            throw Invalid(
                "PLATFORM_QUERY_PAGING_ALIGNMENT_REQUIRED",
                "$skip 必须按 $top 对齐。",
                "$skip");
        }

        var filters = ParseFilters(parser, options, context, validation, schema);
        var select = ParseSelect(parser, options, context, validation, schema, options.ContainsKey("$select"));
        var orderBy = ParseOrderBy(parser, options, context, validation, schema);
        var includeCount = ParseCount(parser);

        var descriptor = new QueryDescriptor(
            filters,
            orderBy,
            select,
            checked((int)(skip / top) + 1),
            checked((int)top),
            IncludeCount: includeCount);
        return QueryDescriptorValidator.Validate(descriptor, schema);
    }

    private long ParseTop(ODataQueryOptionParser parser)
    {
        try
        {
            var top = parser.ParseTop();
            if (top is null or < 1)
            {
                throw Invalid("PLATFORM_QUERY_INVALID", "$top 必须为正整数。", "$top");
            }

            if (top > _settings.MaxTop)
            {
                throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "$top 超过平台限制。", "$top");
            }

            return top.Value;
        }
        catch (QueryValidationException)
        {
            throw;
        }
        catch (ODataException)
        {
            throw Invalid("PLATFORM_QUERY_INVALID", "$top 必须为正整数。", "$top");
        }
    }

    private static long ParseSkip(ODataQueryOptionParser parser)
    {
        try
        {
            var skip = parser.ParseSkip() ?? 0;
            if (skip < 0)
            {
                throw Invalid("PLATFORM_QUERY_INVALID", "$skip 必须为非负整数。", "$skip");
            }

            return skip;
        }
        catch (QueryValidationException)
        {
            throw;
        }
        catch (ODataException)
        {
            throw Invalid("PLATFORM_QUERY_INVALID", "$skip 必须为非负整数。", "$skip");
        }
    }

    private static bool ParseCount(ODataQueryOptionParser parser)
    {
        try
        {
            return parser.ParseCount() ?? false;
        }
        catch (ODataException)
        {
            throw Invalid("PLATFORM_QUERY_INVALID", "$count 必须为 true 或 false。", "$count");
        }
    }

    private List<QueryFilter> ParseFilters(
        ODataQueryOptionParser parser,
        Dictionary<string, string> options,
        ODataQueryContext context,
        ODataValidationSettings validation,
        QueryResourceDefinition schema)
    {
        if (!options.TryGetValue("$filter", out var raw)) return [];

        try
        {
            var option = new FilterQueryOption(raw, context, parser);
            // The official validator also checks operators, functions and
            // node limits. Its generated EDM has no Capabilities annotation,
            // so its default "not filterable" property result is intentionally
            // deferred to the platform schema check below.
            ValidateFilterOption(option, validation);
            var filters = new List<QueryFilter>();
            if (CountNodes(option.FilterClause.Expression) > _settings.MaxFilterNodes)
            {
                throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "过滤节点超过平台限制。", "$filter");
            }

            if (CountFunctionDepth(option.FilterClause.Expression) > _settings.MaxFunctionDepth)
            {
                throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "过滤函数嵌套超过平台限制。", "$filter");
            }

            VisitFilter(option.FilterClause.Expression, filters, schema, 0);
            if (filters.Count > _settings.MaxFilterNodes)
            {
                throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "过滤节点超过平台限制。", "$filter");
            }

            return filters;
        }
        catch (QueryValidationException)
        {
            throw;
        }
        catch (ODataException ex)
        {
            var code = ex.Message.Contains("depth", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("function call", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("node", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("limit", StringComparison.OrdinalIgnoreCase)
                ? "PLATFORM_QUERY_LIMIT_EXCEEDED"
                : "PLATFORM_QUERY_INVALID";
            throw Invalid(code, "无法解析 $filter。", "$filter");
        }
    }

    private static List<string> ParseSelect(
        ODataQueryOptionParser parser,
        Dictionary<string, string> options,
        ODataQueryContext context,
        ODataValidationSettings validation,
        QueryResourceDefinition schema,
        bool hasSelect)
    {
        if (!hasSelect)
        {
            return schema.Fields.Where(pair => pair.Value.Selectable).Select(pair => pair.Key).ToList();
        }

        try
        {
            var option = new SelectExpandQueryOption(options["$select"], null, context, parser);
            ValidateSelectOption(option, validation);
            var fields = new List<string>();
            foreach (var item in option.SelectExpandClause.SelectedItems)
            {
                if (item is not PathSelectItem path ||
                    path.SelectedPath.Count != 1 ||
                    path.SelectedPath.LastSegment is not PropertySegment property)
                {
                    throw Invalid("PLATFORM_QUERY_INVALID", "$select 只允许选择资源的直接字段。", "$select");
                }

                var field = property.Property.Name;
                EnsureField(schema, field, definition => definition.Selectable);
                if (!fields.Contains(field, StringComparer.Ordinal))
                {
                    fields.Add(field);
                }
            }

            if (fields.Count == 0)
            {
                throw Invalid("PLATFORM_QUERY_INVALID", "$select 不能为空。", "$select");
            }

            return fields;
        }
        catch (QueryValidationException)
        {
            throw;
        }
        catch (ODataException)
        {
            throw Invalid("PLATFORM_QUERY_FIELD_NOT_ALLOWED", "查询字段未注册或不可用。", "$select");
        }
    }

    private List<QuerySort> ParseOrderBy(
        ODataQueryOptionParser parser,
        Dictionary<string, string> options,
        ODataQueryContext context,
        ODataValidationSettings validation,
        QueryResourceDefinition schema)
    {
        if (!options.TryGetValue("$orderby", out var raw)) return AddTieBreaker([], schema);

        try
        {
            var option = new OrderByQueryOption(raw, context, parser);
            ValidateOrderByOption(option, validation);
            var result = new List<QuerySort>();
            for (var clause = option.OrderByClause; clause is not null; clause = clause.ThenBy)
            {
                if (clause.Expression is not SingleValuePropertyAccessNode property)
                {
                    throw Invalid("PLATFORM_QUERY_INVALID", "$orderby 只允许直接字段。", "$orderby");
                }

                var field = property.Property.Name;
                EnsureField(schema, field, definition => definition.Sortable);
                result.Add(new QuerySort(
                    field,
                    clause.Direction == OrderByDirection.Descending
                        ? QuerySortDirection.Desc
                        : QuerySortDirection.Asc));
            }

            return AddTieBreaker(result, schema);
        }
        catch (QueryValidationException)
        {
            throw;
        }
        catch (ODataException ex)
        {
            var code = ex.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("limit", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("node", StringComparison.OrdinalIgnoreCase)
                ? "PLATFORM_QUERY_LIMIT_EXCEEDED"
                : "PLATFORM_QUERY_INVALID";
            throw Invalid(code, "无法解析 $orderby。", "$orderby");
        }
    }

    private List<QuerySort> AddTieBreaker(List<QuerySort> result, QueryResourceDefinition schema)
    {
        if (result.Count > _settings.MaxSortFields)
        {
            throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "排序字段超过平台限制。", "$orderby");
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

    private static void ValidateOrderByOption(OrderByQueryOption option, ODataValidationSettings validation)
    {
        try
        {
            option.Validate(validation);
        }
        catch (ODataException) when (HasDisallowedOrderByProperty(option, validation))
        {
            // The generated EDM has no Capabilities annotation. Defer the
            // field capability decision to the platform query schema below.
        }
    }

    private static bool HasDisallowedOrderByProperty(
        OrderByQueryOption option,
        ODataValidationSettings validation)
    {
        var hasDisallowedProperty = false;
        for (var clause = option.OrderByClause; clause is not null; clause = clause.ThenBy)
        {
            if (clause.Expression is not SingleValuePropertyAccessNode property)
            {
                return false;
            }

            hasDisallowedProperty |= !validation.AllowedOrderByProperties.Contains(property.Property.Name);
        }

        return hasDisallowedProperty;
    }

    private static void VisitFilter(
        SingleValueNode node,
        ICollection<QueryFilter> filters,
        QueryResourceDefinition schema,
        int functionDepth)
    {
        switch (node)
        {
            case ConvertNode convert:
                VisitFilter(convert.Source, filters, schema, functionDepth);
                return;

            case BinaryOperatorNode { OperatorKind: BinaryOperatorKind.And } binary:
                VisitFilter(binary.Left, filters, schema, functionDepth);
                VisitFilter(binary.Right, filters, schema, functionDepth);
                return;

            case BinaryOperatorNode binary:
                filters.Add(ToBinaryFilter(binary, schema));
                return;

            case InNode inNode:
                filters.Add(ToInFilter(inNode, schema));
                return;

            case SingleValueFunctionCallNode function:
                if (functionDepth >= QueryLimits.MaxFunctionDepth)
                {
                    throw Invalid("PLATFORM_QUERY_LIMIT_EXCEEDED", "过滤函数嵌套超过平台限制。", "$filter");
                }

                filters.Add(ToFunctionFilter(function, schema));
                return;

            default:
                throw Invalid("PLATFORM_QUERY_INVALID", "过滤表达式包含禁用的导航、逻辑或算术语法。", "$filter");
        }
    }

    private static QueryFilter ToBinaryFilter(BinaryOperatorNode node, QueryResourceDefinition schema)
    {
        var property = Property(node.Left, schema);
        if (Unwrap(node.Right) is not ConstantNode constant)
        {
            throw Invalid("PLATFORM_QUERY_INVALID", "过滤值必须是标量字面量。", "$filter");
        }

        var @operator = node.OperatorKind switch
        {
            BinaryOperatorKind.Equal => QueryOperator.Eq,
            BinaryOperatorKind.NotEqual => QueryOperator.Ne,
            BinaryOperatorKind.GreaterThan => QueryOperator.Gt,
            BinaryOperatorKind.GreaterThanOrEqual => QueryOperator.Ge,
            BinaryOperatorKind.LessThan => QueryOperator.Lt,
            BinaryOperatorKind.LessThanOrEqual => QueryOperator.Le,
            _ => throw Invalid("PLATFORM_QUERY_INVALID", "过滤表达式包含禁用的逻辑或算术语法。", "$filter"),
        };
        return new QueryFilter(property, @operator, constant.Value);
    }

    private static QueryFilter ToInFilter(InNode node, QueryResourceDefinition schema)
    {
        var property = Property(node.Left, schema);
        if (node.Right is not CollectionConstantNode collection || collection.Collection.Count == 0)
        {
            throw Invalid("PLATFORM_QUERY_INVALID", "in 集合不能为空。", "$filter");
        }

        return new QueryFilter(property, QueryOperator.In, collection.Collection.Select(item => item.Value).ToArray());
    }

    private static QueryFilter ToFunctionFilter(SingleValueFunctionCallNode node, QueryResourceDefinition schema)
    {
        var @operator = node.Name.ToLowerInvariant() switch
        {
            "contains" => QueryOperator.Contains,
            "startswith" => QueryOperator.StartsWith,
            _ => throw Invalid("PLATFORM_QUERY_INVALID", "过滤函数不受支持。", "$filter"),
        };
        var parameters = node.Parameters.Cast<SingleValueNode>().ToArray();
        if (parameters.Length != 2 || Unwrap(parameters[1]) is not ConstantNode constant || constant.Value is not string)
        {
            throw Invalid("PLATFORM_QUERY_INVALID", "过滤函数参数无效。", "$filter");
        }

        return new QueryFilter(Property(parameters[0], schema), @operator, constant.Value);
    }

    private static void ValidateFilterOption(FilterQueryOption option, ODataValidationSettings validation)
    {
        try
        {
            option.Validate(validation);
        }
        catch (ODataException ex) when (
            ex.Message.Contains("cannot be used in the $filter query option", StringComparison.OrdinalIgnoreCase))
        {
            // Field capability is owned by QueryResourceDefinition. The
            // generated EDM deliberately does not invent service metadata.
        }
    }

    private static void ValidateSelectOption(SelectExpandQueryOption option, ODataValidationSettings validation)
    {
        try
        {
            option.Validate(validation);
        }
        catch (ODataException ex) when (
            ex.Message.Contains("cannot be used in the $select query option", StringComparison.OrdinalIgnoreCase))
        {
            // Selectable is owned by QueryResourceDefinition, not by an
            // omitted Capabilities vocabulary annotation.
        }
    }

    private static string Property(SingleValueNode node, QueryResourceDefinition schema)
    {
        if (Unwrap(node) is not SingleValuePropertyAccessNode property)
        {
            throw Invalid("PLATFORM_QUERY_INVALID", "过滤字段必须是资源的直接字段。", "$filter");
        }

        var name = property.Property.Name;
        EnsureField(schema, name, definition => definition.Filterable);
        return name;
    }

    private static SingleValueNode Unwrap(SingleValueNode node)
    {
        while (node is ConvertNode convert) node = convert.Source;
        return node;
    }

    private static int CountFunctionDepth(SingleValueNode node)
        => node switch
        {
            SingleValueFunctionCallNode function => 1 + function.Parameters
                .OfType<SingleValueNode>()
                .Select(CountFunctionDepth)
                .DefaultIfEmpty(0)
                .Max(),
            BinaryOperatorNode binary => Math.Max(CountFunctionDepth(binary.Left), CountFunctionDepth(binary.Right)),
            InNode inNode => CountFunctionDepth(inNode.Left),
            UnaryOperatorNode unary => CountFunctionDepth(unary.Operand),
            ConvertNode convert => CountFunctionDepth(convert.Source),
            _ => 0,
        };

    private static int CountNodes(SingleValueNode node)
        => node switch
        {
            BinaryOperatorNode binary => 1 + CountNodes(binary.Left) + CountNodes(binary.Right),
            InNode inNode => 1 + CountNodes(inNode.Left) + (inNode.Right is CollectionConstantNode collection
                ? collection.Collection.Count
                : 1),
            SingleValueFunctionCallNode function => 1 + function.Parameters
                .OfType<SingleValueNode>()
                .Select(CountNodes)
                .Sum(),
            UnaryOperatorNode unary => 1 + CountNodes(unary.Operand),
            ConvertNode convert => 1 + CountNodes(convert.Source),
            _ => 1,
        };

    private static Dictionary<string, string> ReadODataOptions(IQueryCollection query)
        => query
            .Where(pair => AllowedOptions.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);

    private ODataValidationSettings BuildValidationSettings(QueryResourceDefinition schema)
    {
        var settings = new ODataValidationSettings
        {
            AllowedArithmeticOperators = AllowedArithmeticOperators.None,
            AllowedFunctions = AllowedFunctions.Contains | AllowedFunctions.StartsWith,
            AllowedLogicalOperators = AllowedLogicalOperators.And |
                AllowedLogicalOperators.Equal |
                AllowedLogicalOperators.NotEqual |
                AllowedLogicalOperators.GreaterThan |
                AllowedLogicalOperators.GreaterThanOrEqual |
                AllowedLogicalOperators.LessThan |
                AllowedLogicalOperators.LessThanOrEqual,
            AllowedQueryOptions = AllowedQueryOptions.Filter |
                AllowedQueryOptions.Select |
                AllowedQueryOptions.OrderBy |
                AllowedQueryOptions.Top |
                AllowedQueryOptions.Skip |
                AllowedQueryOptions.Count,
            MaxOrderByNodeCount = _settings.MaxSortFields,
            // OData requires this setting to be at least one. A platform
            // value of zero is still enforced by CountFunctionDepth below.
            MaxFunctionCallDepth = Math.Max(1, _settings.MaxFunctionDepth),
            // The OData validator requires a positive value; Any/All remain
            // disabled by AllowedFunctions and the AST conversion below.
            MaxAnyAllExpressionDepth = 1,
            MaxNodeCount = _settings.MaxFilterNodes,
            MaxTop = _settings.MaxTop,
        };
        settings.AllowedOrderByProperties.UnionWith(
            schema.Fields.Where(pair => pair.Value.Sortable).Select(pair => pair.Key));
        return settings;
    }

    private static (IEdmModel Model, IEdmEntityType EntityType, IEdmEntitySet EntitySet) BuildModel(
        QueryResourceDefinition schema)
    {
        var model = new EdmModel();
        var entityType = new EdmEntityType("IndustrialPlatform.Querying", "QueryResource");
        var properties = new Dictionary<string, IEdmStructuralProperty>(StringComparer.Ordinal);
        foreach (var field in schema.Fields)
        {
            var kind = field.Value.ValueType switch
            {
                QueryValueType.Text => EdmPrimitiveTypeKind.String,
                QueryValueType.Number => EdmPrimitiveTypeKind.Int64,
                QueryValueType.Boolean => EdmPrimitiveTypeKind.Boolean,
                QueryValueType.Date => EdmPrimitiveTypeKind.DateTimeOffset,
                _ => throw Invalid("PLATFORM_QUERY_INVALID", "查询字段类型未注册。", field.Key),
            };
            properties[field.Key] = entityType.AddStructuralProperty(field.Key, kind, true);
        }

        entityType.AddKeys(properties.Values.Take(1));
        model.AddElement(entityType);
        var container = new EdmEntityContainer("IndustrialPlatform.Querying", "QueryContainer");
        var entitySet = container.AddEntitySet(schema.Name, entityType);
        model.AddElement(container);
        return (model, entityType, entitySet);
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

    private static QueryValidationException Invalid(string code, string message, string? parameter)
        => new(new QueryValidationError(code, message, parameter));
}
