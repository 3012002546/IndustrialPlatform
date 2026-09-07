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

    /// <summary>查看当前有效刷新会话。</summary>
    public const string SessionView = "identity.session.view";

    /// <summary>撤销单个刷新会话。</summary>
    public const string SessionRevoke = "identity.session.revoke";

    /// <summary>平台首页查看。</summary>
    public const string PlatformHomeView = "platform.home.view";

    /// <summary>平台生产操作模式查看。</summary>
    public const string PlatformOperationView = "platform.operation.view";

    /// <summary>平台 PDA 查看。</summary>
    public const string PlatformPdaView = "platform.pda.view";

    /// <summary>平台移动端查看。</summary>
    public const string PlatformMobileView = "platform.mobile.view";

    /// <summary>查看 bootstrap 状态(§29A.5):仅状态/版本,不含 Secret。</summary>
    public const string BootstrapView = "identity.bootstrap.view";

    /// <summary>紧急恢复内置 admin(§29A.5):接受一次性恢复引用与审批关联,不接受明文密码。</summary>
    public const string BootstrapRecover = "identity.bootstrap.recover";

    public const string SystemDataOrganizationView = "systemdata.organization.view";
    public const string SystemDataOrganizationCreate = "systemdata.organization.create";
    public const string SystemDataOrganizationUpdate = "systemdata.organization.update";
    public const string SystemDataOrganizationMove = "systemdata.organization.move";
    public const string SystemDataOrganizationStatus = "systemdata.organization.status";
    public const string SystemDataPositionView = "systemdata.position.view";
    public const string SystemDataPositionCreate = "systemdata.position.create";
    public const string SystemDataPositionUpdate = "systemdata.position.update";
    public const string SystemDataPositionStatus = "systemdata.position.status";
    public const string SystemDataAssignmentView = "systemdata.assignment.view";
    public const string SystemDataAssignmentManage = "systemdata.assignment.manage";
    public const string SystemDataResourceView = "systemdata.resource.view";
    public const string SystemDataNavigationView = "systemdata.navigation.view";
    public const string SystemDataNavigationManage = "systemdata.navigation.manage";
    public const string SystemDataNavigationPublish = "systemdata.navigation.publish";
    public const string SystemDataNavigationRollback = "systemdata.navigation.rollback";
    public const string SystemDataFeatureView = "systemdata.feature.view";
    public const string SystemDataFeatureManage = "systemdata.feature.manage";
    public const string SystemDataServiceCatalogView = "systemdata.service-catalog.view";
    public const string SystemDataServiceCatalogManage = "systemdata.service-catalog.manage";
    public const string SystemDataThemePolicyView = "systemdata.theme-policy.view";
    public const string SystemDataThemePolicyManage = "systemdata.theme-policy.manage";
    public const string SystemDataDatabaseOrchestrationView = "systemdata.database-orchestration.view";
    public const string SystemDataDatabaseOrchestrationRegister = "systemdata.database-orchestration.register";
    public const string SystemDataDatabaseOrchestrationPlan = "systemdata.database-orchestration.plan";
    public const string SystemDataDatabaseOrchestrationApply = "systemdata.database-orchestration.apply";
    public const string SystemDataDatabaseOrchestrationApprove = "systemdata.database-orchestration.approve";
    public const string SystemDataDatabaseOrchestrationBackup = "systemdata.database-orchestration.backup";
    public const string SystemDataDatabaseOrchestrationCancel = "systemdata.database-orchestration.cancel";
    public const string SystemDataServiceInitializationView = "systemdata.service-initialization.view";
    public const string SystemDataServiceInitializationRegister = "systemdata.service-initialization.register";
    public const string SystemDataServiceInitializationPlan = "systemdata.service-initialization.plan";
    public const string SystemDataServiceInitializationApply = "systemdata.service-initialization.apply";
    public const string SystemDataServiceInitializationApprove = "systemdata.service-initialization.approve";
    public const string SystemDataServiceInitializationBackup = "systemdata.service-initialization.backup";
    public const string SystemDataServiceInitializationCancel = "systemdata.service-initialization.cancel";
    public const string SystemDataFileUpload = "systemdata.file.upload";
    public const string SystemDataFileRead = "systemdata.file.read";
    public const string SystemDataFileDownload = "systemdata.file.download";
    public const string SystemDataFileManage = "systemdata.file.manage";
    public const string SystemDataFileDelete = "systemdata.file.delete";
    public const string SystemDataNotificationInboxRead = "systemdata.notification.inbox.read";
    public const string SystemDataNotificationAnnouncementRead = "systemdata.notification.announcement.read";
    public const string SystemDataNotificationAnnouncementManage = "systemdata.notification.announcement.manage";
    public const string SystemDataNotificationAnnouncementPublish = "systemdata.notification.announcement.publish";
    public const string SystemDataNotificationSystemSend = "systemdata.notification.system.send";
    public const string SystemDataAuditWrite = "systemdata.audit.write";
    public const string SystemDataAuditRead = "systemdata.audit.read";
    public const string SystemDataAuditExport = "systemdata.audit.export";
    public const string SystemDataAuditRetentionManage = "systemdata.audit.retention.manage";

    public const string ReferenceDataDictionaryView = "referencedata.dictionary.view";
    public const string ReferenceDataDictionaryCreate = "referencedata.dictionary.create";
    public const string ReferenceDataDictionaryUpdate = "referencedata.dictionary.update";
    public const string ReferenceDataDictionaryPublish = "referencedata.dictionary.publish";
    public const string ReferenceDataDictionaryDisable = "referencedata.dictionary.disable";
    public const string ReferenceDataParameterView = "referencedata.parameter.view";
    public const string ReferenceDataParameterCreate = "referencedata.parameter.create";
    public const string ReferenceDataParameterUpdate = "referencedata.parameter.update";
    public const string ReferenceDataParameterDisable = "referencedata.parameter.disable";
    public const string ReferenceDataParameterReadSecretReference = "referencedata.parameter.read-secret-reference";
    public const string ReferenceDataDynamicPropertyView = "referencedata.dynamic-property.view";
    public const string ReferenceDataDynamicPropertyCreate = "referencedata.dynamic-property.create";
    public const string ReferenceDataDynamicPropertyUpdate = "referencedata.dynamic-property.update";
    public const string ReferenceDataDynamicPropertyPublish = "referencedata.dynamic-property.publish";
    public const string ReferenceDataDynamicPropertyDisable = "referencedata.dynamic-property.disable";
    public const string ReferenceDataUnitOfMeasureView = "referencedata.unit-of-measure.view";
    public const string ReferenceDataUnitOfMeasureCreate = "referencedata.unit-of-measure.create";
    public const string ReferenceDataUnitOfMeasureUpdate = "referencedata.unit-of-measure.update";
    public const string ReferenceDataUnitOfMeasurePublish = "referencedata.unit-of-measure.publish";
    public const string ReferenceDataUnitOfMeasureDisable = "referencedata.unit-of-measure.disable";
    public const string ReferenceDataMetadataView = "referencedata.metadata.view";
    public const string ReferenceDataMetadataCreate = "referencedata.metadata.create";
    public const string ReferenceDataMetadataUpdate = "referencedata.metadata.update";
    public const string ReferenceDataMetadataPublish = "referencedata.metadata.publish";
    public const string ReferenceDataMetadataDisable = "referencedata.metadata.disable";
    public const string ReferenceDataCodingRuleView = "referencedata.coding-rule.view";
    public const string ReferenceDataCodingRuleCreate = "referencedata.coding-rule.create";
    public const string ReferenceDataCodingRuleUpdate = "referencedata.coding-rule.update";
    public const string ReferenceDataCodingRulePublish = "referencedata.coding-rule.publish";
    public const string ReferenceDataCodingRuleDisable = "referencedata.coding-rule.disable";
    public const string ReferenceDataCodingRulePreview = "referencedata.coding-rule.preview";
    public const string ReferenceDataCodingRuleGenerate = "referencedata.coding-rule.generate";
    public const string ReferenceDataStateMachineView = "referencedata.state-machine.view";
    public const string ReferenceDataStateMachineCreate = "referencedata.state-machine.create";
    public const string ReferenceDataStateMachineUpdate = "referencedata.state-machine.update";
    public const string ReferenceDataStateMachinePublish = "referencedata.state-machine.publish";
    public const string ReferenceDataStateMachineDisable = "referencedata.state-machine.disable";
    public const string ReferenceDataPlatformManage = "referencedata.platform.manage";

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
        SessionView,
        SessionRevoke,
        PlatformHomeView,
        PlatformOperationView,
        PlatformPdaView,
        PlatformMobileView,
        BootstrapView,
        BootstrapRecover,
        SystemDataOrganizationView,
        SystemDataOrganizationCreate,
        SystemDataOrganizationUpdate,
        SystemDataOrganizationMove,
        SystemDataOrganizationStatus,
        SystemDataPositionView,
        SystemDataPositionCreate,
        SystemDataPositionUpdate,
        SystemDataPositionStatus,
        SystemDataAssignmentView,
        SystemDataAssignmentManage,
        SystemDataResourceView,
        SystemDataNavigationView,
        SystemDataNavigationManage,
        SystemDataNavigationPublish,
        SystemDataNavigationRollback,
        SystemDataFeatureView,
        SystemDataFeatureManage,
        SystemDataServiceCatalogView,
        SystemDataServiceCatalogManage,
        SystemDataThemePolicyView,
        SystemDataThemePolicyManage,
        SystemDataDatabaseOrchestrationView,
        SystemDataDatabaseOrchestrationRegister,
        SystemDataDatabaseOrchestrationPlan,
        SystemDataDatabaseOrchestrationApply,
        SystemDataDatabaseOrchestrationApprove,
        SystemDataDatabaseOrchestrationBackup,
        SystemDataDatabaseOrchestrationCancel,
        SystemDataServiceInitializationView,
        SystemDataServiceInitializationRegister,
        SystemDataServiceInitializationPlan,
        SystemDataServiceInitializationApply,
        SystemDataServiceInitializationApprove,
        SystemDataServiceInitializationBackup,
        SystemDataServiceInitializationCancel,
        SystemDataFileUpload,
        SystemDataFileRead,
        SystemDataFileDownload,
        SystemDataFileManage,
        SystemDataFileDelete,
        SystemDataNotificationInboxRead,
        SystemDataNotificationAnnouncementRead,
        SystemDataNotificationAnnouncementManage,
        SystemDataNotificationAnnouncementPublish,
        SystemDataNotificationSystemSend,
        SystemDataAuditWrite,
        SystemDataAuditRead,
        SystemDataAuditExport,
        SystemDataAuditRetentionManage,
        ReferenceDataDictionaryView,
        ReferenceDataDictionaryCreate,
        ReferenceDataDictionaryUpdate,
        ReferenceDataDictionaryPublish,
        ReferenceDataDictionaryDisable,
        ReferenceDataParameterView,
        ReferenceDataParameterCreate,
        ReferenceDataParameterUpdate,
        ReferenceDataParameterDisable,
        ReferenceDataParameterReadSecretReference,
        ReferenceDataDynamicPropertyView,
        ReferenceDataDynamicPropertyCreate,
        ReferenceDataDynamicPropertyUpdate,
        ReferenceDataDynamicPropertyPublish,
        ReferenceDataDynamicPropertyDisable,
        ReferenceDataUnitOfMeasureView,
        ReferenceDataUnitOfMeasureCreate,
        ReferenceDataUnitOfMeasureUpdate,
        ReferenceDataUnitOfMeasurePublish,
        ReferenceDataUnitOfMeasureDisable,
        ReferenceDataMetadataView,
        ReferenceDataMetadataCreate,
        ReferenceDataMetadataUpdate,
        ReferenceDataMetadataPublish,
        ReferenceDataMetadataDisable,
        ReferenceDataCodingRuleView,
        ReferenceDataCodingRuleCreate,
        ReferenceDataCodingRuleUpdate,
        ReferenceDataCodingRulePublish,
        ReferenceDataCodingRuleDisable,
        ReferenceDataCodingRulePreview,
        ReferenceDataCodingRuleGenerate,
        ReferenceDataStateMachineView,
        ReferenceDataStateMachineCreate,
        ReferenceDataStateMachineUpdate,
        ReferenceDataStateMachinePublish,
        ReferenceDataStateMachineDisable,
        ReferenceDataPlatformManage,
    ];
}
