namespace IndustrialPlatform.Identity.Application.Authorization;

/// <summary>集中定义初始化/数据库编排权限的受保护输出策略。</summary>
public static class AuthorizationPermissionPolicy
{
    /// <summary>普通角色可以被分配,但不能通过登录/SSO 权限列表获得的权限前缀。</summary>
    public static bool IsProtectedInitializationPermission(string permissionNId) =>
        permissionNId.StartsWith("systemdata.service-initialization.", StringComparison.OrdinalIgnoreCase)
        || permissionNId.StartsWith("systemdata.database-orchestration.", StringComparison.OrdinalIgnoreCase);
}
