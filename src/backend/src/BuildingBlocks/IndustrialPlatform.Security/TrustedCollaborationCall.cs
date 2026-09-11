namespace IndustrialPlatform.Security;

/// <summary>已验证服务断言形成的不可由浏览器构造的调用上下文。</summary>
public sealed record TrustedCollaborationCall(
    string TenantNId,
    string ActorUserNId,
    string ActorSessionNId,
    string ActorSecurityVersion,
    string Action,
    string RequestNId,
    string Issuer = "",
    string Jti = "",
    DateTime? ExpiresAt = null)
{
    public DateTimeOffset ExpiresOn => ExpiresAt.HasValue
        ? new DateTimeOffset(DateTime.SpecifyKind(ExpiresAt.Value, DateTimeKind.Utc))
        : DateTimeOffset.UtcNow.AddSeconds(30);
}
