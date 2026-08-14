namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>受信任环境种类,用于环境策略默认门禁与拓扑允许规则。</summary>
public enum DatabaseEnvironmentKind
{
    Development,
    Test,
    Staging,
    Production,
}

/// <summary>注册清单状态。Registered 表示清单已保存并解析了物理身份;NotReady 表示目标未达到期望状态。</summary>
public enum RegistrationStatus
{
    Registered,
    NotReady,
}

/// <summary>服务的期望数据状态,决定编排行为边界。</summary>
public enum DesiredState
{
    /// <summary>该数据库是服务的权威数据源。</summary>
    SourceOfTruth,

    /// <summary>运行所需但非权威源。</summary>
    Operational,

    /// <summary>退役中,仅允许排空与读取。</summary>
    Retiring,
}

/// <summary>数据库编排操作类型。</summary>
public enum OperationKind
{
    /// <summary>异步生成不可变计划。</summary>
    Plan,

    /// <summary>异步执行目标数据库变更。</summary>
    Apply,
}

/// <summary>Operation 生命周期状态。</summary>
public enum OperationStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
}

/// <summary>Operation 执行阶段,按顺序推进;取消只允许 Queued 或安全阶段边界。</summary>
public enum OperationPhase
{
    /// <summary>校验输入与允许列表。</summary>
    Validate,

    /// <summary>检查目标数据库身份、当前版本、角色/grant 与环境策略。</summary>
    Inspect,

    /// <summary>创建数据库与最小角色授权(仅 provision admin)。</summary>
    ProvisionDatabase,

    /// <summary>配置目标服务角色(owner/migrator/runtime)。</summary>
    ProvisionRoles,

    /// <summary>在应用变更前完成并验证备份。</summary>
    Backup,

    /// <summary>应用签名迁移产物。</summary>
    Migrate,

    /// <summary>验证 exact desired state。</summary>
    Verify,
}

/// <summary>Operation 步骤生命周期状态。</summary>
public enum OperationStepStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Skipped,
}

/// <summary>计划风险等级。</summary>
public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>计划要求的环境门禁组合(按位组合)。</summary>
[Flags]
public enum DatabasePlanRequiredPolicies
{
    None = 0,
    Approval = 1,
    Backup = 2,
}

/// <summary>迁移观察的验证状态。</summary>
public enum VerificationStatus
{
    Pending,
    Verified,
    Failed,
}

/// <summary>备份证据状态。</summary>
public enum BackupEvidenceStatus
{
    Captured,
    Verified,
    Expired,
    Rejected,
}

/// <summary>审批状态。</summary>
public enum ApprovalStatus
{
    Approved,
    Rejected,
    Expired,
}
