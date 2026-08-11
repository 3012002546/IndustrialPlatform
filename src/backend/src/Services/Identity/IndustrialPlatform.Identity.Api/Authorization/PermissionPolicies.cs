using IndustrialPlatform.Identity.Domain.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace IndustrialPlatform.Identity.Api.Authorization;

/// <summary>
/// 服务端权限策略(§18):每个目录权限注册一条策略,策略名
/// <c>permission:{permissionNId}</c>。控制器用策略名常量声明
/// <c>[Authorize(Policy = PermissionPolicies.UserView)]</c>。
/// </summary>
public static class PermissionPolicies
{
    /// <summary>策略名前缀。</summary>
    public const string Prefix = "permission:";

    /// <summary>查看用户。</summary>
    public const string UserView = Prefix + PermissionCatalog.UserView;

    /// <summary>创建用户。</summary>
    public const string UserCreate = Prefix + PermissionCatalog.UserCreate;

    /// <summary>更新用户。</summary>
    public const string UserUpdate = Prefix + PermissionCatalog.UserUpdate;

    /// <summary>用户状态管理。</summary>
    public const string UserStatus = Prefix + PermissionCatalog.UserStatus;

    /// <summary>分配角色。</summary>
    public const string UserAssignRole = Prefix + PermissionCatalog.UserAssignRole;

    /// <summary>查看角色。</summary>
    public const string RoleView = Prefix + PermissionCatalog.RoleView;

    /// <summary>创建角色。</summary>
    public const string RoleCreate = Prefix + PermissionCatalog.RoleCreate;

    /// <summary>更新角色。</summary>
    public const string RoleUpdate = Prefix + PermissionCatalog.RoleUpdate;

    /// <summary>分配权限。</summary>
    public const string RoleAssignPermission = Prefix + PermissionCatalog.RoleAssignPermission;

    /// <summary>查看权限。</summary>
    public const string PermissionView = Prefix + PermissionCatalog.PermissionView;

    /// <summary>查看登录审计。</summary>
    public const string AuditLoginView = Prefix + PermissionCatalog.AuditLoginView;

    /// <summary>查看 SSO。</summary>
    public const string SsoView = Prefix + PermissionCatalog.SsoView;

    /// <summary>管理 SSO。</summary>
    public const string SsoManage = Prefix + PermissionCatalog.SsoManage;

    /// <summary>测试 SSO。</summary>
    public const string SsoTest = Prefix + PermissionCatalog.SsoTest;

    /// <summary>平台首页查看。</summary>
    public const string PlatformHomeView = Prefix + PermissionCatalog.PlatformHomeView;

    /// <summary>平台 PDA 查看。</summary>
    public const string PlatformPdaView = Prefix + PermissionCatalog.PlatformPdaView;

    /// <summary>平台移动端查看。</summary>
    public const string PlatformMobileView = Prefix + PermissionCatalog.PlatformMobileView;

    /// <summary>由权限 NId 生成策略名。</summary>
    public static string PolicyName(string permissionNId) => Prefix + permissionNId;

    /// <summary>为目录中全部权限注册策略(§9.2 第一批)。</summary>
    public static void AddPermissionPolicies(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var permissionNId in PermissionCatalog.FirstBatchNIds)
        {
            options.AddPolicy(PolicyName(permissionNId), policy =>
                policy.Requirements.Add(new PermissionRequirement(permissionNId)));
        }
    }
}
