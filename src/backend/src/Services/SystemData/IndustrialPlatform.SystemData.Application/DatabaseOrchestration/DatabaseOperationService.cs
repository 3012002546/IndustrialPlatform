using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.Topology;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration;

/// <summary>操作用例端口。</summary>
public interface IOperationService
{
    /// <summary>入队异步 apply(POST /operations/apply)。门禁:计划存在/未过期/未漂移/目标匹配/审批与备份。幂等重放返回原 Operation。</summary>
    Task<EnqueueOperationV1> EnqueueApplyAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        DatabaseApplyRequestV1 request,
        string traceId,
        CancellationToken cancellationToken);

    /// <summary>按操作标识查询(含步骤);不存在抛 404。</summary>
    Task<DatabaseOperationV1> GetAsync(string tenantNId, string operationNId, CancellationToken cancellationToken);

    /// <summary>分页查询操作(Kind/Status 可选过滤)。</summary>
    Task<DatabaseOrchestrationPageResult<DatabaseOperationV1>> ListAsync(
        string tenantNId,
        string? kind,
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>取消操作(仅 Queued 或 Running 且未越过 Inspect 的安全边界)。</summary>
    Task<DatabaseOperationV1> CancelAsync(string tenantNId, string operationNId, CancellationToken cancellationToken);

    /// <summary>心跳续租(仅供 SD-003 Runner 驱动)。</summary>
    Task<DatabaseOperationV1> HeartbeatAsync(string tenantNId, string operationNId, string leaseOwner, CancellationToken cancellationToken);

    /// <summary>释放租约(仅供 SD-003 Runner 驱动)。</summary>
    Task<DatabaseOperationV1> ReleaseLeaseAsync(string tenantNId, string operationNId, string leaseOwner, CancellationToken cancellationToken);

    /// <summary>查询服务数据库就绪状态(注册清单 + 最近观察);未就绪返回 Ready=false,由控制器映射 503 SD_DB_NOT_READY。</summary>
    Task<DatabaseReadinessV1> GetReadinessAsync(string tenantNId, string serviceKey, string traceId, CancellationToken cancellationToken);
}

/// <summary>
/// 操作用例(05 方案 §9.2 apply 入队/状态/取消/readiness)。apply 入队时执行完整门禁链
/// (过期/漂移/目标匹配/审批/备份),SD-003 Runner 执行阶段再做兜底校验;
/// readiness 返回 SD-002 形状,完整握手契约留 TASK-SD-004。
/// </summary>
public sealed class DatabaseOperationService : IOperationService
{
    private readonly IDatabaseOrchestrationStore _store;
    private readonly IDatabaseTopologyProvider _topologyProvider;
    private readonly IApprovalService _approvalService;
    private readonly IBackupService _backupService;
    private readonly IOptions<DatabaseOrchestrationOptions> _options;

