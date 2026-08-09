namespace IndustrialPlatform.Security;

/// <summary>
/// 当前用户上下文,从 HttpContext 中读取认证用户的声明信息。
/// </summary>
public interface ICurrentUser
{
    /// <summary>当前请求是否已认证。</summary>
    bool IsAuthenticated { get; }

    /// <summary>当前用户标识,未认证时为 null。</summary>
    Guid? UserId { get; }

    /// <summary>当前用户名。</summary>
    string? UserName { get; }

    /// <summary>当前租户标识。</summary>
    string? TenantId { get; }

    /// <summary>当前用户角色集合。</summary>
    IReadOnlyCollection<string> Roles { get; }
}
