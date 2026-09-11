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
    public const string SeedVersion = "1.5.0";

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
        new(PermissionCatalog.UserGroupView, "查看用户组", PermissionType.Page),
        new(PermissionCatalog.UserGroupCreate, "创建用户组", PermissionType.Action),
        new(PermissionCatalog.UserGroupUpdate, "更新用户组", PermissionType.Action),
        new(PermissionCatalog.UserGroupStatus, "启用/禁用用户组", PermissionType.Action),
        new(PermissionCatalog.UserGroupAssignMember, "设置用户组成员", PermissionType.Action),
        new(PermissionCatalog.UserGroupAssignRole, "设置用户组角色", PermissionType.Action),
        new(PermissionCatalog.UserResetPassword, "管理员重置密码", PermissionType.Action),
        new(PermissionCatalog.PermissionView, "查看权限", PermissionType.Page),
        new(PermissionCatalog.AuditLoginView, "查看登录审计", PermissionType.Page),
        new(PermissionCatalog.SsoView, "查看 SSO", PermissionType.Page),
        new(PermissionCatalog.SsoManage, "管理 SSO", PermissionType.Action),
        new(PermissionCatalog.SsoTest, "测试 SSO", PermissionType.Action),
        new(PermissionCatalog.SessionView, "查看有效登录会话", PermissionType.Page),
        new(PermissionCatalog.SessionRevoke, "强制退出登录会话", PermissionType.Action),
        new(PermissionCatalog.PlatformHomeView, "平台首页", PermissionType.Page),
        new(PermissionCatalog.PlatformOperationView, "生产操作模式", PermissionType.Page),
        new(PermissionCatalog.PlatformPdaView, "PDA 端页面", PermissionType.Page),
        new(PermissionCatalog.PlatformMobileView, "移动端页面", PermissionType.Page),
        new(PermissionCatalog.BootstrapView, "查看 bootstrap 状态", PermissionType.Page),
        new(PermissionCatalog.BootstrapRecover, "紧急恢复内置 admin", PermissionType.Action),
        new(PermissionCatalog.CollaborationMessagingRead, "查看协作消息", PermissionType.Page),
        new(PermissionCatalog.CollaborationMessagingConversationStart, "发起协作会话", PermissionType.Action),
        new(PermissionCatalog.CollaborationMessagingWrite, "发送协作消息", PermissionType.Action),
        new(PermissionCatalog.CollaborationMessagingReadCursorUpdate, "更新协作阅读游标", PermissionType.Action),
        new(PermissionCatalog.CollaborationMessagingConversationHide, "隐藏协作会话", PermissionType.Action),
        new(PermissionCatalog.CollaborationMessagingConversationRestore, "恢复协作会话", PermissionType.Action),
        new(PermissionCatalog.CollaborationMessagingRetract, "撤回协作消息", PermissionType.Action),
        new(PermissionCatalog.CollaborationMessagingAttachmentSend, "发送协作附件", PermissionType.Action),
        new(PermissionCatalog.CollaborationMessagingAttachmentDownload, "下载协作附件", PermissionType.Action),
        new(PermissionCatalog.CollaborationComplianceRead, "查看协作合规数据", PermissionType.Page),
        new(PermissionCatalog.CollaborationComplianceView, "受控查看协作原文", PermissionType.Action),
        new(PermissionCatalog.CollaborationComplianceReadOriginal, "查看协作原文", PermissionType.Action),
        new(PermissionCatalog.CollaborationComplianceDispose, "处置协作消息", PermissionType.Action),
        new(PermissionCatalog.CollaborationComplianceExportRequest, "申请协作合规导出", PermissionType.Action),
        new(PermissionCatalog.CollaborationComplianceExportDownload, "下载协作合规导出", PermissionType.Action),
        new(PermissionCatalog.CollaborationComplianceExportApprove, "审批协作合规导出", PermissionType.Action),
        new(PermissionCatalog.CollaborationComplianceLegalHoldCreate, "创建协作法律保全", PermissionType.Action),
        new(PermissionCatalog.CollaborationComplianceLegalHoldReview, "审核协作法律保全", PermissionType.Action),
        new(PermissionCatalog.CollaborationComplianceLegalHoldRelease, "申请释放协作法律保全", PermissionType.Action),
        new(PermissionCatalog.CollaborationComplianceLegalHoldReleaseApprove, "审批释放协作法律保全", PermissionType.Action),
        new(PermissionCatalog.CollaborationComplianceRetentionManage, "管理协作保留策略", PermissionType.Action),
        new(PermissionCatalog.CollaborationComplianceRetentionUpdate, "更新协作保留策略", PermissionType.Action),
        new(PermissionCatalog.CollaborationPresenceConnect, "建立协作在线连接", PermissionType.Action),
        new(PermissionCatalog.CollaborationPresenceRead, "查看协作在线状态", PermissionType.Action),
        new(PermissionCatalog.CollaborationPresenceWrite, "更新协作在线状态", PermissionType.Action),
        new(PermissionCatalog.SystemDataOrganizationView, "查看行政组织", PermissionType.Page),
        new(PermissionCatalog.SystemDataOrganizationCreate, "创建行政组织", PermissionType.Action),
        new(PermissionCatalog.SystemDataOrganizationUpdate, "更新行政组织", PermissionType.Action),
        new(PermissionCatalog.SystemDataOrganizationMove, "移动行政组织", PermissionType.Action),
        new(PermissionCatalog.SystemDataOrganizationStatus, "管理行政组织状态", PermissionType.Action),
        new(PermissionCatalog.SystemDataPositionView, "查看岗位", PermissionType.Page),
        new(PermissionCatalog.SystemDataPositionCreate, "创建岗位", PermissionType.Action),
        new(PermissionCatalog.SystemDataPositionUpdate, "更新岗位", PermissionType.Action),
        new(PermissionCatalog.SystemDataPositionStatus, "管理岗位状态", PermissionType.Action),
        new(PermissionCatalog.SystemDataAssignmentView, "查看用户任职", PermissionType.Page),
        new(PermissionCatalog.SystemDataAssignmentManage, "管理用户任职", PermissionType.Action),
        new(PermissionCatalog.SystemDataResourceView, "查看界面资源", PermissionType.Page),
        new(PermissionCatalog.SystemDataNavigationView, "查看导航", PermissionType.Page),
        new(PermissionCatalog.SystemDataNavigationManage, "管理导航", PermissionType.Action),
        new(PermissionCatalog.SystemDataNavigationPublish, "发布导航", PermissionType.Action),
        new(PermissionCatalog.SystemDataNavigationRollback, "回滚导航", PermissionType.Action),
        new(PermissionCatalog.SystemDataFeatureView, "查看功能开关", PermissionType.Page),
        new(PermissionCatalog.SystemDataFeatureManage, "管理功能开关", PermissionType.Action),
        new(PermissionCatalog.SystemDataServiceCatalogView, "查看服务目录", PermissionType.Page),
        new(PermissionCatalog.SystemDataServiceCatalogManage, "管理服务目录", PermissionType.Action),
        new(PermissionCatalog.SystemDataThemePolicyView, "查看主题策略", PermissionType.Page),
        new(PermissionCatalog.SystemDataThemePolicyManage, "管理主题策略", PermissionType.Action),
        new(PermissionCatalog.SystemDataDatabaseOrchestrationView, "查看数据库编排", PermissionType.Page),
        new(PermissionCatalog.SystemDataDatabaseOrchestrationRegister, "注册数据库编排", PermissionType.Action),
        new(PermissionCatalog.SystemDataDatabaseOrchestrationPlan, "生成数据库编排计划", PermissionType.Action),
        new(PermissionCatalog.SystemDataDatabaseOrchestrationApply, "执行数据库编排", PermissionType.Action),
        new(PermissionCatalog.SystemDataDatabaseOrchestrationApprove, "审批数据库编排", PermissionType.Action),
        new(PermissionCatalog.SystemDataDatabaseOrchestrationBackup, "确认数据库备份", PermissionType.Action),
        new(PermissionCatalog.SystemDataDatabaseOrchestrationCancel, "取消数据库编排", PermissionType.Action),
        new(PermissionCatalog.SystemDataServiceInitializationView, "查看服务初始化", PermissionType.Page),
        new(PermissionCatalog.SystemDataServiceInitializationRegister, "注册服务初始化", PermissionType.Action),
        new(PermissionCatalog.SystemDataServiceInitializationPlan, "生成服务初始化计划", PermissionType.Action),
        new(PermissionCatalog.SystemDataServiceInitializationApply, "执行服务初始化", PermissionType.Action),
        new(PermissionCatalog.SystemDataServiceInitializationApprove, "审批服务初始化", PermissionType.Action),
        new(PermissionCatalog.SystemDataServiceInitializationBackup, "确认服务初始化备份", PermissionType.Action),
        new(PermissionCatalog.SystemDataServiceInitializationCancel, "取消服务初始化", PermissionType.Action),
        new(PermissionCatalog.SystemDataFileUpload, "上传文件", PermissionType.Action),
        new(PermissionCatalog.SystemDataFileRead, "查看文件", PermissionType.Page),
        new(PermissionCatalog.SystemDataFileDownload, "下载文件", PermissionType.Action),
        new(PermissionCatalog.SystemDataFileManage, "管理文件", PermissionType.Action),
        new(PermissionCatalog.SystemDataFileDelete, "删除文件", PermissionType.Action),
        new(PermissionCatalog.SystemDataNotificationInboxRead, "查看通知收件箱", PermissionType.Page),
        new(PermissionCatalog.SystemDataNotificationAnnouncementRead, "查看公告", PermissionType.Page),
        new(PermissionCatalog.SystemDataNotificationAnnouncementManage, "管理公告", PermissionType.Action),
        new(PermissionCatalog.SystemDataNotificationAnnouncementPublish, "发布公告", PermissionType.Action),
        new(PermissionCatalog.SystemDataNotificationSystemSend, "发送系统消息", PermissionType.Action),
        new(PermissionCatalog.SystemDataAuditWrite, "写入审计事实", PermissionType.Action),
        new(PermissionCatalog.SystemDataAuditRead, "查看审计事实", PermissionType.Page),
        new(PermissionCatalog.SystemDataAuditExport, "导出审计事实", PermissionType.Action),
        new(PermissionCatalog.SystemDataAuditRetentionManage, "管理审计保留", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataDictionaryView, "字典：view", PermissionType.Page),
        new(PermissionCatalog.ReferenceDataDictionaryCreate, "字典：create", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataDictionaryUpdate, "字典：update", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataDictionaryPublish, "字典：publish", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataDictionaryDisable, "字典：disable", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataParameterView, "参数：view", PermissionType.Page),
        new(PermissionCatalog.ReferenceDataParameterCreate, "参数：create", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataParameterUpdate, "参数：update", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataParameterDisable, "参数：disable", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataParameterReadSecretReference, "参数：read-secret-reference", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataDynamicPropertyView, "动态配置：view", PermissionType.Page),
        new(PermissionCatalog.ReferenceDataDynamicPropertyCreate, "动态配置：create", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataDynamicPropertyUpdate, "动态配置：update", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataDynamicPropertyPublish, "动态配置：publish", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataDynamicPropertyDisable, "动态配置：disable", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataUnitOfMeasureView, "计量单位：view", PermissionType.Page),
        new(PermissionCatalog.ReferenceDataUnitOfMeasureCreate, "计量单位：create", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataUnitOfMeasureUpdate, "计量单位：update", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataUnitOfMeasurePublish, "计量单位：publish", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataUnitOfMeasureDisable, "计量单位：disable", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataMetadataView, "元数据：view", PermissionType.Page),
        new(PermissionCatalog.ReferenceDataMetadataCreate, "元数据：create", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataMetadataUpdate, "元数据：update", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataMetadataPublish, "元数据：publish", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataMetadataDisable, "元数据：disable", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataCodingRuleView, "编码规则：view", PermissionType.Page),
        new(PermissionCatalog.ReferenceDataCodingRuleCreate, "编码规则：create", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataCodingRuleUpdate, "编码规则：update", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataCodingRulePublish, "编码规则：publish", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataCodingRuleDisable, "编码规则：disable", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataCodingRulePreview, "编码规则：preview", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataCodingRuleGenerate, "编码规则：generate", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataStateMachineView, "状态机：view", PermissionType.Page),
        new(PermissionCatalog.ReferenceDataStateMachineCreate, "状态机：create", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataStateMachineUpdate, "状态机：update", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataStateMachinePublish, "状态机：publish", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataStateMachineDisable, "状态机：disable", PermissionType.Action),
        new(PermissionCatalog.ReferenceDataPlatformManage, "平台级参考定义：manage", PermissionType.Action),
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
