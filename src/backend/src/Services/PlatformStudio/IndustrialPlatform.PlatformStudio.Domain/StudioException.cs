using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>Studio边界内可向调用者显示的错误；不包含连接、凭据或源数据正文。</summary>
public sealed class StudioException(string code, string message, int statusCode = 400, string? path = null) : DomainException(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public string? Path { get; } = path;
}

/// <summary>定位到配置字段的校验结果。Error阻止发布，结构有效的草稿仍可保存。</summary>
public sealed record StudioDiagnostic(string Code, string Path, string Message, string Severity = "Error");
