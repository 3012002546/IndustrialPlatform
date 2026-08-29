using IndustrialPlatform.Identity.Contracts.Management;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Querying.Descriptors;
using IndustrialPlatform.Querying.Schema;
using IndustrialPlatform.Querying.Validation;

namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// Identity-owned stable user read model for the controlled OData sample.
/// It contains no password, token or persistence-only credential fields.
/// </summary>
public sealed record UserQueryResource(
    string UserNId,
    string LoginName,
    string Name,
    string? Email,
    string? Phone,
    string Status,
    string TenantNId,
    DateTimeOffset CreatedOn,
    DateTimeOffset? LastLoginOn,
    bool MustChangePassword,
    IReadOnlyList<string> DirectRoleNIds,
    IReadOnlyList<string> GroupRoleNIds,
    IReadOnlyList<string> EffectiveRoleNIds,
    int EffectiveRoleCount,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    bool IsDeleted)
{
    public static QueryResourceDefinition Definition { get; } = new(
        "users",
        new Dictionary<string, QueryFieldDefinition>(StringComparer.Ordinal)
        {
            ["userNId"] = QueryFieldDefinition.Text(),
            ["loginName"] = QueryFieldDefinition.Text(),
            ["name"] = QueryFieldDefinition.Text(),
            ["email"] = QueryFieldDefinition.Text(),
            ["phone"] = QueryFieldDefinition.Text(),
            ["status"] = QueryFieldDefinition.Text(),
            ["tenantNId"] = QueryFieldDefinition.Text(selectable: true, filterable: false, sortable: false),
            ["createdOn"] = QueryFieldDefinition.Date(),
            ["lastLoginOn"] = QueryFieldDefinition.Date(),
            ["mustChangePassword"] = QueryFieldDefinition.Boolean(),
            ["directRoleNIds"] = QueryFieldDefinition.Text(filterable: false, sortable: false),
            ["groupRoleNIds"] = QueryFieldDefinition.Text(filterable: false, sortable: false),
            ["effectiveRoleNIds"] = QueryFieldDefinition.Text(filterable: false, sortable: false),
            // These read-model values are projected for detail/export only;
            // the SqlSugar query map intentionally does not execute them.
            ["effectiveRoleCount"] = QueryFieldDefinition.Number(filterable: false, sortable: false),
            ["optimisticVersion"] = QueryFieldDefinition.Number(filterable: false, sortable: false),
            ["concurrencyVersion"] = QueryFieldDefinition.Text(filterable: false, sortable: false),
            ["isDeleted"] = QueryFieldDefinition.Boolean(filterable: false, sortable: false),
        },
        tieBreaker: "userNId");

    public static UserQueryResource From(UserSummary summary)
        => new(
            summary.UserNId,
            summary.LoginName,
            summary.Name,
            summary.Email,
            summary.Phone,
            summary.Status,
            summary.TenantNId,
            summary.CreatedOn,
            summary.LastLoginOn,
            summary.MustChangePassword,
            summary.DirectRoleNIds,
            summary.GroupRoleNIds,
            summary.EffectiveRoleNIds,
            summary.EffectiveRoleNIds.Count,
            summary.OptimisticVersion,
            summary.ConcurrencyVersion,
            summary.IsDeleted);

    public static UserListFilter ToLegacyFilter(string tenantNId, QueryDescriptor descriptor)
    {
        string? nId = null;
        string? loginName = null;
        string? name = null;
        string? email = null;
        string? phone = null;
        UserStatus? status = null;
        bool? mustChangePassword = null;
        DateTimeOffset? lastLoginFrom = null;
        DateTimeOffset? lastLoginTo = null;
        DateTimeOffset? createdFrom = null;
        DateTimeOffset? createdTo = null;

        foreach (var filter in descriptor.Filters)
        {
            if (filter.Operator is QueryOperator.In or QueryOperator.Between &&
                filter.Value is not IEnumerable<object?>)
            {
                throw new QueryValidationException(new QueryValidationError(
                    "PLATFORM_QUERY_INVALID",
                    "用户查询过滤值格式无效。",
                    filter.Field));
            }

            switch (filter.Field)
            {
                case "userNId": nId = Text(filter); break;
                case "loginName": loginName = Text(filter); break;
                case "name": name = Text(filter); break;
                case "email": email = Text(filter); break;
                case "phone": phone = Text(filter); break;
                case "status":
                    if (!Enum.TryParse<UserStatus>(Text(filter), true, out var parsedStatus))
                    {
                        throw new QueryValidationException(new QueryValidationError(
                            "PLATFORM_QUERY_INVALID", "用户状态过滤值无效。", filter.Field));
                    }
                    status = parsedStatus;
                    break;
                case "mustChangePassword":
                    if (!bool.TryParse(Text(filter), out var parsedBoolean))
                    {
                        throw new QueryValidationException(new QueryValidationError(
                            "PLATFORM_QUERY_INVALID", "用户改密过滤值无效。", filter.Field));
                    }
                    mustChangePassword = parsedBoolean;
                    break;
                case "lastLoginOn":
                    (lastLoginFrom, lastLoginTo) = DateRange(filter);
                    break;
                case "createdOn":
                    (createdFrom, createdTo) = DateRange(filter);
                    break;
                default:
                    throw new QueryValidationException(new QueryValidationError(
                        "PLATFORM_QUERY_FIELD_NOT_ALLOWED", "该用户查询字段不支持过滤。", filter.Field));
            }
        }

        var sort = descriptor.OrderBy.FirstOrDefault(sort => sort.Field != "userNId")
            ?? (descriptor.OrderBy.Count > 0 ? descriptor.OrderBy[0] : null)
            ?? new QuerySort("userNId", QuerySortDirection.Asc);
        var sortField = sort.Field switch
        {
            "userNId" => "nid",
            "loginName" => "loginname",
            "name" => "name",
            "status" => "status",
            "lastLoginOn" => "lastloginon",
            "createdOn" => "createdon",
            _ => "nid",
        };
        return new UserListFilter(
            tenantNId,
            nId,
            loginName,
            name,
            status,
            descriptor.PageIndex,
            descriptor.PageSize,
            IncludeDeleted: false,
            SortField: sortField,
            SortOrder: sort.Direction == QuerySortDirection.Desc ? "desc" : "asc",
            Email: email,
            Phone: phone,
            MustChangePassword: mustChangePassword,
            LastLoginFrom: lastLoginFrom,
            LastLoginTo: lastLoginTo,
            CreatedFrom: createdFrom,
            CreatedTo: createdTo);
    }

    private static string Text(QueryFilter filter)
        => filter.Value switch
        {
            string value => value,
            bool value => value.ToString(),
            long value => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            decimal value => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset value => value.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new QueryValidationException(new QueryValidationError(
                "PLATFORM_QUERY_INVALID", "用户查询过滤值必须是标量。", filter.Field)),
        };

    private static (DateTimeOffset? From, DateTimeOffset? To) DateRange(QueryFilter filter)
    {
        if (filter.Value is not IEnumerable<object?> values)
        {
            var value = DateTimeOffset.TryParse(Text(filter), out var parsed) ? parsed : (DateTimeOffset?)null;
            if (value is null) throw new QueryValidationException(new QueryValidationError("PLATFORM_QUERY_INVALID", "日期过滤值无效。", filter.Field));
            return (value, value);
        }
        var pair = values.Take(2).Select(value => DateTimeOffset.TryParse(
            Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            out var parsed) ? parsed : (DateTimeOffset?)null).ToArray();
        if (pair.Length != 2 || pair.Any(value => value is null))
        {
            throw new QueryValidationException(new QueryValidationError("PLATFORM_QUERY_INVALID", "日期范围过滤值无效。", filter.Field));
        }
        return (pair[0], pair[1]);
    }

    public IReadOnlyDictionary<string, object?> Project(IReadOnlyList<string> fields)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["userNId"] = UserNId,
            ["loginName"] = LoginName,
            ["name"] = Name,
            ["email"] = Email,
            ["phone"] = Phone,
            ["status"] = Status,
            ["tenantNId"] = TenantNId,
            ["createdOn"] = CreatedOn,
            ["lastLoginOn"] = LastLoginOn,
            ["mustChangePassword"] = MustChangePassword,
            ["directRoleNIds"] = DirectRoleNIds,
            ["groupRoleNIds"] = GroupRoleNIds,
            ["effectiveRoleNIds"] = EffectiveRoleNIds,
            ["effectiveRoleCount"] = EffectiveRoleCount,
            ["optimisticVersion"] = OptimisticVersion,
            ["concurrencyVersion"] = ConcurrencyVersion,
            ["isDeleted"] = IsDeleted,
        };
        return fields.ToDictionary(field => field, field => values[field], StringComparer.Ordinal);
    }
}
