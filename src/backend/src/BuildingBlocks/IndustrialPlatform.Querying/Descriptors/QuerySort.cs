namespace IndustrialPlatform.Querying.Descriptors;

public enum QuerySortDirection
{
    Asc,
    Desc,
}

public sealed record QuerySort(string Field, QuerySortDirection Direction);
