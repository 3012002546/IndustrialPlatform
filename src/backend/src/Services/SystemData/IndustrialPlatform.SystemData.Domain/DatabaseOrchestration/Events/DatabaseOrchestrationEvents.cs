using IndustrialPlatform.SharedKernel.Events;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration.Events;

/// <summary>
/// 注册清单创建或重注册后发布。仅携带稳定身份、公开版本与清单校验和,
/// 不含连接串、Secret 值、角色密码或任何物理凭据(05 方案 §9.8)。
/// </summary>
public sealed class DatabaseRegistrationChangedEvent : IDomainEvent
{
    public DatabaseRegistrationChangedEvent(
        string tenantNId,
        string environmentNId,
        string serviceKey,
        string moduleKey,
        string logicalDatabaseName,
        DesiredState desiredState,
        string manifestVersion,
        string manifestChecksum)
    {
        TenantNId = tenantNId;
        EnvironmentNId = environmentNId;
        ServiceKey = serviceKey;
        ModuleKey = moduleKey;
        LogicalDatabaseName = logicalDatabaseName;
        DesiredState = desiredState;
        ManifestVersion = manifestVersion;
        ManifestChecksum = manifestChecksum;
        OccurredOn = DateTimeOffset.UtcNow;
    }

    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; }

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; }

    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; }

    /// <summary>模块标识。</summary>
    public string ModuleKey { get; }

    /// <summary>稳定逻辑库名。</summary>
    public string LogicalDatabaseName { get; }

    /// <summary>期望数据状态。</summary>
    public DesiredState DesiredState { get; }

    /// <summary>公开清单版本。</summary>
    public string ManifestVersion { get; }

    /// <summary>清单校验和(非 Secret)。</summary>
    public string ManifestChecksum { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOn { get; }
}

/// <summary>不可变计划生成完成后发布(本阶段由未来 Runner 生成;事件契约先行冻结)。</summary>
public sealed class DatabasePlanCompletedEvent : IDomainEvent
{
    public DatabasePlanCompletedEvent(
        string tenantNId,
        string planNId,
        string serviceKey,
        string targetStateFingerprint,
        string planChecksum)
    {
        TenantNId = tenantNId;
        PlanNId = planNId;
        ServiceKey = serviceKey;
        TargetStateFingerprint = targetStateFingerprint;
        PlanChecksum = planChecksum;
        OccurredOn = DateTimeOffset.UtcNow;
    }

    public string TenantNId { get; }

    public string PlanNId { get; }

    public string ServiceKey { get; }

    public string TargetStateFingerprint { get; }

    public string PlanChecksum { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOn { get; }
}

/// <summary>
/// Operation 状态转换后发布。仅携带稳定标识、公开状态、阶段与 TraceId,
/// 不含脱敏错误的原文、SQL 或迁移输出(05 方案 §9.8)。
/// </summary>
public sealed class DatabaseOperationStatusChangedEvent : IDomainEvent
{
    public DatabaseOperationStatusChangedEvent(
        string tenantNId,
        string operationNId,
        OperationKind kind,
        OperationStatus status,
        OperationPhase phase,
        string traceId)
    {
        TenantNId = tenantNId;
        OperationNId = operationNId;
        Kind = kind;
        Status = status;
        Phase = phase;
        TraceId = traceId;
        OccurredOn = DateTimeOffset.UtcNow;
    }

    public string TenantNId { get; }

    public string OperationNId { get; }

    public OperationKind Kind { get; }

    public OperationStatus Status { get; }

    public OperationPhase Phase { get; }

    public string TraceId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOn { get; }
}

/// <summary>
/// 种子应用后发布(TASK-SD-004)。仅携带稳定身份、版本与校验和,不含种子内容、
/// Secret 值、SQL 或任何凭据(蓝图 §5.3 脱敏观察)。
/// </summary>
public sealed class SeedAppliedEvent : IDomainEvent
{
    public SeedAppliedEvent(
        string tenantNId,
        string environmentNId,
        string serviceKey,
        string moduleKey,
        string seedKey,
        string seedVersion,
        string checksum,
        SeedScope scope,
        string operationNId)
    {
        TenantNId = tenantNId;
        EnvironmentNId = environmentNId;
        ServiceKey = serviceKey;
        ModuleKey = moduleKey;
        SeedKey = seedKey;
        SeedVersion = seedVersion;
        Checksum = checksum;
        Scope = scope;
        OperationNId = operationNId;
        OccurredOn = DateTimeOffset.UtcNow;
    }

    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; }

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; }

    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; }

    /// <summary>模块标识。</summary>
    public string ModuleKey { get; }

    /// <summary>稳定种子键。</summary>
    public string SeedKey { get; }

    /// <summary>种子版本。</summary>
    public string SeedVersion { get; }

    /// <summary>种子产物校验和(非 Secret)。</summary>
    public string Checksum { get; }

    /// <summary>种子作用域。</summary>
    public SeedScope Scope { get; }

    /// <summary>产生该应用的操作业务标识。</summary>
    public string OperationNId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredOn { get; }
}
