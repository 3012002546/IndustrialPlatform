namespace IndustrialPlatform.Identity.Contracts.Management;

/// <summary>
/// 登录审计条目(§19.1/§25.4 GET /audits/logins)。只追加、只读。
/// IP/User-Agent 仅提供 SHA-256 哈希(作为来源摘要),不暴露完整 IP 或原始 User-Agent;
/// 页面不得展示密码、Token 或敏感请求体。
/// </summary>
public sealed record LoginAuditItem(
    string TenantNId,
    string? UserNId,
    string LoginNameSnapshot,
    bool Success,
    string? FailureCode,
    string IpAddressHash,
    string UserAgentHash,
    string TraceId,
    DateTimeOffset OccurredOn);
