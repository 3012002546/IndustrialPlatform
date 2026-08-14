namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration.Permissions;

/// <summary>
/// 服务初始化七项权限(TASK-SD-004,05 方案 §9.6)。真实授权管线由 TASK-SD-006 接入;
/// 本阶段仅冻结稳定 PermissionNId 常量,供契约、文档与未来策略使用。
/// </summary>
public static class ServiceInitializationPermissions
{
    /// <summary>查看注册、计划、操作与 readiness。</summary>
    public const string View = "systemdata.service-initialization.view";

    /// <summary>按 (ServiceKey, ModuleKey) 注册版本化清单。</summary>
    public const string Register = "systemdata.service-initialization.register";

    /// <summary>请求异步初始化计划。</summary>
    public const string Plan = "systemdata.service-initialization.plan";

    /// <summary>请求异步 apply。</summary>
    public const string Apply = "systemdata.service-initialization.apply";

    /// <summary>记录绑定计划的审批。</summary>
    public const string Approve = "systemdata.service-initialization.approve";

    /// <summary>登记并验证备份证据。</summary>
    public const string Backup = "systemdata.service-initialization.backup";

    /// <summary>在允许边界取消操作。</summary>
    public const string Cancel = "systemdata.service-initialization.cancel";
}
