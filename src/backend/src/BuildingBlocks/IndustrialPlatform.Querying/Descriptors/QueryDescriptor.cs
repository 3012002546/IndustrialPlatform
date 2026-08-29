namespace IndustrialPlatform.Querying.Descriptors;

/// <summary>
/// 平台内部唯一的分页查询语义。协议适配器应在进入应用层前转换为此模型。
/// </summary>
public sealed record QueryDescriptor
{
    public QueryDescriptor(
        IReadOnlyList<QueryFilter> filters,
        IReadOnlyList<QuerySort> orderBy,
        IReadOnlyList<string> select,
        int PageIndex,
        int PageSize,
        string? Search = null,
        bool IncludeCount = false)
    {
        Filters = filters.ToArray();
        OrderBy = orderBy.ToArray();
        Select = select.ToArray();
        this.PageIndex = PageIndex;
        this.PageSize = PageSize;
        this.Search = Search;
        this.IncludeCount = IncludeCount;
    }

    public IReadOnlyList<QueryFilter> Filters { get; init; }

    public IReadOnlyList<QuerySort> OrderBy { get; init; }

    public IReadOnlyList<string> Select { get; init; }

    public int PageIndex { get; init; }

    public int PageSize { get; init; }

    public string? Search { get; init; }

    public bool IncludeCount { get; init; }
}
