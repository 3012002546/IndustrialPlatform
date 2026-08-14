namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// 操作审计动作常量(§19.2)。稳定字符串,写入 identity_operation_audit.action。
/// 新动作必须以常量扩展,不使用自由文本。
/// </summary>
public static class OperationAction
{
    /// <summary>创建用户。</summary>
    public const string UserCreate = "user.create";

    /// <summary>修改用户。</summary>
    public const string UserUpdate = "user.update";

    /// <summary>禁用用户。</summary>
    public const string UserDisable = "user.disable";

    /// <summary>启用用户。</summary>
    public const string UserEnable = "user.enable";

    /// <summary>管理员重置密码。</summary>
    public const string UserResetPassword = "user.reset_password";

    /// <summary>分配用户角色。</summary>
    public const string UserAssignRoles = "user.assign_roles";

    /// <summary>安全删除用户(墓碑)。</summary>
    public const string UserDelete = "user.delete";

    /// <summary>恢复用户墓碑。</summary>
    public const string UserRestore = "user.restore";

    /// <summary>创建角色。</summary>
    public const string RoleCreate = "role.create";

    /// <summary>修改角色。</summary>
    public const string RoleUpdate = "role.update";

    /// <summary>分配角色权限。</summary>
    public const string RoleAssignPermissions = "role.assign_permissions";

    /// <summary>创建用户组。</summary>
    public const string UserGroupCreate = "user_group.create";

    /// <summary>修改用户组资料。</summary>
    public const string UserGroupUpdate = "user_group.update";

    /// <summary>禁用用户组。</summary>
    public const string UserGroupDisable = "user_group.disable";

    /// <summary>启用用户组。</summary>
    public const string UserGroupEnable = "user_group.enable";

    /// <summary>设置用户组成员集。</summary>
    public const string UserGroupSetMembers = "user_group.set_members";

    /// <summary>设置用户组角色集。</summary>
    public const string UserGroupSetRoles = "user_group.set_roles";

    /// <summary>安全删除用户组(墓碑)。</summary>
    public const string UserGroupDelete = "user_group.delete";

    /// <summary>恢复用户组墓碑。</summary>
    public const string UserGroupRestore = "user_group.restore";

    /// <summary>创建企业登录源。</summary>
    public const string SsoProviderCreate = "sso.provider.create";

    /// <summary>修改企业登录源配置。</summary>
    public const string SsoProviderUpdate = "sso.provider.update";

    /// <summary>更新企业登录源密钥引用。</summary>
    public const string SsoProviderUpdateSecret = "sso.provider.update_secret";

    /// <summary>启用企业登录源。</summary>
    public const string SsoProviderEnable = "sso.provider.enable";

    /// <summary>停用企业登录源。</summary>
    public const string SsoProviderDisable = "sso.provider.disable";

    /// <summary>绑定外部账号。</summary>
    public const string SsoAccountBind = "sso.account.bind";

    /// <summary>解绑外部账号。</summary>
    public const string SsoAccountUnbind = "sso.account.unbind";

    /// <summary>创建平台 SSO Client。</summary>
    public const string SsoClientCreate = "sso.client.create";

    /// <summary>修改平台 SSO Client。</summary>
    public const string SsoClientUpdate = "sso.client.update";

    /// <summary>启用平台 SSO Client。</summary>
    public const string SsoClientEnable = "sso.client.enable";

    /// <summary>停用平台 SSO Client。</summary>
    public const string SsoClientDisable = "sso.client.disable";

    /// <summary>登记 Client 端点。</summary>
    public const string SsoClientEndpointAdd = "sso.client.endpoint.add";

    /// <summary>启用/停用 Client 端点。</summary>
    public const string SsoClientEndpointUpdate = "sso.client.endpoint.update";

    /// <summary>移除 Client 端点。</summary>
    public const string SsoClientEndpointRemove = "sso.client.endpoint.remove";
}

/// <summary>操作审计对象类型常量(§19.2),写入 identity_operation_audit.object_type。</summary>
public static class OperationObjectType
{
    /// <summary>用户对象。</summary>
    public const string User = "user";

    /// <summary>角色对象。</summary>
    public const string Role = "role";

    /// <summary>用户组对象。</summary>
    public const string UserGroup = "user_group";

    /// <summary>企业登录源对象。</summary>
    public const string SsoProvider = "sso.provider";

    /// <summary>外部账号对象。</summary>
    public const string SsoAccount = "sso.account";

    /// <summary>平台 SSO Client 对象。</summary>
    public const string SsoClient = "sso.client";
}

/// <summary>
/// 操作审计条目(§19.2)。记录执行者、目标对象、动作与前后值摘要;
/// 密码、Token、内部哈希及敏感请求体必须排除,绝不进入审计。
/// </summary>
public sealed record OperationAuditEntry(
    string TenantNId,
    string ActorUserNId,
    string Action,
    string ObjectType,
    string ObjectNId,
    string? BeforeSummary,
    string? AfterSummary,
    string? TraceId,
    DateTimeOffset OccurredOn);

/// <summary>操作审计持久化端口(只追加,§19.2)。</summary>
public interface IOperationAuditSink
{
    Task WriteAsync(OperationAuditEntry entry, CancellationToken cancellationToken);
}
