namespace IndustrialPlatform.Querying.Schema;

public enum QueryValueType
{
    Text,
    Number,
    Boolean,
    Date,
}

public sealed record QueryFieldDefinition(
    QueryValueType ValueType,
    bool Selectable = true,
    bool Filterable = true,
    bool Sortable = true)
{
    public static QueryFieldDefinition Text(
        bool selectable = true,
        bool filterable = true,
        bool sortable = true)
        => new(QueryValueType.Text, selectable, filterable, sortable);

    public static QueryFieldDefinition Number(
        bool selectable = true,
        bool filterable = true,
        bool sortable = true)
        => new(QueryValueType.Number, selectable, filterable, sortable);

    public static QueryFieldDefinition Boolean(
        bool selectable = true,
        bool filterable = true,
        bool sortable = true)
        => new(QueryValueType.Boolean, selectable, filterable, sortable);

    public static QueryFieldDefinition Date(
        bool selectable = true,
        bool filterable = true,
        bool sortable = true)
        => new(QueryValueType.Date, selectable, filterable, sortable);
}
