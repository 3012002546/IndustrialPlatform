using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>
/// 不可变数据库供应计划聚合根(05 方案 §7.1.3、§8.1 <c>system_data_database_plan</c>)。
/// 创建即固化(IsFrozen),无业务修改方法;PlanChecksum 在创建时由 <see cref="DatabaseTopologyFingerprint"/>
/// 按全部字段与步骤内容计算,成功后不可变。expiry/drift 由 Application 与 SD-003 门禁校验。
/// </summary>
public sealed class DatabaseProvisionPlan : AggregateRoot
{
    /// <summary>版本串最大长度。</summary>
    public const int VersionMaxLength = 64;

    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; private set; }

    /// <summary>计划业务标识(如 <c>PLAN-...</c>)。</summary>
    public string PlanNId { get; private set; }

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; private set; }

    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; private set; }

    /// <summary>模块标识;按 (ServiceKey, ModuleKey) 粒度。</summary>
    public string ModuleKey { get; private set; }

    /// <summary>请求的目标迁移版本。</summary>
    public string RequestedMigrationVersion { get; private set; }

    /// <summary>计划生成时观察到的当前迁移版本。</summary>
    public string CurrentMigrationVersion { get; private set; }

    /// <summary>目标状态指纹(计划生成时固化;apply 前重算对比判定 drift)。</summary>
    public string TargetStateFingerprint { get; private set; }

    /// <summary>计划校验和(§8.1 唯一)。</summary>
    public string PlanChecksum { get; private set; }

    /// <summary>计划风险等级。</summary>
    public RiskLevel RiskLevel { get; private set; }

    /// <summary>是否检测到破坏性变更。</summary>
    public bool DestructiveChangeDetected { get; private set; }

    /// <summary>服务初始化器首次计划声明的 RequiresApply；legacy SQL Runner 计划为空。</summary>
    public bool? ServiceRequiresApply { get; private set; }

    /// <summary>计划要求的环境门禁组合(审批/备份)。</summary>
    public DatabasePlanRequiredPolicies RequiredPolicies { get; private set; }

    /// <summary>计划有效期截止(默认创建后 30 分钟,由创建方决定)。</summary>
    public DateTimeOffset ExpiresOn { get; private set; }

    /// <summary>创建人业务标识。</summary>
    public string CreatedByUserNId { get; private set; }

    private readonly List<DatabasePlanStep> _steps = [];

    /// <summary>按顺序排列的不可变计划步骤。</summary>
    public IReadOnlyCollection<DatabasePlanStep> Steps => _steps;

    private DatabaseProvisionPlan()
    {
        TenantNId = string.Empty;
        PlanNId = string.Empty;
        EnvironmentNId = string.Empty;
        ServiceKey = string.Empty;
        ModuleKey = string.Empty;
        RequestedMigrationVersion = string.Empty;
        CurrentMigrationVersion = string.Empty;
        TargetStateFingerprint = string.Empty;
        PlanChecksum = string.Empty;
        CreatedByUserNId = string.Empty;
    }

    private DatabaseProvisionPlan(
        string tenantNId,
        string planNId,
        string environmentNId,
        string serviceKey,
        string? moduleKey,
        string requestedMigrationVersion,
        string currentMigrationVersion,
        string targetStateFingerprint,
        RiskLevel riskLevel,
        bool destructiveChangeDetected,
        bool? serviceRequiresApply,
        DatabasePlanRequiredPolicies requiredPolicies,
        DateTimeOffset expiresOn,
        string createdByUserNId,
        IReadOnlyCollection<DatabasePlanStep> steps)
    {
        TenantNId = DatabaseOrchestrationGuard.RequireNId(tenantNId, "计划的租户标识不能为空。");
        PlanNId = DatabaseOrchestrationGuard.RequireNId(planNId, "计划标识不能为空。");
        EnvironmentNId = DatabaseOrchestrationGuard.RequireNId(environmentNId, "计划的环境标识不能为空。");
        ServiceKey = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            serviceKey, "服务键不能为空。", DatabaseRegistration.ServiceKeyMaxLength, $"服务键长度不能超过 {DatabaseRegistration.ServiceKeyMaxLength} 个字符。");
        ModuleKey = moduleKey is null
            ? serviceKey
            : DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
                moduleKey, "模块标识不能为空。", DatabaseRegistration.ModuleKeyMaxLength, $"模块标识长度不能超过 {DatabaseRegistration.ModuleKeyMaxLength} 个字符。");
        RequestedMigrationVersion = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            requestedMigrationVersion, "请求版本不能为空。", VersionMaxLength, $"请求版本长度不能超过 {VersionMaxLength} 个字符。");
        CurrentMigrationVersion = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            currentMigrationVersion, "当前版本不能为空。", VersionMaxLength, $"当前版本长度不能超过 {VersionMaxLength} 个字符。");
        TargetStateFingerprint = DatabaseOrchestrationGuard.RequireSha256Hex(targetStateFingerprint, "目标状态指纹不能为空。");
        RiskLevel = riskLevel;
        DestructiveChangeDetected = destructiveChangeDetected;
        ServiceRequiresApply = serviceRequiresApply;
        RequiredPolicies = requiredPolicies;
        ExpiresOn = expiresOn;
        CreatedByUserNId = DatabaseOrchestrationGuard.RequireNId(createdByUserNId, "创建人标识不能为空。");

        if (steps is null || steps.Count == 0)
        {
            throw new ValidationException("计划至少需要一个步骤。");
        }

        var ordered = steps.OrderBy(step => step.Sequence).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Sequence != i + 1)
            {
                throw new ValidationException("计划步骤顺序必须连续且从 1 开始。");
            }
        }

        _steps.AddRange(ordered);
        PlanChecksum = DatabaseTopologyFingerprint.ComputePlanChecksum(
            planNId,
            tenantNId,
            environmentNId,
            serviceKey,
            requestedMigrationVersion,
            currentMigrationVersion,
            targetStateFingerprint,
            riskLevel.ToString(),
            destructiveChangeDetected,
            requiredPolicies.ToString(),
            _steps.Select(step => step.ToChecksumCanonical()).ToList(),
            moduleKey,
            serviceRequiresApply);
        IsFrozen = true;
    }

    /// <summary>持久化层重建专用构造,不重新校验。</summary>
    internal DatabaseProvisionPlan(
        Guid id,
        string tenantNId,
        string planNId,
        string environmentNId,
        string serviceKey,
        string moduleKey,
        string requestedMigrationVersion,
        string currentMigrationVersion,
        string targetStateFingerprint,
        string planChecksum,
        RiskLevel riskLevel,
        bool destructiveChangeDetected,
        bool? serviceRequiresApply,
        DatabasePlanRequiredPolicies requiredPolicies,
        DateTimeOffset expiresOn,
        string createdByUserNId,
        IReadOnlyCollection<DatabasePlanStep> steps,
        bool isFrozen,
        bool isLocked,
        bool isDeleted,
        string entityType,
        DateTimeOffset createdOn,
        DateTimeOffset lastUpdatedOn,
        long optimisticVersion,
        Guid concurrencyVersion)
        : base()
    {
        Id = id;
        TenantNId = tenantNId;
        PlanNId = planNId;
        EnvironmentNId = environmentNId;
        ServiceKey = serviceKey;
        ModuleKey = moduleKey;
        RequestedMigrationVersion = requestedMigrationVersion;
        CurrentMigrationVersion = currentMigrationVersion;
        TargetStateFingerprint = targetStateFingerprint;
        PlanChecksum = planChecksum;
        RiskLevel = riskLevel;
        DestructiveChangeDetected = destructiveChangeDetected;
        ServiceRequiresApply = serviceRequiresApply;
        RequiredPolicies = requiredPolicies;
        ExpiresOn = expiresOn;
        CreatedByUserNId = createdByUserNId;
        _steps.AddRange(steps ?? []);
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>创建不可变计划(固化)。v1 兼容:moduleKey 缺省 = serviceKey。</summary>
    public static DatabaseProvisionPlan Create(
        string tenantNId,
        string planNId,
        string environmentNId,
        string serviceKey,
        string requestedMigrationVersion,
        string currentMigrationVersion,
        string targetStateFingerprint,
        RiskLevel riskLevel,
        bool destructiveChangeDetected,
        DatabasePlanRequiredPolicies requiredPolicies,
        DateTimeOffset expiresOn,
        string createdByUserNId,
        IReadOnlyCollection<DatabasePlanStep> steps,
        string? moduleKey = null,
        bool? serviceRequiresApply = null)
        => new(
            tenantNId,
            planNId,
            environmentNId,
            serviceKey,
            moduleKey,
            requestedMigrationVersion,
            currentMigrationVersion,
            targetStateFingerprint,
            riskLevel,
            destructiveChangeDetected,
            serviceRequiresApply,
            requiredPolicies,
            expiresOn,
            createdByUserNId,
            steps);

    /// <summary>是否已过期。</summary>
    public bool IsExpired(DateTimeOffset now) => now > ExpiresOn;

    /// <summary>目标状态指纹是否与给定指纹一致。</summary>
    public bool MatchesTargetStateFingerprint(string targetStateFingerprint) =>
        string.Equals(TargetStateFingerprint, targetStateFingerprint, StringComparison.Ordinal);

    /// <summary>计划校验和是否与给定校验和一致。</summary>
    public bool MatchesPlanChecksum(string planChecksum) =>
        string.Equals(PlanChecksum, planChecksum, StringComparison.Ordinal);
}