    public DatabaseOperationService(
        IDatabaseOrchestrationStore store,
        IDatabaseTopologyProvider topologyProvider,
        IApprovalService approvalService,
        IBackupService backupService,
        IOptions<DatabaseOrchestrationOptions> options)
    {
        _store = store;
        _topologyProvider = topologyProvider;
        _approvalService = approvalService;
        _backupService = backupService;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<EnqueueOperationV1> EnqueueApplyAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        DatabaseApplyRequestV1 request,
        string traceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var topology = _topologyProvider.GetTopology();
        var idempotencyKeyTrimmed = DatabaseOrchestrationInput.Require(idempotencyKey, "幂等键不能为空。");
        var planNId = DatabaseOrchestrationInput.Require(request.PlanNId, "计划标识不能为空。");
        var now = DateTimeOffset.UtcNow;

        var plan = await _store.GetPlanAsync(tenantNId, planNId, cancellationToken)
            ?? throw new NotFoundException();
        if (plan.IsExpired(now))
        {
            throw new PlanExpiredException();
        }

        var registration = await _store.GetRegistrationAsync(tenantNId, plan.EnvironmentNId, plan.ServiceKey, cancellationToken)
            ?? throw new TargetMismatchException("目标服务注册清单不存在。");

        var topologyRevision = DatabaseTopologyFingerprint.ComputeTopologyRevision(topology);
        if (!string.Equals(registration.TopologyRevision, topologyRevision, StringComparison.Ordinal))
        {
            throw new TopologyDriftException();
        }

        var policy = await EnvironmentPolicyResolver.ResolveAsync(
            _store, _options.Value, tenantNId, plan.EnvironmentNId, topology, cancellationToken);
        var currentFingerprint = DatabaseTopologyFingerprint.ComputeTargetStateFingerprint(
            plan.EnvironmentNId,
            plan.ServiceKey,
            registration.Provider,
            registration.LogicalDatabaseName,
            registration.PhysicalDatabaseName,
            registration.TopologyMode,
            registration.TopologyRevision,
            registration.ArtifactChecksum,
            plan.RequestedMigrationVersion,
            registration.DesiredState.ToString(),
            policy.ApprovalRequired,
            policy.BackupRequired);
        if (!plan.MatchesTargetStateFingerprint(currentFingerprint))
        {
            throw new PlanDriftException();
        }

        if (!string.Equals(plan.RequestedMigrationVersion, registration.MigrationVersion, StringComparison.Ordinal))
        {
            throw new TargetMismatchException("计划请求版本与注册清单当前版本不一致。");
        }

        if (policy.ApprovalRequired && !await _approvalService.IsApprovedForAsync(tenantNId, plan, cancellationToken))
        {
            throw new ApprovalRequiredException();
        }

        if (policy.BackupRequired && !await _backupService.IsVerifiedForAsync(tenantNId, plan, cancellationToken))
        {
            throw new BackupRequiredException();
        }

        var requestHash = RequestHasher.HashApplyRequest(planNId, request.RequestedVersion);
        var existing = await _store.FindOperationByIdempotencyKeyAsync(tenantNId, idempotencyKeyTrimmed, cancellationToken);
        if (existing is not null)
        {
            if (existing.MatchesRequestHash(requestHash))
            {
                return ToEnqueueV1(existing);
            }

            throw new OperationConflictException("同一幂等键已用于不同请求。");
        }

        var timeoutOn = now.AddSeconds(policy.ApplyTimeoutSeconds);
        var operation = DatabaseProvisionOperation.Enqueue(
            tenantNId,
            DatabaseOrchestrationInput.NewNId("OP"),
            OperationKind.Apply,
            plan.EnvironmentNId,
            plan.ServiceKey,
            plan.PlanNId,
            plan.RequestedMigrationVersion,
            idempotencyKeyTrimmed,
            requestHash,
            timeoutOn,
            traceId,
            actorUserNId);
        operation.ClearDomainEvents();
        await WriteGuard.ExecuteAsync(() => _store.AddOperationAsync(operation, cancellationToken));
        return ToEnqueueV1(operation);
    }

    /// <inheritdoc />
    public async Task<DatabaseOperationV1> GetAsync(string tenantNId, string operationNId, CancellationToken cancellationToken)
    {
        var operation = await GetRequiredOperationAsync(tenantNId, operationNId, cancellationToken);
        return ToOperationV1(operation);
    }

    /// <inheritdoc />
    public async Task<DatabaseOrchestrationPageResult<DatabaseOperationV1>> ListAsync(
        string tenantNId,
        string? kind,
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var filter = new OperationListFilter(
            tenantNId,
            string.IsNullOrWhiteSpace(kind) ? null : DatabaseOrchestrationInput.ParseEnum<OperationKind>(kind, nameof(OperationKind)),
            string.IsNullOrWhiteSpace(status) ? null : DatabaseOrchestrationInput.ParseEnum<OperationStatus>(status, nameof(OperationStatus)),
            pageIndex,
            pageSize);
        var page = await _store.QueryOperationsAsync(filter, cancellationToken);
        return new DatabaseOrchestrationPageResult<DatabaseOperationV1>(
            page.Items.Select(ToOperationV1).ToList(),
            page.Total,
            page.PageIndex,
            page.PageSize);
    }

