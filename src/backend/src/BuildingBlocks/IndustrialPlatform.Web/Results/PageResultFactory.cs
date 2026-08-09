namespace IndustrialPlatform.Web.Results;

/// <summary>
/// <see cref="PageResult{T}"/> 的非泛型工厂,避免在泛型类型上放置静态成员(CA1000)。
/// </summary>
public static class PageResult
{
    /// <summary>
    /// 从数据源创建分页结果。
    /// </summary>
    /// <param name="items">当前页数据。</param>
    /// <param name="total">总记录数。</param>
    /// <param name="pageIndex">当前页码,从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <typeparam name="T">列表元素类型。</typeparam>
    /// <returns>分页结果。</returns>
    public static PageResult<T> Create<T>(IEnumerable<T> items, long total, int pageIndex, int pageSize)
        => new()
        {
            Items = items.ToList(),
            Total = total,
            PageIndex = pageIndex,
            PageSize = pageSize,
        };
}
