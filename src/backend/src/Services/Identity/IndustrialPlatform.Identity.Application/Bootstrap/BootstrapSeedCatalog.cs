using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Identity.Domain.Permissions;

namespace IndustrialPlatform.Identity.Application.Bootstrap;

/// <summary>
/// Identity 三层种子目录(§29A.4):SystemCatalogSeed / TenantSecuritySeed / BootstrapAdminSeed。
/// 种子键、版本、类别、bootstrap 稳定标识与内容源集中定义;
/// 内容变更必须同步递增 <see cref="SeedVersion"/>,否则同版本不同 checksum 会被账本判定为 drift 并拒绝。
/// </summary>
public static class BootstrapSeedCatalog
{
    /// <summary>SystemCatalogSeed 种子键(权限目录 + SYSTEM_ADMIN 角色)。</summary>
    public const string SystemCatalogSeedKey = "identity.system-catalog";

    /// <summary>TenantSecuritySeed 种子键(按租户确认系统角色)。</summary>
    public const string TenantSecuritySeedKey = "identity.tenant-security";

    /// <summary>BootstrapAdminSeed 种子键(内置 admin 引导,SecretBootstrap)。</summary>
    public const string BootstrapAdminSeedKey = "identity.bootstrap-admin";

    /// <summary>全部种子当前版本;目录内容变化必须递增。</summary>
    public const string SeedVersion = "1.0.0";

    /// <summary>系统作用域。</summary>
    public const string SystemScope = "system";

    /// <summary>租户作用域。</summary>
    public const string TenantScope = "tenant";

    /// <summary>稳定 SYSTEM_ADMIN 系统角色标识。</summary>
    public const string SystemAdminRoleNId = "SYSTEM_ADMIN";

    /// <summary>内置 bootstrap admin 稳定业务标识(§29A.4:UserNId=ADMIN)。</summary>
    public const string BootstrapUserNId = "ADMIN";

    /// <summary>内置 bootstrap admin 登录名。</summary>
    public const string BootstrapLoginName = "admin";

    /// <summary>内置 bootstrap admin 稳定内部 Id(§29A.4:稳定内部 Id,跨环境一致)。</summary>
    public static readonly Guid BootstrapAdminStableId = Guid.Parse("00000000-0000-0000-0000-0000000000AD");

    /// <summary>内置 admin 随机临时密码最短长度(§29A.4:不少于 20 个字符)。</summary>
    public const int BootstrapPasswordMinLength = 20;

    /// <summary>权限目录条目。</summary>
    public sealed record PermissionEntry(string NId, string Name, PermissionType Type);

    /// <summary>系统角色定义。</summary>
    public sealed record SystemRoleEntry(string NId, string Name, string Description);

    /// <summary>系统目录权限条目(§9.2 第一批 + §29A.5 bootstrap 权限)。</summary>
    public static IReadOnlyList<PermissionEntry> Permissions { get; } =
    [
        new(PermissionCatalog.UserView, "查看用户", PermissionType.Page),
        new(PermissionCatalog.UserCreate, "创建用户", PermissionType.Action),
        new(PermissionCatalog.UserUpdate, "更新用户", PermissionType.Action),
        new(PermissionCatalog.UserStatus, "用户状态管理", PermissionType.Action),
        new(PermissionCatalog.UserAssignRole, "分配角色", PermissionType.Action),
        new(PermissionCatalog.UserDelete, "安全删除用户", PermissionType.Action),
        new(PermissionCatalog.UserRestore, "恢复用户墓碑", PermissionType.Action),
        new(PermissionCatalog.RoleView, "查看角色", PermissionType.Page),
        new(PermissionCatalog.RoleCreate, "创建角色", PermissionType.Action),
        new(PermissionCatalog.RoleUpdate, "更新角色", PermissionType.Action),
        new(PermissionCatalog.RoleAssignPermission, "分配权限", PermissionType.Action),
        new(PermissionCatalog.UserGroupDelete, "安全删除用户组", PermissionType.Action),
        new(PermissionCatalog.UserGroupRestore, "恢复用户组墓碑", PermissionType.Action),
        new(PermissionCatalog.PermissionView, "查看权限", PermissionType.Page),
        new(PermissionCatalog.AuditLoginView, "查看登录审计", PermissionType.Page),
        new(PermissionCatalog.SsoView, "查看 SSO", PermissionType.Page),
        new(PermissionCatalog.SsoManage, "管理 SSO", PermissionType.Action),
        new(PermissionCatalog.SsoTest, "测试 SSO", PermissionType.Action),
        new(PermissionCatalog.PlatformHomeView, "平台首页", PermissionType.Page),
        new(PermissionCatalog.PlatformPdaView, "PDA 端页面", PermissionType.Page),
        new(PermissionCatalog.PlatformMobileView, "移动端页面", PermissionType.Page),
        new(PermissionCatalog.BootstrapView, "查看 bootstrap 状态", PermissionType.Page),
        new(PermissionCatalog.BootstrapRecover, "紧急恢复内置 admin", PermissionType.Action),
    ];

    /// <summary>系统角色定义(SYSTEM_ADMIN 拥有全部目录权限)。</summary>
    public static SystemRoleEntry SystemAdminRole { get; } = new(
        SystemAdminRoleNId,
        "系统管理员",
        "内置系统管理员角色,拥有全部系统目录权限。");

    /// <summary>计算种子内容校验和(SHA-256 十六进制)。内容源变化必须同步递增 SeedVersion。</summary>
    public static string Checksum(string canonicalContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalContent);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalContent))).ToLowerInvariant();
    }

    /// <summary>SystemCatalogSeed 规范化内容(权限目录 + 系统角色 + 绑定)。</summary>
    public static string SystemCatalogContent() =>
        string.Join("\n",
            Permissions.Select(p => $"{p.NId}|{p.Name}|{p.Type}"))
        + "\nROLE|" + SystemAdminRole.NId + "|" + SystemAdminRole.Name + "|" + SystemAdminRole.Description;

    /// <summary>TenantSecuritySeed 规范化内容(按租户的系统角色确认,与目录版本绑定)。</summary>
    public static string TenantSecurityContent(string tenantNId) =>
        $"TENANT|{tenantNId}\n" + SystemCatalogContent();

    /// <summary>BootstrapAdminSeed 规范化内容(稳定标识 + 生成规则,不含任何密码)。</summary>
    public static string BootstrapAdminContent() =>
        $"USER|{BootstrapUserNId}|{BootstrapLoginName}\nROLE|{SystemAdminRoleNId}\nMINLEN|{BootstrapPasswordMinLength}";
}
