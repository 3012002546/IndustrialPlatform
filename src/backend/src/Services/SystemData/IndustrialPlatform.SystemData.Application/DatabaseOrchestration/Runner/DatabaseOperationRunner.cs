using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.Topology;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// 数据库编排 Operation Runner 编排核心(05 方案 §7.1.4/§7.1.5)。
/// 单实例串行执行一个 Operation;领取后按 Validate→…→Verify 推进状态机,每阶段心跳续租,
/// Apply 越过 Inspect 后持同目标 advisory lock;迁移前瞬时失败受限重试,迁移失败不盲重试。
/// 编排核心不感知具体数据库驱动,只调用 Runner 适配器端口;所有错误经脱敏映射稳定错误码。
/// </summary>
public sealed class DatabaseOperationRunner : IOperationRunnerCoordinator
{
    private readonly IDatabaseOrchestrationStore _store;
    private readonly IDatabaseTopologyProvider _topologyProvider;
    private readonly IApprovalService _approvalService;
    private readonly IBackupService _backupService;
    private readonly IOptions<DatabaseOrchestrationOptions> _options;
    private readonly IOptions<DatabaseOperationRunnerOptions> _runnerOptions;
    private readonly ITargetDatabaseInspector _inspector;
    private readonly ITargetDatabaseProvisioner _provisioner;
    private readonly IMigrationArtifactStore _artifactStore;
    private readonly IMigrationArtifactVerifier _artifactVerifier;
    private readonly IMigrationExecutor _migrationExecutor;
    private readonly IDatabaseCredentialResolver _credentialResolver;
    private readonly IDatabaseCredentialSink _credentialSink;
    private readonly ITargetDatabaseAdvisoryLock _advisoryLock;

    private ActiveOperation? _active;
    private IDisposable? _lockHandle;
    private string? _instanceId;
    private long _persistedOptimisticVersion;
    private Guid _persistedConcurrencyVersion;

    /// <summary>初始化 Runner 编排核心。</summary>
    public DatabaseOperationRunner(
        IDatabaseOrchestrationStore store,
        IDatabaseTopologyProvider topologyProvider,
        IApprovalService approvalService,
        IBackupService backupService,
        IOptions<DatabaseOrchestrationOptions> options,
        IOptions<DatabaseOperationRunnerOptions> runnerOptions,
        ITargetDatabaseInspector inspector,
        ITargetDatabaseProvisioner provisioner,
        IMigrationArtifactStore artifactStore,
        IMigrationArtifactVerifier artifactVerifier,
        IMigrationExecutor migrationExecutor,
        IDatabaseCredentialResolver credentialResolver,
        IDatabaseCredentialSink credentialSink,
        ITargetDatabaseAdvisoryLock advisoryLock)
    {
        _store = store;
        _topologyProvider = topologyProvider;
        _approvalService = approvalService;
        _backupService = backupService;
        _options = options;
        _runnerOptions = runnerOptions;
        _inspector = inspector;
        _provisioner = provisioner;
        _artifactStore = artifactStore;
        _artifactVerifier = artifactVerifier;
        _migrationExecutor = migrationExecutor;
        _credentialResolver = credentialResolver;
        _credentialSink = credentialSink;
        _advisoryLock = advisoryLock;
    }

    /// <inheritdoc />
    public bool HasActiveOperation => _active is not null;

    /// <inheritdoc />
    public string? ActiveOperationNId => _active?.OperationNId;

    /// <inheritdoc />
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var leaseOwner = InstanceId;
        var operation = await _store.ClaimNextOperationAsync(
            leaseOwner,
            DateTimeOffset.UtcNow,
            LeaseDuration,
            cancellationToken);
        if (operation is null)
        {
            return 0;
        }

        _active = new ActiveOperation(operation.TenantNId, operation.OperationNId, leaseOwner);
        // 领取后 operation 的版本即库中最新持久化版本;后续每轮 Touch 递增,保存须以该快照为 expected。
        _persistedOptimisticVersion = operation.OptimisticVersion;
        _persistedConcurrencyVersion = operation.ConcurrencyVersion;
        try
        {
            await ExecuteOperationAsync(operation, leaseOwner, cancellationToken);
        }
        finally
        {
            _lockHandle?.Dispose();
            _lockHandle = null;
            _active = null;
        }

