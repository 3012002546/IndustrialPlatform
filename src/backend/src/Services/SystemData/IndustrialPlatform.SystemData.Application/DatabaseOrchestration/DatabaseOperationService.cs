using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;
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

    /// <summary>v2:入队模块级异步 apply(POST service-initialization/operations/apply)。</summary>
    Task<EnqueueOperationV1> EnqueueApplyModuleAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        ServiceInitializationApplyRequestV2 request,
        string traceId,
        CancellationToken cancellationToken);

    /// <summary>v2:查询模块级服务初始化就绪(migration + RequiredSeed + bootstrap);未就绪返回 Ready=false,由控制器映射 503 SD_DB_NOT_READY。</summary>
    Task<ServiceInitializationReadinessV2> GetReadinessV2Async(
        string tenantNId,
        string serviceKey,
        string moduleKey,
        string traceId,
        CancellationToken cancellationToken);
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
            policy.BackupRequired,
            registration.ModuleKey,
            registration.SeedSets.Select(seed => seed.ToChecksumCanonical()).ToList());
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
    public async Task<EnqueueOperationV1> EnqueueApplyModuleAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        ServiceInitializationApplyRequestV2 request,
        string traceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var topology = _topologyProvider.GetTopology();
        var idempotencyKeyTrimmed = DatabaseOrchestrationInput.Require(idempotencyKey, "幂等键不能为空。");
        var planNId = DatabaseOrchestrationInput.Require(request.PlanNId, "计划标识不能为空。");
        var moduleKey = DatabaseOrchestrationInput.Require(request.ModuleKey, "模块标识不能为空。");
        var now = DateTimeOffset.UtcNow;

        var plan = await _store.GetPlanAsync(tenantNId, planNId, cancellationToken)
            ?? throw new NotFoundException();
        if (plan.IsExpired(now))
        {
            throw new PlanExpiredException();
        }

        var registration = await _store.GetRegistrationAsync(tenantNId, plan.EnvironmentNId, plan.ServiceKey, moduleKey, cancellationToken)
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
            policy.BackupRequired,
            registration.ModuleKey,
            registration.SeedSets.Select(seed => seed.ToChecksumCanonical()).ToList());
        if (!plan.MatchesTargetStateFingerprint(currentFingerprint))
        {
            throw new PlanDriftException();
        }

        if (!string.Equals(plan.RequestedMigrationVersion, registration.MigrationVersion, StringComparison.Ordinal))
        {
            throw new TargetMismatchException("计划请求版本与注册清单当前版本不一致。");
        }

        // apply 层 EnvironmentSample 门禁(蓝图 §12.3 第三层)。
        var environmentKind = EnvironmentPolicyResolver.ParseEnvironmentKind(plan.EnvironmentNId);
        if (environmentKind is DatabaseEnvironmentKind.Staging or DatabaseEnvironmentKind.Production
            && registration.SeedSets.Any(seed => seed.SeedClass == SeedClass.EnvironmentSample))
        {
            throw new SampleEnvironmentForbiddenException();
        }

        if (policy.ApprovalRequired && !await _approvalService.IsApprovedForAsync(tenantNId, plan, cancellationToken))
        {
            throw new ApprovalRequiredException();
        }

        if (policy.BackupRequired && !await _backupService.IsVerifiedForAsync(tenantNId, plan, cancellationToken))
        {
            throw new BackupRequiredException();
        }

        var requestHash = RequestHasher.HashApplyRequestV2(planNId, moduleKey, request.RequestedVersion);
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
            actorUserNId,
            moduleKey: moduleKey);
        operation.ClearDomainEvents();
        await WriteGuard.ExecuteAsync(() => _store.AddOperationAsync(operation, cancellationToken));
        return ToEnqueueV1(operation);
    }

    /// <inheritdoc />
    public async Task<DatabaseOperationV1> GetAsync(string tenantNId, string operationNId, CancellationToken cancellationToken)
    {
        var operation = await GetRequiredOperationAsync(tenantNId, operationNId, cancellationToken);
        return await ToOperationV1Async(operation, cancellationToken);
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
        var operations = await Task.WhenAll(page.Items.Select(operation => ToOperationV1Async(operation, cancellationToken)));
        return new DatabaseOrchestrationPageResult<DatabaseOperationV1>(
            operations,
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

    /// <inheritdoc />
    public async Task<ServiceInitializationReadinessV2> GetReadinessV2Async(
        string tenantNId,
        string serviceKey,
        string moduleKey,
        string traceId,
        CancellationToken cancellationToken)
    {
        var serviceKeyTrimmed = DatabaseOrchestrationInput.Require(serviceKey, "服务键不能为空。");
        var moduleKeyTrimmed = DatabaseOrchestrationInput.Require(moduleKey, "模块标识不能为空。");
        var topology = _topologyProvider.GetTopology();
        var environmentNId = topology.EnvironmentName;

        var registration = await _store.GetRegistrationAsync(tenantNId, environmentNId, serviceKeyTrimmed, moduleKeyTrimmed, cancellationToken);
        if (registration is null)
        {
            return NotReadyV2(serviceKeyTrimmed, moduleKeyTrimmed, "服务数据库尚未注册。", traceId);
        }

        var databaseIdentityFingerprint = DatabaseTopologyFingerprint.ComputeDatabaseIdentityFingerprint(
            environmentNId,
            serviceKeyTrimmed,
            registration.Provider,
            registration.LogicalDatabaseName,
            registration.PhysicalDatabaseName,
            registration.TopologyRevision);
        var observation = await _store.GetLatestObservationAsync(
            tenantNId,
            environmentNId,
            serviceKeyTrimmed,
            registration.ModuleKey,
            cancellationToken);
        var migrationObservationMatches = observation is not null
                                          && observation.VerificationStatus == VerificationStatus.Verified
                                          && string.Equals(observation.DatabaseIdentityFingerprint, databaseIdentityFingerprint, StringComparison.Ordinal)
                                          && string.Equals(observation.ArtifactChecksum, registration.ArtifactChecksum, StringComparison.Ordinal);
        var currentObservation = migrationObservationMatches ? observation : null;
        var migrationReady = registration.Status == RegistrationStatus.Registered
                             && currentObservation is not null
                             && string.Equals(currentObservation.ObservedVersion, registration.MigrationVersion, StringComparison.Ordinal);

        var seedStates = new List<SeedReadinessV2>();
        var requiredSeedReady = true;
        var bootstrapReady = true;
        var bootstrapObserved = false;
        var bootstrapPending = false;
        var bootstrapRecoveryRequired = false;
        string? reason = null;

        if (registration.Status != RegistrationStatus.Registered)
        {
            reason = "注册清单标记为未就绪。";
        }
        else if (observation is null)
        {
            reason = "尚未观察到当前初始化单元的目标数据库迁移状态。";
        }
        else if (!migrationObservationMatches)
        {
            reason = "迁移观察与当前数据库身份或产物不一致。";
        }
        else if (!string.Equals(observation.ObservedVersion, registration.MigrationVersion, StringComparison.Ordinal))
        {
            reason = "观察版本与期望版本不一致。";
        }

        foreach (var seed in registration.SeedSets.OrderBy(seed => seed.SeedKey, StringComparer.Ordinal))
        {
            var latest = await _store.GetLatestSeedObservationAsync(
                tenantNId, environmentNId, serviceKeyTrimmed, moduleKeyTrimmed, seed.SeedKey, cancellationToken);
            var applied = latest is not null
                          && latest.VerificationStatus == VerificationStatus.Verified
                          && latest.Status == SeedStatus.Applied
                          && string.Equals(latest.SeedVersion, seed.SeedVersion, StringComparison.Ordinal)
                          && string.Equals(latest.Checksum, seed.SeedChecksum, StringComparison.Ordinal)
                          && latest.Scope == seed.Scope;
            seedStates.Add(new SeedReadinessV2
            {
                SeedKey = seed.SeedKey,
                SeedVersion = seed.SeedVersion,
                Status = latest is null ? SeedStatus.Pending.ToString() : latest.Status.ToString(),
                AppliedOn = latest?.AppliedOn,
            });

            if (seed.SeedClass == SeedClass.SecretBootstrap)
            {
                bootstrapObserved = true;
                // bootstrap 完成 = 已应用,或按 SkipWhenMissing 策略记账 Skipped(不阻塞 readiness)。
                var skippedByPolicy = latest is not null
                                      && latest.VerificationStatus == VerificationStatus.Verified
                                      && latest.Status == SeedStatus.Skipped
                                      && seed.BootstrapPolicy == BootstrapPolicy.SkipWhenMissing
                                      && string.Equals(latest.SeedVersion, seed.SeedVersion, StringComparison.Ordinal)
                                      && string.Equals(latest.Checksum, seed.SeedChecksum, StringComparison.Ordinal)
                                      && latest.Scope == seed.Scope;
                if (!applied && !skippedByPolicy)
                {
                    bootstrapReady = false;
                    if (latest?.Status == SeedStatus.Failed)
                    {
                        bootstrapRecoveryRequired = true;
                        reason ??= $"SecretBootstrap 种子 {seed.SeedKey} 需要恢复处理。";
                    }
                    else
                    {
                        bootstrapPending = true;
                        reason ??= $"SecretBootstrap 种子 {seed.SeedKey} 尚未完成。";
                    }
                }
            }
            else if (seed.RequiredForReadiness && !applied)
            {
                requiredSeedReady = false;
                reason ??= $"RequiredForReadiness 种子 {seed.SeedKey} 未达到期望版本。";
            }
        }

        var ready = migrationReady && requiredSeedReady && bootstrapReady;
        var bootstrapStatus = !bootstrapObserved
            ? null
            : bootstrapRecoveryRequired
                ? "RecoveryRequired"
                : bootstrapPending
                    ? "Pending"
                    : "Ready";
        return new ServiceInitializationReadinessV2
        {
            ServiceKey = serviceKeyTrimmed,
            ModuleKey = moduleKeyTrimmed,
            LogicalDatabaseName = registration.LogicalDatabaseName,
            PhysicalDatabaseTarget = Mask(registration.PhysicalDatabaseName),
            DatabaseIdentityFingerprint = databaseIdentityFingerprint,
            ArtifactChecksum = registration.ArtifactChecksum,
            DesiredMigrationVersion = registration.MigrationVersion,
            ObservedMigrationVersion = currentObservation?.ObservedVersion,
            ObservedOn = currentObservation?.ObservedOn,
            TopologyRevision = registration.TopologyRevision,
            MigrationReady = migrationReady,
            RequiredSeedReady = requiredSeedReady,
            BootstrapReady = bootstrapReady,
            BootstrapStatus = bootstrapStatus,
            Seeds = seedStates,
            Ready = ready,
            Status = ready ? "Ready" : "NotReady",
            Reason = ready ? null : reason ?? BuildNotReadyReason(migrationReady),
            OperationNId = currentObservation?.OperationNId,
            TraceId = traceId,
        };
    }

    private static string BuildNotReadyReason(bool migrationReady) =>
        migrationReady ? "初始化未完成,请检查种子与 bootstrap 状态。" : "目标数据库未达到期望迁移版本。";

    private static ServiceInitializationReadinessV2 NotReadyV2(string serviceKey, string moduleKey, string reason, string traceId) => new()
    {
        ServiceKey = serviceKey,
        ModuleKey = moduleKey,
        LogicalDatabaseName = string.Empty,
        PhysicalDatabaseTarget = "***",
        DatabaseIdentityFingerprint = string.Empty,
        ArtifactChecksum = string.Empty,
        DesiredMigrationVersion = string.Empty,
        ObservedMigrationVersion = null,
        ObservedOn = null,
        TopologyRevision = string.Empty,
        MigrationReady = false,
        RequiredSeedReady = false,
        BootstrapReady = false,
        BootstrapStatus = null,
        Seeds = null,
        Ready = false,
        Status = "NotReady",
        Reason = reason,
        OperationNId = null,
        TraceId = traceId,
    };

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

    private async Task<DatabaseOperationV1> ToOperationV1Async(
        DatabaseProvisionOperation operation,
        CancellationToken cancellationToken)
    {
        var registration = await _store.GetRegistrationAsync(
            operation.TenantNId,
            operation.EnvironmentNId,
            operation.ServiceKey,
            operation.ModuleKey,
            cancellationToken);
        var observations = registration?.SeedSets.Count > 0
            ? (await Task.WhenAll(registration.SeedSets.Select(async seed =>
            {
                var observation = await _store.GetLatestSeedObservationAsync(
                    operation.TenantNId,
                    operation.EnvironmentNId,
                    operation.ServiceKey,
                    operation.ModuleKey,
                    seed.SeedKey,
                    cancellationToken);
                return observation is null ? null : ToSeedObservationV1(observation);
            }))).Where(observation => observation is not null).Cast<SeedObservationV1>().ToList()
            : null;
        return ToOperationV1(operation, observations);
    }

    private static DatabaseOperationV1 ToOperationV1(
        DatabaseProvisionOperation operation,
        IReadOnlyCollection<SeedObservationV1>? seedObservations = null) => new()
    {
        TenantNId = operation.TenantNId,
        OperationNId = operation.OperationNId,
        Kind = operation.Kind.ToString(),
        EnvironmentNId = operation.EnvironmentNId,
        ServiceKey = operation.ServiceKey,
        ModuleKey = operation.ModuleKey,
        PlanNId = operation.PlanNId,
        RequestedVersion = operation.RequestedVersion,
        IdempotencyKey = operation.IdempotencyKey,
        Status = operation.Status.ToString(),
        Phase = ToV1Phase(operation.Phase),
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
        SeedObservations = seedObservations,
    };

    private static SeedObservationV1 ToSeedObservationV1(DatabaseSeedObservation observation) => new()
    {
        TenantNId = observation.TenantNId,
        EnvironmentNId = observation.EnvironmentNId,
        ServiceKey = observation.ServiceKey,
        ModuleKey = observation.ModuleKey,
        SeedKey = observation.SeedKey,
        SeedVersion = observation.SeedVersion,
        Checksum = observation.Checksum,
        Scope = observation.Scope.ToString(),
        Status = observation.Status.ToString(),
        AppliedOn = observation.AppliedOn,
        OperationNId = observation.OperationNId,
        VerificationStatus = observation.VerificationStatus.ToString(),
    };

    /// <summary>v1 线兼容:SchemaMigration 阶段在 v1 契约中映射为 <c>Migrate</c>。</summary>
    private static string ToV1Phase(OperationPhase phase) =>
        phase == OperationPhase.SchemaMigration ? "Migrate" : phase.ToString();

    private static DatabaseOperationStepV1 ToStepV1(DatabaseOperationStep step) => new()
    {
        Sequence = step.Sequence,
        Phase = ToV1Phase(step.Phase),
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
