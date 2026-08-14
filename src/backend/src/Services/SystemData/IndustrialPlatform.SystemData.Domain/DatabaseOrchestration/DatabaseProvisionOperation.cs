using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration.Events;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>
/// 数据库编排 Operation 聚合根(05 方案 §7.1.4、§8.1 <c>system_data_database_operation</c>)。
/// 状态机:Queued → Running → Succeeded / Failed / Cancelled / TimedOut;入队时创建全部阶段步骤。
/// 取消仅允许 Queued 或 Running 且阶段未越过 Inspect(ProvisionDatabase 之前)的安全边界;
/// lease 60s / heartbeat 15s 由 SD-003 Runner 驱动,本聚合持有租约与超时字段。
/// </summary>
public sealed class DatabaseProvisionOperation : AggregateRoot
{
    /// <summary>IdempotencyKey 最大长度。</summary>
    public const int IdempotencyKeyMaxLength = 128;

    /// <summary>TraceId 最大长度。</summary>
    public const int TraceIdMaxLength = 128;

    /// <summary>租约所有者标识最大长度。</summary>
    public const int LeaseOwnerMaxLength = 128;

    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; private set; }

    /// <summary>操作业务标识(如 <c>OP-...</c>)。</summary>
    public string OperationNId { get; private set; }

    /// <summary>操作类型。</summary>
    public OperationKind Kind { get; private set; }

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; private set; }

    /// <summary>服务稳定键。</summary>
    public string ServiceKey { get; private set; }

    /// <summary>关联计划业务标识(Apply 操作绑定)。</summary>
    public string? PlanNId { get; private set; }

    /// <summary>请求的目标迁移版本。</summary>
    public string RequestedVersion { get; private set; }

    /// <summary>客户端幂等键。</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>规范化请求哈希,与幂等键组合判定重放。</summary>
    public string RequestHash { get; private set; }

    /// <summary>操作状态。</summary>
    public OperationStatus Status { get; private set; }

    /// <summary>当前阶段。</summary>
    public OperationPhase Phase { get; private set; }

    /// <summary>当前尝试序号(入队 0,启动后 1)。</summary>
    public int Attempt { get; private set; }

    /// <summary>当前租约所有者(Runner 实例标识)。</summary>
    public string? LeaseOwner { get; private set; }

    /// <summary>租约到期时间。</summary>
    public DateTimeOffset? LeaseExpiresOn { get; private set; }

    /// <summary>最近一次心跳时间。</summary>
    public DateTimeOffset? HeartbeatOn { get; private set; }

    /// <summary>入队时间。</summary>
    public DateTimeOffset QueuedOn { get; private set; }

    /// <summary>开始执行时间。</summary>
    public DateTimeOffset? StartedOn { get; private set; }

    /// <summary>结束时间(成功/失败/取消/超时)。</summary>
    public DateTimeOffset? CompletedOn { get; private set; }

    /// <summary>操作超时截止(此后未完成即超时)。</summary>
    public DateTimeOffset TimeoutOn { get; private set; }

    /// <summary>脱敏错误码。</summary>
    public string? SanitizedErrorCode { get; private set; }

    /// <summary>脱敏错误摘要。</summary>
    public string? SanitizedErrorSummary { get; private set; }

    /// <summary>追踪标识。</summary>
    public string TraceId { get; private set; }

    /// <summary>发起人业务标识。</summary>
    public string CreatedByUserNId { get; private set; }

    private readonly List<DatabaseOperationStep> _steps = [];

    /// <summary>按顺序排列的操作步骤。</summary>
    public IReadOnlyCollection<DatabaseOperationStep> Steps => _steps;

    private DatabaseProvisionOperation()
    {
        TenantNId = string.Empty;
        OperationNId = string.Empty;
        EnvironmentNId = string.Empty;
        ServiceKey = string.Empty;
        RequestedVersion = string.Empty;
        IdempotencyKey = string.Empty;
        RequestHash = string.Empty;
        TraceId = string.Empty;
        CreatedByUserNId = string.Empty;
    }

    private DatabaseProvisionOperation(
        string tenantNId,
        string operationNId,
        OperationKind kind,
        string environmentNId,
        string serviceKey,
        string? planNId,
        string requestedVersion,
        string idempotencyKey,
        string requestHash,
        DateTimeOffset timeoutOn,
        string traceId,
        string createdByUserNId,
        IReadOnlyCollection<OperationPhase> phases)
    {
        TenantNId = DatabaseOrchestrationGuard.RequireNId(tenantNId, "操作的租户标识不能为空。");
        OperationNId = DatabaseOrchestrationGuard.RequireNId(operationNId, "操作标识不能为空。");
        Kind = kind;
        EnvironmentNId = DatabaseOrchestrationGuard.RequireNId(environmentNId, "操作的环境标识不能为空。");
        ServiceKey = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            serviceKey, "服务键不能为空。", DatabaseRegistration.ServiceKeyMaxLength, $"服务键长度不能超过 {DatabaseRegistration.ServiceKeyMaxLength} 个字符。");
        PlanNId = planNId is null
            ? null
            : DatabaseOrchestrationGuard.RequireNId(planNId, "关联计划标识不能为空。");
        RequestedVersion = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            requestedVersion, "请求版本不能为空。", DatabaseProvisionPlan.VersionMaxLength, $"请求版本长度不能超过 {DatabaseProvisionPlan.VersionMaxLength} 个字符。");
        IdempotencyKey = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            idempotencyKey, "幂等键不能为空。", IdempotencyKeyMaxLength, $"幂等键长度不能超过 {IdempotencyKeyMaxLength} 个字符。");
        RequestHash = DatabaseOrchestrationGuard.RequireSha256Hex(requestHash, "请求哈希不能为空。");
        Status = OperationStatus.Queued;
        Phase = OperationPhase.Validate;
        Attempt = 0;
        QueuedOn = DateTimeOffset.UtcNow;
        TimeoutOn = timeoutOn;
        TraceId = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            traceId, "TraceId 不能为空。", TraceIdMaxLength, $"TraceId 长度不能超过 {TraceIdMaxLength} 个字符。");
        CreatedByUserNId = DatabaseOrchestrationGuard.RequireNId(createdByUserNId, "发起人标识不能为空。");

        var sequence = 1;
        foreach (var phase in phases)
        {
            _steps.Add(new DatabaseOperationStep(sequence++, phase, 0));
        }

        AddDomainEvent(CreateStatusChangedEvent());
    }

    /// <summary>持久化层重建专用构造,不重新校验、不发布事件。</summary>
    internal DatabaseProvisionOperation(
        Guid id,
        string tenantNId,
        string operationNId,
        OperationKind kind,
        string environmentNId,
        string serviceKey,
        string? planNId,
        string requestedVersion,
        string idempotencyKey,
        string requestHash,
        OperationStatus status,
        OperationPhase phase,
        int attempt,
        string? leaseOwner,
        DateTimeOffset? leaseExpiresOn,
        DateTimeOffset? heartbeatOn,
        DateTimeOffset queuedOn,
        DateTimeOffset? startedOn,
        DateTimeOffset? completedOn,
        DateTimeOffset timeoutOn,
        string? sanitizedErrorCode,
        string? sanitizedErrorSummary,
        string traceId,
        string createdByUserNId,
        IReadOnlyCollection<DatabaseOperationStep> steps,
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
        OperationNId = operationNId;
        Kind = kind;
        EnvironmentNId = environmentNId;
        ServiceKey = serviceKey;
        PlanNId = planNId;
        RequestedVersion = requestedVersion;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        Status = status;
        Phase = phase;
        Attempt = attempt;
        LeaseOwner = leaseOwner;
        LeaseExpiresOn = leaseExpiresOn;
        HeartbeatOn = heartbeatOn;
        QueuedOn = queuedOn;
        StartedOn = startedOn;
        CompletedOn = completedOn;
        TimeoutOn = timeoutOn;
        SanitizedErrorCode = sanitizedErrorCode;
        SanitizedErrorSummary = sanitizedErrorSummary;
        TraceId = traceId;
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

    /// <summary>入队操作(Status = Queued,Phase = Validate)。</summary>
    public static DatabaseProvisionOperation Enqueue(
        string tenantNId,
        string operationNId,
        OperationKind kind,
        string environmentNId,
        string serviceKey,
        string? planNId,
        string requestedVersion,
        string idempotencyKey,
        string requestHash,
        DateTimeOffset timeoutOn,
        string traceId,
        string createdByUserNId,
        IReadOnlyCollection<OperationPhase>? phases = null)
        => new(
            tenantNId,
            operationNId,
            kind,
            environmentNId,
            serviceKey,
            planNId,
            requestedVersion,
            idempotencyKey,
            requestHash,
            timeoutOn,
            traceId,
            createdByUserNId,
            phases ?? AllPhases);

    /// <summary>是否可按幂等键重放(请求哈希一致)。</summary>
    public bool MatchesRequestHash(string requestHash) =>
        string.Equals(RequestHash, requestHash, StringComparison.Ordinal);

    /// <summary>是否允许取消:Queued,或 Running 且阶段未越过 Inspect(ProvisionDatabase 之前的安全边界)。</summary>
    public bool IsCancellable =>
        Status == OperationStatus.Queued
        || (Status == OperationStatus.Running && Phase <= OperationPhase.Inspect);

    /// <summary>是否已超过操作超时截止。</summary>
    public bool HasTimedOut(DateTimeOffset now) => now > TimeoutOn;

    /// <summary>开始执行(Queued → Running),获取租约。</summary>
    public void Start(string leaseOwner, DateTimeOffset now, TimeSpan leaseDuration)
    {
        EnsureCanModify();
        EnsureState(Status == OperationStatus.Queued, "只有 Queued 状态的操作才能启动。");
        Status = OperationStatus.Running;
        Attempt = 1;
        LeaseOwner = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            leaseOwner, "租约所有者不能为空。", LeaseOwnerMaxLength, $"租约所有者标识长度不能超过 {LeaseOwnerMaxLength} 个字符。");
        LeaseExpiresOn = now.Add(leaseDuration);
        HeartbeatOn = now;
        StartedOn = now;
        CurrentStep?.MarkRunning(now, Attempt);
        PublishStatusChanged();
        Touch();
    }

    /// <summary>推进到下一阶段(Validate→Inspect→…→Verify),已完成阶段标记成功。</summary>
    public void AdvanceToNextPhase(DateTimeOffset now)
    {
        EnsureCanModify();
        EnsureState(Status == OperationStatus.Running, "只有 Running 状态的操作才能推进阶段。");
        EnsureState(Phase < OperationPhase.Verify, "已在最后阶段 Verify,应执行 Complete 而非推进。");
        CurrentStep?.MarkSucceeded(now);
        Phase = (OperationPhase)((int)Phase + 1);
        CurrentStep?.MarkRunning(now, Attempt);
        PublishStatusChanged();
        Touch();
    }

    /// <summary>成功完成(Running → Succeeded)。</summary>
    public void Complete(DateTimeOffset now)
    {
        EnsureCanModify();
        EnsureState(Status == OperationStatus.Running, "只有 Running 状态的操作才能完成。");
        CurrentStep?.MarkSucceeded(now);
        Status = OperationStatus.Succeeded;
        CompletedOn = now;
        ReleaseLeaseFields();
        PublishStatusChanged();
        Touch();
    }

    /// <summary>失败(Queued/Running → Failed),记录脱敏错误。</summary>
    public void Fail(string code, string summary, DateTimeOffset now)
    {
        EnsureCanModify();
        EnsureState(Status is OperationStatus.Queued or OperationStatus.Running, "只有 Queued 或 Running 状态的操作才能失败。");
        CurrentStep?.MarkFailed(now, code, summary);
        Status = OperationStatus.Failed;
        CompletedOn = now;
        SanitizedErrorCode = code;
        SanitizedErrorSummary = summary;
        ReleaseLeaseFields();
        PublishStatusChanged();
        Touch();
    }

    /// <summary>取消(仅 Queued 或安全阶段边界)。</summary>
    public void Cancel(DateTimeOffset now)
    {
        EnsureCanModify();
        EnsureState(IsCancellable, "当前状态不允许取消:仅 Queued 或 Running 且未越过 Inspect 阶段。");
        if (Status == OperationStatus.Running)
        {
            CurrentStep?.MarkCancelled(now);
        }

        foreach (var step in _steps.Where(step => step.Status == OperationStepStatus.Pending))
        {
            step.MarkCancelled(now);
        }

        Status = OperationStatus.Cancelled;
        CompletedOn = now;
        ReleaseLeaseFields();
        PublishStatusChanged();
        Touch();
    }

    /// <summary>超时(Running/Queued → TimedOut),记录脱敏超时错误。</summary>
    public void Timeout(DateTimeOffset now)
    {
        EnsureCanModify();
        EnsureState(Status is OperationStatus.Queued or OperationStatus.Running, "只有 Queued 或 Running 状态的操作才能超时。");
        CurrentStep?.MarkFailed(now, TimedOutErrorCode, "操作超过超时截止未完成。");
        foreach (var step in _steps.Where(step => step.Status == OperationStepStatus.Pending))
        {
            step.MarkCancelled(now);
        }

        Status = OperationStatus.TimedOut;
        CompletedOn = now;
        SanitizedErrorCode = TimedOutErrorCode;
        SanitizedErrorSummary = "操作超过超时截止未完成。";
        ReleaseLeaseFields();
        PublishStatusChanged();
        Touch();
    }

    /// <summary>心跳续租(仅 Running 且租约所有者匹配)。</summary>
    public void Heartbeat(string leaseOwner, DateTimeOffset now, TimeSpan leaseDuration)
    {
        EnsureCanModify();
        EnsureState(Status == OperationStatus.Running, "只有 Running 状态的操作才能心跳。");
        EnsureState(MatchesLeaseOwner(leaseOwner), "租约所有者不匹配,无法心跳。");
        HeartbeatOn = now;
        LeaseExpiresOn = now.Add(leaseDuration);
        Touch();
    }

    /// <summary>释放租约(仅租约所有者匹配)。</summary>
    public void ReleaseLease(string leaseOwner)
    {
        EnsureCanModify();
        EnsureState(MatchesLeaseOwner(leaseOwner), "租约所有者不匹配,无法释放租约。");
        ReleaseLeaseFields();
        Touch();
    }

    /// <summary>当前阶段对应的步骤(入队时为 Pending,推进后为 Running)。</summary>
    private DatabaseOperationStep? CurrentStep => _steps.FirstOrDefault(step => step.Phase == Phase);

    private bool MatchesLeaseOwner(string leaseOwner) =>
        !string.IsNullOrEmpty(LeaseOwner) && string.Equals(LeaseOwner, leaseOwner, StringComparison.Ordinal);

    private void ReleaseLeaseFields()
    {
        LeaseOwner = null;
        LeaseExpiresOn = null;
        HeartbeatOn = null;
    }

    private DatabaseOperationStatusChangedEvent CreateStatusChangedEvent() =>
        new(TenantNId, OperationNId, Kind, Status, Phase, TraceId);

    private void PublishStatusChanged() => AddDomainEvent(CreateStatusChangedEvent());

    private static void EnsureState(bool condition, string message)
    {
        if (!condition)
        {
            throw new BusinessException(message);
        }
    }

    /// <summary>全部阶段(顺序:Validate→Inspect→ProvisionDatabase→ProvisionRoles→Backup→Migrate→Verify)。</summary>
    internal static readonly OperationPhase[] AllPhases =
    [
        OperationPhase.Validate,
        OperationPhase.Inspect,
        OperationPhase.ProvisionDatabase,
        OperationPhase.ProvisionRoles,
        OperationPhase.Backup,
        OperationPhase.Migrate,
        OperationPhase.Verify,
    ];

    private const string TimedOutErrorCode = "SD_DB_OPERATION_TIMED_OUT";
}
