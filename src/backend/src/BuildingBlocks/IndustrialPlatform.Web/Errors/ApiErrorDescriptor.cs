namespace IndustrialPlatform.Web.Errors;

/// <summary>
/// 稳定 API 错误描述。Code 用于客户端本地化，Message 仅作兼容 fallback，Parameters 携带模板参数。
/// </summary>
public sealed record ApiErrorDescriptor(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    string? TraceId = null);