        return 1;
    }

    /// <inheritdoc />
    public async Task HeartbeatActiveAsync(CancellationToken cancellationToken)
    {
        var active = _active;
        if (active is null)
        {
            return;
        }

        var operation = await _store.GetOperationAsync(active.TenantNId, active.OperationNId, cancellationToken);
        if (operation is null || operation.Status != OperationStatus.Running)
        {
            return;
        }

        var expectedOptimisticVersion = operation.OptimisticVersion;
        var expectedConcurrencyVersion = operation.ConcurrencyVersion;
        try
        {
            operation.Heartbeat(active.LeaseOwner, DateTimeOffset.UtcNow, LeaseDuration);
            operation.ClearDomainEvents();
            await _store.UpdateOperationAsync(
                operation,
                expectedOptimisticVersion,
                expectedConcurrencyVersion,
                cancellationToken);
        }
        catch (ConcurrencyException)
        {
            // 租约已被其他实例接管或操作已结束,忽略。
        }
    }

    // ===== 编排主循环 =====

    private async Task ExecuteOperationAsync(
        DatabaseProvisionOperation operation,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var context = new RunContext();
        var topology = _topologyProvider.GetTopology();
        var policy = await EnvironmentPolicyResolver.ResolveAsync(
            _store, _options.Value, operation.TenantNId, operation.EnvironmentNId, topology, cancellationToken);
        context.Policy = policy;
        var maxPreMigrationRetries = policy.MaxPreMigrationRetries;
        var preMigrationRetries = 0;

        while (operation.Status == OperationStatus.Running)
        {
            var now = DateTimeOffset.UtcNow;
            if (operation.HasTimedOut(now))
            {
                operation.Timeout(now);
                await SaveOperationAsync(operation, cancellationToken);
                return;
            }

            // 心跳续租(每阶段一次;长阶段由 HostedService 定时续租兜底)。
            operation.Heartbeat(leaseOwner, now, LeaseDuration);
            await SaveOperationAsync(operation, cancellationToken);

            var execution = await ExecuteCurrentPhaseAsync(context, operation, cancellationToken);
            if (execution.Outcome == PhaseOutcome.Succeeded)
            {
                preMigrationRetries = 0;

                if (operation.Kind == OperationKind.Plan && operation.Phase == OperationPhase.Inspect)
                {
                    // Plan 操作生成不可变计划后即完成。
                    operation.Complete(now);
                    await SaveOperationAsync(operation, cancellationToken);
                    return;
                }

                if (operation.Phase == OperationPhase.Verify)
                {
                    // Apply 操作到达最后阶段 Verify 且成功后即完成;AdvanceToNextPhase 在 Verify 阶段会拒推。
                    operation.Complete(now);
                    await SaveOperationAsync(operation, cancellationToken);
                    return;
                }

                operation.AdvanceToNextPhase(now);
                await SaveOperationAsync(operation, cancellationToken);

                if (_lockHandle is null && operation.Kind == OperationKind.Apply && operation.Phase > OperationPhase.Inspect)
                {
                    if (!await AcquireTargetLockAsync(context, operation, now, cancellationToken))
                    {
                        return;
                    }
                }
            }
            else if (execution.Outcome == PhaseOutcome.RetryableFailure
                     && operation.Phase < OperationPhase.Migrate
                     && preMigrationRetries < maxPreMigrationRetries)
            {
                // 迁移前瞬时失败受限重试;不推进阶段,下一轮循环重跑当前阶段。
                preMigrationRetries++;
            }
            else
            {
                operation.Fail(execution.ErrorCode!, execution.ErrorSummary!, now);
                await SaveOperationAsync(operation, cancellationToken);
                if (operation.Phase >= OperationPhase.Migrate)
                {
                    await MarkRegistrationNotReadyAsync(context, operation, cancellationToken);
                }

                return;
            }
        }
    }

    private async Task<PhaseExecution> ExecuteCurrentPhaseAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        CancellationToken cancellationToken) =>
        operation.Phase switch
        {
            OperationPhase.Validate => await ExecuteValidateAsync(context, operation, cancellationToken),
            OperationPhase.Inspect => await ExecuteInspectAsync(context, operation, cancellationToken),
            OperationPhase.ProvisionDatabase => await ExecuteProvisionDatabaseAsync(context, operation, cancellationToken),
            OperationPhase.ProvisionRoles => await ExecuteProvisionRolesAsync(context, operation, cancellationToken),
            OperationPhase.Backup => await ExecuteBackupAsync(context, operation, cancellationToken),
            OperationPhase.Migrate => await ExecuteMigrateAsync(context, operation, cancellationToken),
            OperationPhase.Verify => await ExecuteVerifyAsync(context, operation, cancellationToken),
            _ => FatalFailure(DatabaseOrchestrationRunnerErrors.InternalFailure, "未知操作阶段。"),
        };

    // ===== 阶段执行 =====

    private async Task<PhaseExecution> ExecuteValidateAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var registration = await GetRegistrationAsync(context, operation, cancellationToken);
            if (operation.Kind == OperationKind.Plan)
            {
                // 预检迁移产物可解析(真实 inspect 在 Inspect 阶段)。
                _ = await _artifactStore.ResolveAsync(registration.MigrationArtifactId, cancellationToken);
            }
            else
            {
                await RevalidateApplyGateAsync(context, operation, registration, cancellationToken);
            }

            return Success();
        }
        catch (DatabaseOrchestrationException exception)
        {
            return FatalFailure(exception);
        }
        catch (Exception)
        {
            return TransientFailure();
        }
    }

    private async Task<PhaseExecution> ExecuteInspectAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var registration = await GetRegistrationAsync(context, operation, cancellationToken);
            var topology = _topologyProvider.GetTopology();
            var target = DatabaseTopologyResolver.Resolve(
                topology,
                operation.ServiceKey,
                ParseProvider(registration.Provider),
                registration.LogicalDatabaseName);
            context.Target = target;

            var credentials = await _credentialResolver.ResolveAsync(
                target,
                provisionRequired: operation.Kind == OperationKind.Apply,
                cancellationToken);
            context.Credentials = credentials;

            var inspection = await _inspector.InspectAsync(target, credentials, cancellationToken);
            context.Inspection = inspection;

            if (operation.Kind == OperationKind.Plan)
            {
                await BuildPlanAsync(context, operation, registration, inspection, cancellationToken);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(operation.PlanNId))
                {
                    throw new NotFoundException();
                }

                var plan = await _store.GetPlanAsync(operation.TenantNId, operation.PlanNId, cancellationToken)
                    ?? throw new NotFoundException();
                var currentFingerprint = ComputeTargetStateFingerprint(registration, plan, context.Policy!);
                if (!plan.MatchesTargetStateFingerprint(currentFingerprint))
                {
                    throw new PlanDriftException();
                }

                if (inspection.CurrentVersion is not null
                    && !string.Equals(inspection.CurrentVersion, plan.CurrentMigrationVersion, StringComparison.Ordinal))
                {
                    throw new TargetMismatchException("目标数据库当前版本与计划生成时不一致,拒绝 apply。");
                }

                context.Plan = plan;
            }

            return Success();
        }
        catch (DatabaseOrchestrationException exception)
        {
            return FatalFailure(exception);
        }
        catch (Exception)
        {
            return TransientFailure();
        }
    }

    private async Task<PhaseExecution> ExecuteProvisionDatabaseAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            if (context.Inspection is null)
            {
                return FatalFailure(DatabaseOrchestrationRunnerErrors.InternalFailure, "缺少目标检查结果。");
            }

            var inspection = context.Inspection;
            if (inspection.DatabaseExists)
            {
                return Success();
            }

            var registration = context.Registration!;
            if (!registration.AutoProvision)
            {
                return FatalFailure(
                    DatabaseOrchestrationRunnerErrors.ProvisionRequired,
                    "目标数据库不存在且注册清单未允许自动 provision。");
            }

            if (context.Credentials?.Admin is null)
            {
                return FatalFailure(DatabaseOrchestrationRunnerErrors.SecretUnavailable, "缺少 provision admin 凭据。");
            }

            _ = await _provisioner.EnsureDatabaseAsync(context.Target!, context.Credentials.Admin, cancellationToken);
            context.Inspection = new DatabaseTargetInspection(
                DatabaseExists: true,
                inspection.CurrentVersion,
                inspection.DatabaseIdentityFingerprint,
                inspection.AppliedStepIds);
            return Success();
        }
        catch (DatabaseOrchestrationException exception)
        {
            return FatalFailure(exception);
        }
        catch (Exception)
        {
            return TransientFailure();
        }
    }

    private async Task<PhaseExecution> ExecuteProvisionRolesAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            if (context.Credentials?.Admin is null)
            {
                return FatalFailure(DatabaseOrchestrationRunnerErrors.SecretUnavailable, "缺少 provision admin 凭据。");
            }

            var admin = context.Credentials.Admin;
            var roles = await _provisioner.EnsureRolesAsync(context.Target!, admin, cancellationToken);
            await _credentialSink.WriteAsync(context.Target!, roles, cancellationToken);
            context.Credentials = new DatabaseTargetCredentials(admin, roles.Migrator, roles.Runtime);
            return Success();
        }
        catch (DatabaseOrchestrationException exception)
        {
            return FatalFailure(exception);
        }
        catch (Exception)
        {
            return TransientFailure();
        }
    }

    private async Task<PhaseExecution> ExecuteBackupAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            if (context.Policy!.BackupRequired
                && !await _backupService.IsVerifiedForAsync(operation.TenantNId, context.Plan!, cancellationToken))
            {
                throw new BackupRequiredException();
            }

            return Success();
        }
        catch (DatabaseOrchestrationException exception)
        {
            return FatalFailure(exception);
        }
        catch (Exception)
        {
            return TransientFailure();
        }
    }

    private async Task<PhaseExecution> ExecuteMigrateAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var registration = context.Registration!;
            var artifact = await _artifactStore.ResolveAsync(registration.MigrationArtifactId, cancellationToken);
            var verified = await _artifactVerifier.VerifyAsync(
                artifact,
                registration.ArtifactChecksum,
                registration.ArtifactSignature,
                cancellationToken);
            if (!verified)
            {
                throw new ArtifactInvalidException();
            }

            if (context.Credentials?.Migrator is null)
            {
                return FatalFailure(DatabaseOrchestrationRunnerErrors.SecretUnavailable, "缺少 migrator 凭据。");
            }

            var migrator = context.Credentials.Migrator;
            var result = await _migrationExecutor.ApplyAsync(context.Target!, artifact, migrator, cancellationToken);
            context.Artifact = artifact;
            context.Inspection = new DatabaseTargetInspection(
                DatabaseExists: true,
                result.ResultingVersion,
                context.Inspection?.DatabaseIdentityFingerprint,
                []);
            return Success();
        }
        catch (DatabaseOrchestrationException exception)
        {
            return FatalFailure(exception);
        }
        catch (Exception)
        {
            return FatalFailure(
                DatabaseOrchestrationRunnerErrors.MigrationFailed,
                "迁移失败且未证明可安全恢复,不自动重试。");
        }
    }

    private async Task<PhaseExecution> ExecuteVerifyAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var registration = context.Registration!;
            var inspection = await _inspector.InspectAsync(context.Target!, context.Credentials!, cancellationToken);
            if (!string.Equals(inspection.CurrentVersion, operation.RequestedVersion, StringComparison.Ordinal))
            {
                throw new TargetMismatchException("迁移后目标版本与期望版本不一致。");
            }

            var identityFingerprint = ComputeDatabaseIdentityFingerprint(registration);
            var observation = DatabaseMigrationObservation.Record(
                operation.TenantNId,
                operation.EnvironmentNId,
                operation.ServiceKey,
                identityFingerprint,
                inspection.CurrentVersion!,
                registration.ArtifactChecksum,
                DateTimeOffset.UtcNow,
                operation.OperationNId,
                VerificationStatus.Verified);
            await _store.AddObservationAsync(observation, cancellationToken);
            return Success();
        }
        catch (DatabaseOrchestrationException exception)
        {
            return FatalFailure(exception);
        }
        catch (Exception)
        {
            return FatalFailure(
                DatabaseOrchestrationRunnerErrors.TargetMismatch,
                "目标验证失败,未达到期望迁移版本。");
        }
    }

    // ===== 子过程 =====

    private async Task<DatabaseRegistration> GetRegistrationAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        CancellationToken cancellationToken)
    {
        if (context.Registration is not null)
        {
            return context.Registration;
        }

        var registration = await _store.GetRegistrationAsync(
            operation.TenantNId,
            operation.EnvironmentNId,
            operation.ServiceKey,
            cancellationToken)
            ?? throw new RegistrationNotFoundException();
        context.Registration = registration;
        return registration;
    }

    private async Task RevalidateApplyGateAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        DatabaseRegistration registration,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var plan = await _store.GetPlanAsync(operation.TenantNId, operation.PlanNId!, cancellationToken)
            ?? throw new NotFoundException();
        if (plan.IsExpired(now))
        {
            throw new PlanExpiredException();
        }

        var topology = _topologyProvider.GetTopology();
        var topologyRevision = DatabaseTopologyFingerprint.ComputeTopologyRevision(topology);
        if (!string.Equals(registration.TopologyRevision, topologyRevision, StringComparison.Ordinal))
        {
            throw new TopologyDriftException();
        }

        var policy = await EnvironmentPolicyResolver.ResolveAsync(
            _store, _options.Value, operation.TenantNId, operation.EnvironmentNId, topology, cancellationToken);
        context.Policy = policy;
        var fingerprint = ComputeTargetStateFingerprint(registration, plan, policy);
        if (!plan.MatchesTargetStateFingerprint(fingerprint))
        {
            throw new PlanDriftException();
        }

        if (!string.Equals(plan.RequestedMigrationVersion, registration.MigrationVersion, StringComparison.Ordinal))
        {
            throw new TargetMismatchException("计划请求版本与注册清单当前版本不一致。");
        }

        if (policy.ApprovalRequired && !await _approvalService.IsApprovedForAsync(operation.TenantNId, plan, cancellationToken))
        {
            throw new ApprovalRequiredException();
        }

        if (policy.BackupRequired && !await _backupService.IsVerifiedForAsync(operation.TenantNId, plan, cancellationToken))
        {
            throw new BackupRequiredException();
        }

        context.Plan = plan;
    }

    private async Task BuildPlanAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        DatabaseRegistration registration,
        DatabaseTargetInspection inspection,
        CancellationToken cancellationToken)
    {
        var artifact = await _artifactStore.ResolveAsync(registration.MigrationArtifactId, cancellationToken);
        context.Artifact = artifact;

        var currentVersion = inspection.CurrentVersion ?? "0.0.0";
        var fingerprint = ComputeTargetStateFingerprint(
            registration,
            artifact: artifact,
            requestedVersion: operation.RequestedVersion,
            policy: context.Policy!);
        var destructiveDetected = artifact.Steps.Any(step => step.Destructive);
        var riskLevel = destructiveDetected ? RiskLevel.High : RiskLevel.Medium;
        var requiredPolicies = DatabasePlanRequiredPolicies.None;
        if (context.Policy!.ApprovalRequired)
        {
            requiredPolicies |= DatabasePlanRequiredPolicies.Approval;
        }

        if (context.Policy.BackupRequired)
        {
            requiredPolicies |= DatabasePlanRequiredPolicies.Backup;
        }

        var expiresOn = DateTimeOffset.UtcNow.AddSeconds(context.Policy.PlanTtlSeconds);
        var steps = artifact.Steps
            .Select(step => new DatabasePlanStep(
                step.Sequence,
                $"migrate-{step.StepId}",
                $"应用迁移步骤 {step.StepId}。",
                $"目标数据库已存在且当前版本可迁移。",
                $"目标版本达到 {artifact.Version}。",
                step.Destructive ? RiskLevel.High : RiskLevel.Medium))
            .ToList();

        var plan = DatabaseProvisionPlan.Create(
            operation.TenantNId,
            DatabaseOrchestrationInput.NewNId("PLAN"),
            operation.EnvironmentNId,
            operation.ServiceKey,
            operation.RequestedVersion,
            currentVersion,
            fingerprint,
            riskLevel,
            destructiveDetected,
            requiredPolicies,
            expiresOn,
            operation.CreatedByUserNId,
            steps);
        plan.ClearDomainEvents();
        await _store.AddPlanAsync(plan, cancellationToken);
        context.Plan = plan;
    }

    private async Task<bool> AcquireTargetLockAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var target = context.Target
            ?? throw new InvalidOperationException("获取目标锁前必须先解析目标。");
        if (context.Credentials?.Migrator is null && context.Credentials?.Admin is null)
        {
            operation.Fail(
                DatabaseOrchestrationRunnerErrors.SecretUnavailable,
                "缺少目标凭据,无法获取目标锁。",
                now);
            await SaveOperationAsync(operation, cancellationToken);
            return false;
        }

        var connection = context.Credentials.Migrator ?? context.Credentials.Admin!;
        var key = DatabaseTargetLockKey.FromTarget(operation.EnvironmentNId, target);
        var handle = await _advisoryLock.AcquireAsync(
            key,
            connection,
            TimeSpan.FromSeconds(_runnerOptions.Value.AdvisoryLockTimeoutSeconds),
            cancellationToken);
        if (handle is not null)
        {
            _lockHandle = handle;
            return true;
        }

        operation.Fail(
            DatabaseOrchestrationRunnerErrors.OperationConflict,
            "同物理目标操作冲突:获取目标 advisory lock 超时。",
            now);
        await SaveOperationAsync(operation, cancellationToken);
        return false;
    }

    private async Task MarkRegistrationNotReadyAsync(
        RunContext context,
        DatabaseProvisionOperation operation,
        CancellationToken cancellationToken)
    {
        var registration = context.Registration;
        if (registration is null || registration.Status == RegistrationStatus.NotReady)
        {
            return;
        }

        var expectedOptimisticVersion = registration.OptimisticVersion;
        var expectedConcurrencyVersion = registration.ConcurrencyVersion;
        registration.MarkNotReady();
        registration.ClearDomainEvents();
        await _store.UpdateRegistrationAsync(
            registration,
            expectedOptimisticVersion,
            expectedConcurrencyVersion,
            cancellationToken);
    }

    private async Task SaveOperationAsync(DatabaseProvisionOperation operation, CancellationToken cancellationToken)
    {
        operation.ClearDomainEvents();
        await _store.UpdateOperationAsync(
            operation,
            _persistedOptimisticVersion,
            _persistedConcurrencyVersion,
            cancellationToken);
        _persistedOptimisticVersion = operation.OptimisticVersion;
        _persistedConcurrencyVersion = operation.ConcurrencyVersion;
    }

    private static string ComputeTargetStateFingerprint(
        DatabaseRegistration registration,
        DatabaseProvisionPlan plan,
        ResolvedPolicy policy) =>
        DatabaseTopologyFingerprint.ComputeTargetStateFingerprint(
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

    private static string ComputeTargetStateFingerprint(
        DatabaseRegistration registration,
        DatabaseMigrationArtifact artifact,
        string requestedVersion,
        ResolvedPolicy policy) =>
        DatabaseTopologyFingerprint.ComputeTargetStateFingerprint(
            registration.EnvironmentNId,
            registration.ServiceKey,
            registration.Provider,
            registration.LogicalDatabaseName,
            registration.PhysicalDatabaseName,
            registration.TopologyMode,
            registration.TopologyRevision,
            artifact.Checksum,
            requestedVersion,
            registration.DesiredState.ToString(),
            policy.ApprovalRequired,
            policy.BackupRequired);

    private static string ComputeDatabaseIdentityFingerprint(DatabaseRegistration registration) =>
        DatabaseTopologyFingerprint.ComputeDatabaseIdentityFingerprint(
            registration.EnvironmentNId,
            registration.ServiceKey,
            registration.Provider,
            registration.LogicalDatabaseName,
            registration.PhysicalDatabaseName,
            registration.TopologyRevision);

    private static DatabaseProvider ParseProvider(string provider) => provider switch
    {
        "Sqlite" => DatabaseProvider.Sqlite,
        "PostgreSQL" => DatabaseProvider.PostgreSQL,
        _ => throw new ProviderUnsupportedException(provider),
    };

    private static PhaseExecution Success() => new(PhaseOutcome.Succeeded, null, null);

    private static PhaseExecution TransientFailure() =>
        new(
            PhaseOutcome.RetryableFailure,
            DatabaseOrchestrationRunnerErrors.InternalFailure,
            "目标数据库瞬时操作失败。");

    private static PhaseExecution FatalFailure(string code, string summary) =>
        new(PhaseOutcome.FatalFailure, code, summary);

    private static PhaseExecution FatalFailure(DatabaseOrchestrationException exception) =>
        new(PhaseOutcome.FatalFailure, exception.Code, exception.Message);

    private TimeSpan LeaseDuration => TimeSpan.FromSeconds(_options.Value.LeaseSeconds);

    private string InstanceId =>
        _instanceId ??= _runnerOptions.Value.InstanceId
            ?? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"[..64];

    /// <summary>当前活动操作(租约持有上下文)。</summary>
    private sealed record ActiveOperation(string TenantNId, string OperationNId, string LeaseOwner);

    /// <summary>单轮执行上下文:在 Inspect/Provision 阶段填充,供后续阶段复用,避免重复解析。</summary>
    private sealed class RunContext
    {
        public DatabaseRegistration? Registration { get; set; }

        public ResolvedDatabaseTarget? Target { get; set; }

        public DatabaseTargetCredentials? Credentials { get; set; }

        public DatabaseTargetInspection? Inspection { get; set; }

        public DatabaseProvisionPlan? Plan { get; set; }

        public ResolvedPolicy? Policy { get; set; }

        public DatabaseMigrationArtifact? Artifact { get; set; }
    }

    /// <summary>阶段执行结果。</summary>
    private sealed record PhaseExecution(PhaseOutcome Outcome, string? ErrorCode, string? ErrorSummary);

    private enum PhaseOutcome
    {
        Succeeded,
        RetryableFailure,
        FatalFailure,
    }
}
