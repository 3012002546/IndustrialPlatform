using System.Text.Json.Serialization;

namespace IndustrialPlatform.Web.Results;

/// <summary>
/// API 统一返回模型(携带数据负载)。通过 <see cref="ApiResult"/> 的静态工厂方法创建。
/// </summary>
/// <typeparam name="T">业务数据类型。</typeparam>
public class ApiResult<T>
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>业务状态码,如 "200"、"404"、"MAT001"。</summary>
    public string Code { get; set; } = "200";

    /// <summary>提示信息。</summary>
    public string Message { get; set; } = "success";

    /// <summary>数据负载,失败时通常为 null。</summary>
    public T? Data { get; set; }

    /// <summary>可选的本地化参数或诊断参数。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Parameters { get; set; }

    /// <summary>可选的分布式追踪标识。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; set; }
}
