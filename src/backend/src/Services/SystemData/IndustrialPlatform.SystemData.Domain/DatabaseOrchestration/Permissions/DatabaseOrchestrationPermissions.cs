namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration.Permissions;

/// <summary>
/// 数据库编排七项权限(05 方案 §9.6)。真实授权管线由 TASK-SD-006 接入;
/// 本阶段仅冻结稳定 PermissionNId 常量,供契约、文档与未来策略使用。
/// </summary>
public static class DatabaseOrchestrationPermissions
{
    /// <summary>查看注册、计划与操作。</summary>
    public const string View = "systemdata.database-orchestration.view";

    /// <summary>注册版本化清单。</summary>
    public const string Register = "systemdata.database-orchestration.register";

    /// <summary>请求异步计划。</summary>
    public const string Plan = "systemdata.database-orchestration.plan";

    /// <summary>请求异步 apply。</summary>
    public const string Apply = "systemdata.database-orchestration.apply";

    /// <summary>记录绑定计划的审批。</summary>
    public const string Approve = "systemdata.database-orchestration.approve";

    /// <summary>登记并验证备份证据。</summary>
    public const string Backup = "systemdata.database-orchestration.backup";

    /// <summary>在允许边界取消操作。</summary>
    public const string Cancel = "systemdata.database-orchestration.cancel";
}
