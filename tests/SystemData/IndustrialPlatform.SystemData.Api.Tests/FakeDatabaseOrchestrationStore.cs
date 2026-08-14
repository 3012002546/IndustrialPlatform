using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Api.Tests;

/// <summary>
/// 数据库编排存储端口的进程内假实现(API 端点测试,镜像 Application.Tests 同名假存储)。
/// 以字典模拟九张控制面表;Add 遇唯一键冲突、Update 遇双版本不匹配抛
/// <see cref="ConcurrencyException"/>(对齐 SqlSugar 语义),由应用层 WriteGuard 映射为 SD_DB_OPERATION_CONFLICT。
/// </summary>
public sealed class FakeDatabaseOrchestrationStore : IDatabaseOrchestrationStore
{
    private readonly Dictionary<string, DatabaseEnvironmentPolicy> _policies = [];
    private readonly Dictionary<string, DatabaseRegistration> _registrations = [];
    private readonly Dictionary<string, DatabaseProvisionPlan> _plans = [];
    private readonly Dictionary<string, DatabaseApproval> _approvals = [];
    private readonly Dictionary<string, DatabaseBackupEvidence> _evidences = [];
    private readonly Dictionary<string, DatabaseProvisionOperation> _operations = [];
    private readonly List<DatabaseMigrationObservation> _observations = [];

    private readonly Dictionary<string, (long Optimistic, Guid Concurrency)> _registrationVersions = [];
    private readonly Dictionary<string, (long Optimistic, Guid Concurrency)> _evidenceVersions = [];
    private readonly Dictionary<string, (long Optimistic, Guid Concurrency)> _operationVersions = [];

    // ===== 环境策略 =====

    /// <inheritdoc />
    public Task<DatabaseEnvironmentPolicy?> GetEnvironmentPolicyAsync(string tenantNId, string environmentNId, CancellationToken cancellationToken) =>
        Task.FromResult(_policies.TryGetValue(PolicyKey(tenantNId, environmentNId), out var policy) ? policy : null);

    /// <inheritdoc />
    public Task AddEnvironmentPolicyAsync(DatabaseEnvironmentPolicy policy, CancellationToken cancellationToken)
    {
        var key = PolicyKey(policy.TenantNId, policy.EnvironmentNId);
        if (_policies.ContainsKey(key))
        {
            throw new ConcurrencyException("环境策略已存在。");
        }

        _policies[key] = policy;
        return Task.CompletedTask;
    }

    // ===== 注册清单 =====

    /// <inheritdoc />
    public Task<DatabaseRegistration?> GetRegistrationAsync(string tenantNId, string environmentNId, string serviceKey, CancellationToken cancellationToken) =>
        Task.FromResult(_registrations.TryGetValue(RegistrationKey(tenantNId, environmentNId, serviceKey), out var registration) ? registration : null);

