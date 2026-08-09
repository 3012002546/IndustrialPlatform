namespace IndustrialPlatform.SharedKernel.Results;

/// <summary>
/// 统一返回模型(携带数据负载)。通过 <see cref="Result"/> 的静态工厂方法创建。
/// </summary>
/// <typeparam name="T">业务数据类型。</typeparam>
public class Result<T>
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>失败时的错误信息,或成功时的补充说明。</summary>
    public string? Message { get; set; }

    /// <summary>业务数据,失败时通常为 null。</summary>
    public T? Data { get; set; }
}
