namespace IndustrialPlatform.Web.Results;

/// <summary>
/// API 统一返回模型(无数据负载),并提供泛型返回模型的工厂方法。
/// 与 <see cref="Result{T}"/> 的工厂方法分布保持一致(CA1000)。
/// </summary>
public class ApiResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>业务状态码,如 "200"、"404"、"MAT001"。</summary>
    public string Code { get; set; } = "200";

    /// <summary>提示信息。</summary>
    public string Message { get; set; } = "success";

    /// <summary>数据负载,无数据时为空。</summary>
    public object? Data { get; set; }

    public static ApiResult Ok()
        => new() { Success = true };

    public static ApiResult Ok(string message)
        => new() { Success = true, Message = message };

    public static ApiResult Fail(string code, string message)
        => new() { Success = false, Code = code, Message = message };

    public static ApiResult<T> Ok<T>(T data)
        => new() { Success = true, Data = data };

    public static ApiResult<T> Ok<T>(T data, string message)
        => new() { Success = true, Message = message, Data = data };

    public static ApiResult<T> Fail<T>(string code, string message)
        => new() { Success = false, Code = code, Message = message };
}
