using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace IndustrialPlatform.Security;

/// <summary>
/// 当前用户上下文默认实现,从 HttpContext 的 ClaimsPrincipal 读取声明。
/// </summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// 初始化 <see cref="CurrentUser"/>。
    /// </summary>
    /// <param name="httpContextAccessor">HTTP 上下文访问器。</param>
    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    /// <inheritdoc />
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public string? UserNId => User?.FindFirst(ClaimConstants.UserNId)?.Value;

    /// <inheritdoc />
    public string? UserName => User?.FindFirst(ClaimConstants.UserName)?.Value;

    /// <inheritdoc />
    public string? TenantId => User?.FindFirst(ClaimConstants.TenantId)?.Value;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles =>
        User?.FindAll(ClaimConstants.Role).Select(c => c.Value).ToList() ?? [];
}