    /// <inheritdoc />
    public Task AddRegistrationAsync(DatabaseRegistration registration, CancellationToken cancellationToken)
    {
        var key = RegistrationKey(registration.TenantNId, registration.EnvironmentNId, registration.ServiceKey);
        if (_registrations.ContainsKey(key))
        {
            throw new ConcurrencyException("注册清单已存在。");
        }

        _registrations[key] = registration;
        _registrationVersions[key] = (registration.OptimisticVersion, registration.ConcurrencyVersion);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateRegistrationAsync(DatabaseRegistration registration, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken)
    {
        var key = RegistrationKey(registration.TenantNId, registration.EnvironmentNId, registration.ServiceKey);
        EnsureVersion(key, _registrationVersions, expectedOptimisticVersion, expectedConcurrencyVersion);
        _registrations[key] = registration;
        _registrationVersions[key] = (registration.OptimisticVersion, registration.ConcurrencyVersion);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DatabaseOrchestrationPageResult<DatabaseRegistration>> QueryRegistrationsAsync(RegistrationListFilter filter, CancellationToken cancellationToken)
    {
        var items = _registrations.Values
            .Where(registration => registration.TenantNId == filter.TenantNId)
            .Where(registration => filter.ServiceKey is null || registration.ServiceKey.Contains(filter.ServiceKey, StringComparison.Ordinal))
            .OrderByDescending(registration => registration.CreatedOn)
            .ToList();
        return Task.FromResult(Page(filter.PageIndex, filter.PageSize, items));
    }

    // ===== 不可变计划 =====

    /// <inheritdoc />
    public Task<DatabaseProvisionPlan?> GetPlanAsync(string tenantNId, string planNId, CancellationToken cancellationToken) =>
        Task.FromResult(_plans.TryGetValue(PlanKey(tenantNId, planNId), out var plan) ? plan : null);

    /// <inheritdoc />
    public Task AddPlanAsync(DatabaseProvisionPlan plan, CancellationToken cancellationToken)
    {
        var key = PlanKey(plan.TenantNId, plan.PlanNId);
        if (_plans.ContainsKey(key))
        {
            throw new ConcurrencyException("计划已存在。");
        }

        _plans[key] = plan;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DatabaseOrchestrationPageResult<DatabaseProvisionPlan>> QueryPlansAsync(PlanListFilter filter, CancellationToken cancellationToken)
    {
        var items = _plans.Values
            .Where(plan => plan.TenantNId == filter.TenantNId)
            .OrderByDescending(plan => plan.CreatedOn)
            .ToList();
        return Task.FromResult(Page(filter.PageIndex, filter.PageSize, items));
    }

    // ===== 审批 =====

    /// <inheritdoc />
    public Task<DatabaseApproval?> GetApprovalAsync(string tenantNId, string approvalNId, CancellationToken cancellationToken) =>
        Task.FromResult(_approvals.TryGetValue(ApprovalKey(tenantNId, approvalNId), out var approval) ? approval : null);

    /// <inheritdoc />
    public Task<IReadOnlyList<DatabaseApproval>> GetApprovalsForPlanAsync(string tenantNId, string planNId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DatabaseApproval>>(
            _approvals.Values
                .Where(approval => approval.TenantNId == tenantNId && approval.PlanNId == planNId)
                .OrderByDescending(approval => approval.ApprovedOn)
                .ToList());

    /// <inheritdoc />
    public Task AddApprovalAsync(DatabaseApproval approval, CancellationToken cancellationToken)
    {
        var key = ApprovalKey(approval.TenantNId, approval.ApprovalNId);
        if (_approvals.ContainsKey(key))
        {
            throw new ConcurrencyException("审批记录已存在。");
        }

        _approvals[key] = approval;
        return Task.CompletedTask;
    }

    // ===== 备份证据 =====

    /// <inheritdoc />
    public Task<DatabaseBackupEvidence?> GetBackupEvidenceAsync(string tenantNId, string evidenceNId, CancellationToken cancellationToken) =>
        Task.FromResult(_evidences.TryGetValue(EvidenceKey(tenantNId, evidenceNId), out var evidence) ? evidence : null);

    /// <inheritdoc />
    public Task<IReadOnlyList<DatabaseBackupEvidence>> GetBackupEvidenceForPlanAsync(string tenantNId, string planNId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DatabaseBackupEvidence>>(
            _evidences.Values
                .Where(evidence => evidence.TenantNId == tenantNId && evidence.PlanNId == planNId)
                .OrderByDescending(evidence => evidence.CapturedOn)
                .ToList());

    /// <inheritdoc />
    public Task AddBackupEvidenceAsync(DatabaseBackupEvidence evidence, CancellationToken cancellationToken)
    {
        var key = EvidenceKey(evidence.TenantNId, evidence.EvidenceNId);
        if (_evidences.ContainsKey(key))
        {
            throw new ConcurrencyException("备份证据已存在。");
        }

        _evidences[key] = evidence;
        _evidenceVersions[key] = (evidence.OptimisticVersion, evidence.ConcurrencyVersion);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateBackupEvidenceAsync(DatabaseBackupEvidence evidence, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken)
    {
        var key = EvidenceKey(evidence.TenantNId, evidence.EvidenceNId);
        EnsureVersion(key, _evidenceVersions, expectedOptimisticVersion, expectedConcurrencyVersion);
        _evidences[key] = evidence;
        _evidenceVersions[key] = (evidence.OptimisticVersion, evidence.ConcurrencyVersion);
        return Task.CompletedTask;
    }

    // ===== Operation =====

    /// <inheritdoc />
    public Task<DatabaseProvisionOperation?> GetOperationAsync(string tenantNId, string operationNId, CancellationToken cancellationToken) =>
        Task.FromResult(_operations.TryGetValue(OperationKey(tenantNId, operationNId), out var operation) ? operation : null);

    /// <inheritdoc />
    public Task<DatabaseProvisionOperation?> FindOperationByIdempotencyKeyAsync(string tenantNId, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(_operations.Values.FirstOrDefault(operation =>
            operation.TenantNId == tenantNId && string.Equals(operation.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)));

    /// <inheritdoc />
    public Task AddOperationAsync(DatabaseProvisionOperation operation, CancellationToken cancellationToken)
    {
        var key = OperationKey(operation.TenantNId, operation.OperationNId);
        if (_operations.ContainsKey(key))
        {
            throw new ConcurrencyException("操作已存在。");
        }

        _operations[key] = operation;
        _operationVersions[key] = (operation.OptimisticVersion, operation.ConcurrencyVersion);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateOperationAsync(DatabaseProvisionOperation operation, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken)
    {
        var key = OperationKey(operation.TenantNId, operation.OperationNId);
        EnsureVersion(key, _operationVersions, expectedOptimisticVersion, expectedConcurrencyVersion);
        _operations[key] = operation;
        _operationVersions[key] = (operation.OptimisticVersion, operation.ConcurrencyVersion);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DatabaseOrchestrationPageResult<DatabaseProvisionOperation>> QueryOperationsAsync(OperationListFilter filter, CancellationToken cancellationToken)
    {
        var items = _operations.Values
            .Where(operation => operation.TenantNId == filter.TenantNId)
            .Where(operation => filter.Kind is null || operation.Kind == filter.Kind)
            .Where(operation => filter.Status is null || operation.Status == filter.Status)
            .OrderByDescending(operation => operation.QueuedOn)
            .ToList();
        return Task.FromResult(Page(filter.PageIndex, filter.PageSize, items));
    }

    // ===== 迁移观察 =====

    /// <inheritdoc />
    public Task<DatabaseProvisionOperation?> ClaimNextOperationAsync(
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var candidate = _operations.Values
            .Where(operation => operation.Status == OperationStatus.Queued && operation.TimeoutOn > now)
            .OrderBy(operation => operation.QueuedOn)
            .FirstOrDefault();
        if (candidate is null)
        {
            return Task.FromResult<DatabaseProvisionOperation?>(null);
        }

        // 对齐真实 store:先捕获 Start 前的双版本,Start 推进后以该版本原子写回。
        var key = OperationKey(candidate.TenantNId, candidate.OperationNId);
        var expectedOptimistic = candidate.OptimisticVersion;
        var expectedConcurrency = candidate.ConcurrencyVersion;
        candidate.Start(leaseOwner, now, leaseDuration);
        candidate.ClearDomainEvents();
        _operations[key] = candidate;
        _operationVersions[key] = (candidate.OptimisticVersion, candidate.ConcurrencyVersion);
        return Task.FromResult<DatabaseProvisionOperation?>(candidate);
    }

    /// <inheritdoc />
    public Task<DatabaseMigrationObservation?> GetLatestObservationAsync(string tenantNId, string environmentNId, string serviceKey, CancellationToken cancellationToken) =>
        Task.FromResult(_observations
            .Where(observation =>
                observation.TenantNId == tenantNId
                && observation.EnvironmentNId == environmentNId
                && observation.ServiceKey == serviceKey)
            .OrderByDescending(observation => observation.ObservedOn)
            .FirstOrDefault());

    /// <inheritdoc />
    public Task AddObservationAsync(DatabaseMigrationObservation observation, CancellationToken cancellationToken)
    {
        _observations.Add(observation);
        return Task.CompletedTask;
    }

    // ===== 内部助手 =====

    private static void EnsureVersion(
        string key,
        Dictionary<string, (long Optimistic, Guid Concurrency)> versions,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion)
    {
        if (!versions.TryGetValue(key, out var version)
            || version.Optimistic != expectedOptimisticVersion
            || version.Concurrency != expectedConcurrencyVersion)
        {
            throw new ConcurrencyException("版本不匹配。");
        }
    }

    private static DatabaseOrchestrationPageResult<T> Page<T>(int pageIndex, int pageSize, List<T> ordered)
    {
        var items = ordered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
        return new DatabaseOrchestrationPageResult<T>(items, ordered.Count, pageIndex, pageSize);
    }

    private static string PolicyKey(string tenantNId, string environmentNId) => $"{tenantNId}|{environmentNId}";

    private static string RegistrationKey(string tenantNId, string environmentNId, string serviceKey) => $"{tenantNId}|{environmentNId}|{serviceKey}";

    private static string PlanKey(string tenantNId, string planNId) => $"{tenantNId}|{planNId}";

    private static string ApprovalKey(string tenantNId, string approvalNId) => $"{tenantNId}|{approvalNId}";

    private static string EvidenceKey(string tenantNId, string evidenceNId) => $"{tenantNId}|{evidenceNId}";

    private static string OperationKey(string tenantNId, string operationNId) => $"{tenantNId}|{operationNId}";
}

/// <summary>提供固定 <see cref="DatabaseTopology"/> 的测试替身。</summary>
internal sealed class FakeTopologyProvider : IDatabaseTopologyProvider
{
    private readonly DatabaseTopology _topology;

    public FakeTopologyProvider(DatabaseTopology topology) => _topology = topology;

    public DatabaseTopology GetTopology() => _topology;
}
