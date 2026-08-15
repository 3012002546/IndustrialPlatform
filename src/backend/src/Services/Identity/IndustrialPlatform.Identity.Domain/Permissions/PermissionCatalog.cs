namespace IndustrialPlatform.Identity.Domain.Permissions;

/// <summary>
/// 系统权限目录(§9.2)。提供第一批业务权限的 NId 常量,供种子数据创建。
/// </summary>
public static class PermissionCatalog
{
    /// <summary>查看用户。</summary>
    public const string UserView = "identity.user.view";

    /// <summary>创建用户。</summary>
    public const string UserCreate = "identity.user.create";

    /// <summary>更新用户。</summary>
    public const string UserUpdate = "identity.user.update";

    /// <summary>用户状态管理。</summary>
    public const string UserStatus = "identity.user.status";

    /// <summary>分配角色。</summary>
    public const string UserAssignRole = "identity.user.assign-role";

    /// <summary>安全删除用户(§29A.5)。</summary>
    public const string UserDelete = "identity.user.delete";

    /// <summary>恢复用户墓碑(§29A.5)。</summary>
    public const string UserRestore = "identity.user.restore";

    /// <summary>查看角色。</summary>
    public const string RoleView = "identity.role.view";

    /// <summary>创建角色。</summary>
    public const string RoleCreate = "identity.role.create";

    /// <summary>更新角色。</summary>
    public const string RoleUpdate = "identity.role.update";

    /// <summary>分配权限。</summary>
    public const string RoleAssignPermission = "identity.role.assign-permission";

    /// <summary>安全删除用户组(§29A.5)。</summary>
    public const string UserGroupDelete = "identity.user-group.delete";

    /// <summary>恢复用户组墓碑(§29A.5)。</summary>
    public const string UserGroupRestore = "identity.user-group.restore";

    /// <summary>查看用户组(§29A.5)。</summary>
    public const string UserGroupView = "identity.user-group.view";

    /// <summary>创建用户组(§29A.5)。</summary>
    public const string UserGroupCreate = "identity.user-group.create";

    /// <summary>更新用户组资料(§29A.5)。</summary>
    public const string UserGroupUpdate = "identity.user-group.update";

    /// <summary>启用/禁用用户组(§29A.5)。</summary>
    public const string UserGroupStatus = "identity.user-group.status";

    /// <summary>设置用户组成员(§29A.5)。</summary>
    public const string UserGroupAssignMember = "identity.user-group.assign-member";

    /// <summary>设置用户组角色(§29A.5)。</summary>
    public const string UserGroupAssignRole = "identity.user-group.assign-role";

    /// <summary>管理员重置密码(§29A.5)。</summary>
    public const string UserResetPassword = "identity.user.reset-password";

    /// <summary>查看权限。</summary>
    public const string PermissionView = "identity.permission.view";

    /// <summary>查看登录审计。</summary>
    public const string AuditLoginView = "identity.audit.login.view";

    /// <summary>查看 SSO。</summary>
    public const string SsoView = "identity.sso.view";

    /// <summary>管理 SSO。</summary>
    public const string SsoManage = "identity.sso.manage";

    /// <summary>测试 SSO。</summary>
    public const string SsoTest = "identity.sso.test";

    /// <summary>平台首页查看。</summary>
    public const string PlatformHomeView = "platform.home.view";

    /// <summary>平台 PDA 查看。</summary>
    public const string PlatformPdaView = "platform.pda.view";

    /// <summary>平台移动端查看。</summary>
    public const string PlatformMobileView = "platform.mobile.view";

    /// <summary>查看 bootstrap 状态(§29A.5):仅状态/版本,不含 Secret。</summary>
    public const string BootstrapView = "identity.bootstrap.view";

    /// <summary>紧急恢复内置 admin(§29A.5):接受一次性恢复引用与审批关联,不接受明文密码。</summary>
    public const string BootstrapRecover = "identity.bootstrap.recover";

    /// <summary>第一批权限 NId,按 §9.2 目录顺序。</summary>
    public static IReadOnlyList<string> FirstBatchNIds { get; } =
    [
        UserView,
        UserCreate,
        UserUpdate,
        UserStatus,
        UserAssignRole,
        UserDelete,
        UserRestore,
        RoleView,
        RoleCreate,
        RoleUpdate,
        RoleAssignPermission,
        UserGroupDelete,
        UserGroupRestore,
        UserGroupView,
        UserGroupCreate,
        UserGroupUpdate,
        UserGroupStatus,
        UserGroupAssignMember,
        UserGroupAssignRole,
        UserResetPassword,
        PermissionView,
        AuditLoginView,
        SsoView,
        SsoManage,
        SsoTest,
        PlatformHomeView,
        PlatformPdaView,
        PlatformMobileView,
        BootstrapView,
        BootstrapRecover,
    ];
}
