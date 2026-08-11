namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>
/// Access Token 载荷描述(§12 Claims,不含完整 permissions、不含数据库 Guid)。
/// </summary>
public sealed record AccessTokenDescriptor(
    string Subject,
    string UserName,
    string TenantNId,
    IReadOnlyList<string> Roles,
    string SessionId,
    int AuthVersion,
    DateTimeOffset IssuedAt);

/// <summary>签发结果:JWT 串与过期时间(响应契约 <c>expiresAt</c> 使用同一值)。</summary>
public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);

/// <summary>RS256 Access Token 签发端口(实现持有 RSA 私钥与 kid)。</summary>
public interface IAccessTokenFactory
{
    AccessTokenResult Create(AccessTokenDescriptor descriptor);
}
