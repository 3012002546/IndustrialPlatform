namespace IndustrialPlatform.Web.Results;

/// <summary>
/// 分页结果。通过 <see cref="PageResult"/> 的静态工厂方法创建。
/// </summary>
/// <typeparam name="T">列表元素类型。</typeparam>
public class PageResult<T>
{
    /// <summary>当前页数据。</summary>
    public List<T> Items { get; set; } = [];

    /// <summary>总记录数。</summary>
    public long Total { get; set; }

    /// <summary>当前页码,从 1 开始。</summary>
    public int PageIndex { get; set; }

    /// <summary>每页条数。</summary>
    public int PageSize { get; set; }
}
