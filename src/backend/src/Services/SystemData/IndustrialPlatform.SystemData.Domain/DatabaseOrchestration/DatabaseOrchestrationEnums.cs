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

/// <summary>
/// Operation 执行阶段,按顺序推进;取消只允许 Queued 或安全阶段边界。
/// TASK-SD-004:阶段 v2——<c>Migrate</c> 重命名为 <c>SchemaMigration</c>(显式值 5),
/// 新增 <c>RequiredSeed</c>(6)/<c>SecretBootstrap</c>(7),<c>Verify</c> 移到 8。
/// 线性枚举 + <c>(int)Phase+1</c> 推进;持久化 int 重编号安全(现有操作一次性
/// queued→running→completed、无持久化中途 phase)。v1 契约序列化时映射为 <c>"Migrate"</c>。
/// </summary>
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

    /// <summary>应用签名迁移产物(v1 线映射为 <c>"Migrate"</c>)。</summary>
    SchemaMigration = 5,

    /// <summary>执行 RequiredForReadiness 种子(SystemBaseline/TenantBaseline)。</summary>
    RequiredSeed = 6,

    /// <summary>按需 SecretBootstrap(环境策略+manifest 同时允许;缺失 Secret fail-closed)。</summary>
    SecretBootstrap = 7,

    /// <summary>验证 exact desired state(迁移+RequiredSeed+bootstrap 就绪)。</summary>
    Verify = 8,
}

/// <summary>种子类别,决定允许环境、readiness 语义与执行路径。</summary>
public enum SeedClass
{
    /// <summary>系统基线种子,默认 RequiredForReadiness;全环境允许。</summary>
    SystemBaseline,

    /// <summary>租户基线种子,按租户作用域执行。</summary>
    TenantBaseline,

    /// <summary>环境示例数据,仅 Development/Test 允许(registration/plan/apply 三层拒绝 Staging/Production)。</summary>
    EnvironmentSample,

    /// <summary>按需 bootstrap 的秘密种子(账本与观察不含秘密值)。</summary>
    SecretBootstrap,
}

/// <summary>种子作用域(执行粒度与幂等范围)。</summary>
public enum SeedScope
{
    /// <summary>系统级(跨租户一次执行)。</summary>
    System,

    /// <summary>租户级(每租户一次执行)。</summary>
    Tenant,
}

/// <summary>种子账本状态。</summary>
public enum SeedStatus
{
    /// <summary>已应用并记账。</summary>
    Applied,

    /// <summary>已声明、等待执行。</summary>
    Pending,

    /// <summary>执行失败(从账本边界重试)。</summary>
    Failed,

    /// <summary>被跳过(如 SecretBootstrap SkipWhenMissing 策略)。</summary>
    Skipped,
}

/// <summary>SecretBootstrap 交付策略。</summary>
public enum BootstrapPolicy
{
    /// <summary>缺失 Secret 时失败并 NotReady(fail-closed,默认)。</summary>
    FailClosed,

    /// <summary>缺失 Secret 时跳过,不阻塞 readiness。</summary>
    SkipWhenMissing,
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
