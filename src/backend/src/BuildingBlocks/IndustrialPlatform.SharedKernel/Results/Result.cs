namespace IndustrialPlatform.SharedKernel.Results;

/// <summary>
/// 统一返回模型(无数据负载),并提供泛型返回模型的工厂方法。
/// </summary>
public class Result
{
    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>失败时的错误信息,或成功时的补充说明。</summary>
    public string? Message { get; set; }

    public static Result Ok()
        => new() { Success = true };

    public static Result Ok(string message)
        => new() { Success = true, Message = message };

    public static Result Fail(string message)
        => new() { Success = false, Message = message };

    public static Result<T> Ok<T>(T data)
        => new() { Success = true, Data = data };

    public static Result<T> Ok<T>(T data, string message)
        => new() { Success = true, Message = message, Data = data };

    public static Result<T> Fail<T>(string message)
        => new() { Success = false, Message = message };
}
