using Microsoft.AspNetCore.Authorization;

namespace IndustrialPlatform.SystemData.Api.Authorization;

/// <summary>
/// SystemData 服务端权限策略(TASK-SD-006,05 方案 §9.6):每个目录权限注册一条策略,
/// 策略名 <c>permission:{permissionNId}</c>。控制器用策略名常量声明
/// <c>[Authorize(Policy = SystemDataPermissionPolicies.OrganizationView)]</c>。
/// 本任务仅登记 §9.3 组织/岗位/任职(11 项)与既存 v1/v2 初始化控制面权限
/// (database-orchestration.* / service-initialization.*,各 7 项);其余模块权限随各自任务登记。
/// </summary>
public static class SystemDataPermissionPolicies
{
    /// <summary>策略名前缀。</summary>
    public const string Prefix = "permission:";

    // ===== 行政组织(§9.3) =====

    /// <summary>查看组织森林与详情。</summary>
    public const string OrganizationView = Prefix + "systemdata.organization.view";

    /// <summary>创建组织。</summary>
    public const string OrganizationCreate = Prefix + "systemdata.organization.create";

    /// <summary>修改组织名称、顺序。</summary>
    public const string OrganizationUpdate = Prefix + "systemdata.organization.update";

    /// <summary>组织移动预览与提交。</summary>
    public const string OrganizationMove = Prefix + "systemdata.organization.move";

    /// <summary>组织启用/停用。</summary>
    public const string OrganizationStatus = Prefix + "systemdata.organization.status";

    // ===== 岗位(§9.3) =====

    /// <summary>查看岗位。</summary>
    public const string PositionView = Prefix + "systemdata.position.view";

    /// <summary>创建岗位。</summary>
    public const string PositionCreate = Prefix + "systemdata.position.create";

    /// <summary>修改岗位。</summary>
    public const string PositionUpdate = Prefix + "systemdata.position.update";

    /// <summary>岗位启用/停用。</summary>
    public const string PositionStatus = Prefix + "systemdata.position.status";

    // ===== 用户任职(§9.3) =====

    /// <summary>查看用户任职。</summary>
    public const string AssignmentView = Prefix + "systemdata.assignment.view";

    /// <summary>管理用户任职(创建/改区间/结束/取消/主任职切换)。</summary>
    public const string AssignmentManage = Prefix + "systemdata.assignment.manage";

    // ===== 数据库编排 v1(migration-only 兼容期,§9.2/§9.6) =====

    /// <summary>查看注册/计划/操作。</summary>
    public const string DatabaseOrchestrationView = Prefix + "systemdata.database-orchestration.view";

    /// <summary>注册/重注册版本化清单。</summary>
    public const string DatabaseOrchestrationRegister = Prefix + "systemdata.database-orchestration.register";

    /// <summary>入队计划。</summary>
    public const string DatabaseOrchestrationPlan = Prefix + "systemdata.database-orchestration.plan";

    /// <summary>入队 apply。</summary>
    public const string DatabaseOrchestrationApply = Prefix + "systemdata.database-orchestration.apply";

    /// <summary>登记审批。</summary>
    public const string DatabaseOrchestrationApprove = Prefix + "systemdata.database-orchestration.approve";

    /// <summary>登记并验证备份证据。</summary>
    public const string DatabaseOrchestrationBackup = Prefix + "systemdata.database-orchestration.backup";

    /// <summary>取消操作。</summary>
    public const string DatabaseOrchestrationCancel = Prefix + "systemdata.database-orchestration.cancel";

    // ===== 服务初始化 v2(§9.2/§9.6) =====

    /// <summary>查看注册/计划/操作。</summary>
    public const string ServiceInitializationView = Prefix + "systemdata.service-initialization.view";

    /// <summary>注册/重注册模块级版本化清单。</summary>
    public const string ServiceInitializationRegister = Prefix + "systemdata.service-initialization.register";

    /// <summary>入队模块级计划。</summary>
    public const string ServiceInitializationPlan = Prefix + "systemdata.service-initialization.plan";

    /// <summary>入队模块级 apply。</summary>
    public const string ServiceInitializationApply = Prefix + "systemdata.service-initialization.apply";

    /// <summary>登记模块级审批。</summary>
    public const string ServiceInitializationApprove = Prefix + "systemdata.service-initialization.approve";

    /// <summary>登记模块级备份证据。</summary>
    public const string ServiceInitializationBackup = Prefix + "systemdata.service-initialization.backup";

    /// <summary>取消模块级操作。</summary>
    public const string ServiceInitializationCancel = Prefix + "systemdata.service-initialization.cancel";

    /// <summary>本任务登记的权限 NId 集合(§9.3 三组 + v1/v2 初始化兼容权限)。</summary>
    private static readonly string[] RegisteredPermissionNIds =
    [
        "systemdata.organization.view",
        "systemdata.organization.create",
        "systemdata.organization.update",
        "systemdata.organization.move",
        "systemdata.organization.status",
        "systemdata.position.view",
        "systemdata.position.create",
        "systemdata.position.update",
        "systemdata.position.status",
        "systemdata.assignment.view",
        "systemdata.assignment.manage",
        "systemdata.database-orchestration.view",
        "systemdata.database-orchestration.register",
        "systemdata.database-orchestration.plan",
        "systemdata.database-orchestration.apply",
        "systemdata.database-orchestration.approve",
        "systemdata.database-orchestration.backup",
        "systemdata.database-orchestration.cancel",
        "systemdata.service-initialization.view",
        "systemdata.service-initialization.register",
        "systemdata.service-initialization.plan",
        "systemdata.service-initialization.apply",
        "systemdata.service-initialization.approve",
        "systemdata.service-initialization.backup",
        "systemdata.service-initialization.cancel",
    ];

    /// <summary>由权限 NId 生成策略名。</summary>
    public static string PolicyName(string permissionNId) => Prefix + permissionNId;

    /// <summary>为登记权限集合中的全部权限注册策略(§9.6)。</summary>
    public static void AddPermissionPolicies(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var permissionNId in RegisteredPermissionNIds)
        {
            options.AddPolicy(PolicyName(permissionNId), policy =>
                policy.Requirements.Add(new SystemDataPermissionRequirement(permissionNId)));
        }
    }
}
