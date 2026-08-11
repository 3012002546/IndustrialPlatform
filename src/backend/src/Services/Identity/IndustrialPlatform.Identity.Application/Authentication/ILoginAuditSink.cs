namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>
/// 登录审计条目(§19.1)。IP/User-Agent 以原始值传递,由实现内部哈希;
/// 绝不允许出现密码、Token 或内部哈希。
/// </summary>
public sealed record LoginAuditEntry(
    string TenantNId,
    string? UserNId,
    string LoginNameSnapshot,
    bool Success,
    string? FailureCode,
    string? IpAddress,
    string? UserAgent,
    string? TraceId,
    DateTimeOffset OccurredOn);

/// <summary>登录审计持久化端口(只追加,§19)。</summary>
public interface ILoginAuditSink
{
    Task WriteAsync(LoginAuditEntry entry, CancellationToken cancellationToken);
}