    /// <inheritdoc />
    public async Task<DatabaseOperationV1> CancelAsync(string tenantNId, string operationNId, CancellationToken cancellationToken)
    {
        var operation = await GetRequiredOperationAsync(tenantNId, operationNId, cancellationToken);
        var expectedOptimisticVersion = operation.OptimisticVersion;
        var expectedConcurrencyVersion = operation.ConcurrencyVersion;

        try
        {
            operation.Cancel(DateTimeOffset.UtcNow);
        }
        catch (BusinessException)
        {
            throw new OperationNotCancellableException();
        }

        operation.ClearDomainEvents();
        await WriteGuard.ExecuteAsync(
            () => _store.UpdateOperationAsync(operation, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
        return ToOperationV1(operation);
    }

    /// <inheritdoc />
    public async Task<DatabaseOperationV1> HeartbeatAsync(
        string tenantNId,
        string operationNId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var operation = await GetRequiredOperationAsync(tenantNId, operationNId, cancellationToken);
        var expectedOptimisticVersion = operation.OptimisticVersion;
        var expectedConcurrencyVersion = operation.ConcurrencyVersion;

        try
        {
            operation.Heartbeat(
                DatabaseOrchestrationInput.Require(leaseOwner, "租约所有者不能为空。"),
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(_options.Value.LeaseSeconds));
        }
        catch (BusinessException ex)
        {
            throw new OperationConflictException(ex.Message);
        }

        operation.ClearDomainEvents();
        await WriteGuard.ExecuteAsync(
            () => _store.UpdateOperationAsync(operation, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
        return ToOperationV1(operation);
    }

    /// <inheritdoc />
    public async Task<DatabaseOperationV1> ReleaseLeaseAsync(
        string tenantNId,
        string operationNId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var operation = await GetRequiredOperationAsync(tenantNId, operationNId, cancellationToken);
        var expectedOptimisticVersion = operation.OptimisticVersion;
        var expectedConcurrencyVersion = operation.ConcurrencyVersion;

        try
        {
            operation.ReleaseLease(DatabaseOrchestrationInput.Require(leaseOwner, "租约所有者不能为空。"));
        }
        catch (BusinessException ex)
        {
            throw new OperationConflictException(ex.Message);
        }

        operation.ClearDomainEvents();
        await WriteGuard.ExecuteAsync(
            () => _store.UpdateOperationAsync(operation, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
        return ToOperationV1(operation);
    }

    /// <inheritdoc />
    public async Task<DatabaseReadinessV1> GetReadinessAsync(
        string tenantNId,
        string serviceKey,
        string traceId,
        CancellationToken cancellationToken)
    {
        var serviceKeyTrimmed = DatabaseOrchestrationInput.Require(serviceKey, "服务键不能为空。");
        var topology = _topologyProvider.GetTopology();
        var environmentNId = topology.EnvironmentName;

        var registration = await _store.GetRegistrationAsync(tenantNId, environmentNId, serviceKeyTrimmed, cancellationToken);
        if (registration is null)
        {
            return NotReady(serviceKeyTrimmed, "服务数据库尚未注册。", traceId);
        }

        var observation = await _store.GetLatestObservationAsync(tenantNId, environmentNId, serviceKeyTrimmed, cancellationToken);
        var ready = registration.Status == RegistrationStatus.Registered
                    && observation is not null
                    && string.Equals(observation.ObservedVersion, registration.MigrationVersion, StringComparison.Ordinal);

        string? reason = null;
        if (!ready)
        {
            reason = registration.Status != RegistrationStatus.Registered
                ? "注册清单标记为未就绪。"
                : observation is null
                    ? "尚未观察到目标数据库迁移状态。"
                    : "观察版本与期望版本不一致。";
        }

        return new DatabaseReadinessV1
        {
            ServiceKey = serviceKeyTrimmed,
            LogicalDatabaseName = registration.LogicalDatabaseName,
            PhysicalDatabaseTarget = Mask(registration.PhysicalDatabaseName),
            DatabaseIdentityFingerprint = DatabaseTopologyFingerprint.ComputeDatabaseIdentityFingerprint(
                environmentNId,
                serviceKeyTrimmed,
                registration.Provider,
                registration.LogicalDatabaseName,
                registration.PhysicalDatabaseName,
                registration.TopologyRevision),
            DesiredVersion = registration.MigrationVersion,
            ObservedVersion = observation?.ObservedVersion,
            ObservedOn = observation?.ObservedOn,
            TopologyRevision = registration.TopologyRevision,
            ArtifactChecksum = registration.ArtifactChecksum,
            Ready = ready,
            Status = ready ? "Ready" : "NotReady",
            Reason = reason,
            OperationNId = observation?.OperationNId,
            TraceId = traceId,
        };
    }

    private async Task<DatabaseProvisionOperation> GetRequiredOperationAsync(
        string tenantNId,
        string operationNId,
        CancellationToken cancellationToken)
    {
        var operationNIdTrimmed = DatabaseOrchestrationInput.Require(operationNId, "操作标识不能为空。");
        return await _store.GetOperationAsync(tenantNId, operationNIdTrimmed, cancellationToken)
            ?? throw new NotFoundException();
    }

    /// <summary>脱敏物理库目标:保留首 3 与末 2 字符,中间以星号遮蔽;过短目标完全遮蔽。</summary>
    private static string Mask(string physicalDatabaseName)
    {
        if (string.IsNullOrWhiteSpace(physicalDatabaseName))
        {
            return "***";
        }

        return physicalDatabaseName.Length <= 8
            ? "***"
            : $"{physicalDatabaseName[..3]}***{physicalDatabaseName[^2..]}";
    }

    private static DatabaseReadinessV1 NotReady(string serviceKey, string reason, string traceId) => new()
    {
        ServiceKey = serviceKey,
        LogicalDatabaseName = string.Empty,
        PhysicalDatabaseTarget = "***",
        DatabaseIdentityFingerprint = string.Empty,
        DesiredVersion = string.Empty,
        ObservedVersion = null,
        ObservedOn = null,
        TopologyRevision = string.Empty,
        ArtifactChecksum = string.Empty,
        Ready = false,
        Status = "NotReady",
        Reason = reason,
        OperationNId = null,
        TraceId = traceId,
    };

    private static DatabaseOperationV1 ToOperationV1(DatabaseProvisionOperation operation) => new()
    {
        TenantNId = operation.TenantNId,
        OperationNId = operation.OperationNId,
        Kind = operation.Kind.ToString(),
        EnvironmentNId = operation.EnvironmentNId,
        ServiceKey = operation.ServiceKey,
        PlanNId = operation.PlanNId,
        RequestedVersion = operation.RequestedVersion,
        IdempotencyKey = operation.IdempotencyKey,
        Status = operation.Status.ToString(),
        Phase = operation.Phase.ToString(),
        Attempt = operation.Attempt,
        LeaseOwner = operation.LeaseOwner,
        QueuedOn = operation.QueuedOn,
        StartedOn = operation.StartedOn,
        CompletedOn = operation.CompletedOn,
        TimeoutOn = operation.TimeoutOn,
        SanitizedErrorCode = operation.SanitizedErrorCode,
        SanitizedErrorSummary = operation.SanitizedErrorSummary,
        TraceId = operation.TraceId,
        CreatedByUserNId = operation.CreatedByUserNId,
        Steps = operation.Steps.Select(ToStepV1).ToList(),
    };

    private static DatabaseOperationStepV1 ToStepV1(DatabaseOperationStep step) => new()
    {
        Sequence = step.Sequence,
        Phase = step.Phase.ToString(),
        Status = step.Status.ToString(),
        Attempt = step.Attempt,
        StartedOn = step.StartedOn,
        CompletedOn = step.CompletedOn,
        SanitizedErrorCode = step.SanitizedErrorCode,
        SanitizedErrorSummary = step.SanitizedErrorSummary,
    };

    private static EnqueueOperationV1 ToEnqueueV1(DatabaseProvisionOperation operation) => new()
    {
        OperationNId = operation.OperationNId,
        Kind = operation.Kind.ToString(),
        Status = operation.Status.ToString(),
        Phase = operation.Phase.ToString(),
        AcceptedOn = operation.QueuedOn,
    };
}
