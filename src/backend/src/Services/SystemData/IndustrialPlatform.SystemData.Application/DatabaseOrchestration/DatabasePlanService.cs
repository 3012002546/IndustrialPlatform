using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration;

/// <summary>计划用例端口。</summary>
public interface IPlanService
{
    /// <summary>入队异步计划(POST /plans)。幂等:同一 Idempotency-Key+RequestHash 返回原 Operation。</summary>
    Task<EnqueueOperationV1> EnqueuePlanAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        DatabasePlanRequestV1 request,
        string traceId,
        CancellationToken cancellationToken);

    /// <summary>v2:入队模块级异步计划(POST service-initialization/plans,按 (ServiceKey, ModuleKey) 粒度)。</summary>
    Task<EnqueueOperationV1> EnqueuePlanModuleAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        ServiceInitializationPlanRequestV2 request,
        string traceId,
        CancellationToken cancellationToken);

    /// <summary>按计划标识查询不可变计划(含 IsExpired 计算);不存在抛 404。</summary>
    Task<DatabasePlanV1> GetAsync(string tenantNId, string planNId, CancellationToken cancellationToken);

    /// <summary>分页查询计划。</summary>
    Task<DatabaseOrchestrationPageResult<DatabasePlanV1>> ListAsync(string tenantNId, int pageIndex, int pageSize, CancellationToken cancellationToken);

    /// <summary>读取当前可信环境的有效策略,与 plan/apply 门禁使用同一解析器。</summary>
    Task<EnvironmentPolicyV1> GetEffectivePolicyAsync(string tenantNId, CancellationToken cancellationToken);
}

/// <summary>
/// 计划用例(05 方案 §9.2 POST /plans 入队)。只创建 Queued 计划操作与阶段步骤,
/// 真实计划由 SD-003 Runner 消费后生成;环境策略解析无记录时按环境回退默认。
/// </summary>
public sealed class DatabasePlanService : IPlanService
{
    private readonly IDatabaseOrchestrationStore _store;
    private readonly IDatabaseTopologyProvider _topologyProvider;
    private readonly IOptions<DatabaseOrchestrationOptions> _options;

    public DatabasePlanService(
        IDatabaseOrchestrationStore store,
        IDatabaseTopologyProvider topologyProvider,
        IOptions<DatabaseOrchestrationOptions> options)
    {
        _store = store;
        _topologyProvider = topologyProvider;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<EnqueueOperationV1> EnqueuePlanAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        DatabasePlanRequestV1 request,
        string traceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var topology = _topologyProvider.GetTopology();
        var environmentNId = topology.EnvironmentName;
        var serviceKey = DatabaseOrchestrationInput.Require(request.ServiceKey, "服务键不能为空。");
        var requestedVersion = DatabaseOrchestrationInput.Require(request.RequestedVersion, "请求版本不能为空。");
        var idempotencyKeyTrimmed = DatabaseOrchestrationInput.Require(idempotencyKey, "幂等键不能为空。");

        var registration = await _store.GetRegistrationAsync(tenantNId, environmentNId, serviceKey, cancellationToken)
            ?? throw new RegistrationNotFoundException();

        var requestHash = RequestHasher.HashPlanRequest(serviceKey, requestedVersion, request.DesiredState);
        var existing = await _store.FindOperationByIdempotencyKeyAsync(tenantNId, idempotencyKeyTrimmed, cancellationToken);
        if (existing is not null)
        {
            if (existing.MatchesRequestHash(requestHash))
            {
                return ToEnqueueV1(existing);
            }

            throw new OperationConflictException("同一幂等键已用于不同请求。");
        }

        var policy = await EnvironmentPolicyResolver.ResolveAsync(
            _store, _options.Value, tenantNId, environmentNId, topology, cancellationToken);
        var timeoutOn = DateTimeOffset.UtcNow.AddSeconds(policy.PlanTimeoutSeconds);
        var operation = DatabaseProvisionOperation.Enqueue(
            tenantNId,
            DatabaseOrchestrationInput.NewNId("OP"),
            OperationKind.Plan,
            environmentNId,
            serviceKey,
            planNId: null,
            requestedVersion,
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
    public async Task<EnqueueOperationV1> EnqueuePlanModuleAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        ServiceInitializationPlanRequestV2 request,
        string traceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var topology = _topologyProvider.GetTopology();
        var environmentNId = topology.EnvironmentName;
        var serviceKey = DatabaseOrchestrationInput.Require(request.ServiceKey, "服务键不能为空。");
        var moduleKey = DatabaseOrchestrationInput.Require(request.ModuleKey, "模块标识不能为空。");
        var requestedVersion = DatabaseOrchestrationInput.Require(request.RequestedVersion, "请求版本不能为空。");
        var idempotencyKeyTrimmed = DatabaseOrchestrationInput.Require(idempotencyKey, "幂等键不能为空。");

        var registration = await _store.GetRegistrationAsync(tenantNId, environmentNId, serviceKey, moduleKey, cancellationToken)
            ?? throw new RegistrationNotFoundException();

        // 计划层 EnvironmentSample 门禁(蓝图 §12.3 第二层)。
        var environmentKind = EnvironmentPolicyResolver.ParseEnvironmentKind(environmentNId);
        if (environmentKind is DatabaseEnvironmentKind.Staging or DatabaseEnvironmentKind.Production
            && registration.SeedSets.Any(seed => seed.SeedClass == SeedClass.EnvironmentSample))
        {
            throw new SampleEnvironmentForbiddenException();
        }

        var requestHash = RequestHasher.HashPlanRequestV2(serviceKey, moduleKey, requestedVersion, request.DesiredState);
        var existing = await _store.FindOperationByIdempotencyKeyAsync(tenantNId, idempotencyKeyTrimmed, cancellationToken);
        if (existing is not null)
        {
            if (existing.MatchesRequestHash(requestHash))
            {
                return ToEnqueueV1(existing);
            }

            throw new OperationConflictException("同一幂等键已用于不同请求。");
        }

        var policy = await EnvironmentPolicyResolver.ResolveAsync(
            _store, _options.Value, tenantNId, environmentNId, topology, cancellationToken);
        var timeoutOn = DateTimeOffset.UtcNow.AddSeconds(policy.PlanTimeoutSeconds);
        var operation = DatabaseProvisionOperation.Enqueue(
            tenantNId,
            DatabaseOrchestrationInput.NewNId("OP"),
            OperationKind.Plan,
            environmentNId,
            serviceKey,
            planNId: null,
            requestedVersion,
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
    public async Task<DatabasePlanV1> GetAsync(string tenantNId, string planNId, CancellationToken cancellationToken)
    {
        var planNIdTrimmed = DatabaseOrchestrationInput.Require(planNId, "计划标识不能为空。");
        var plan = await _store.GetPlanAsync(tenantNId, planNIdTrimmed, cancellationToken)
            ?? throw new NotFoundException();
        return ToPlanV1(plan, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async Task<DatabaseOrchestrationPageResult<DatabasePlanV1>> ListAsync(
        string tenantNId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var filter = new PlanListFilter(tenantNId, pageIndex, pageSize);
        var page = await _store.QueryPlansAsync(filter, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return new DatabaseOrchestrationPageResult<DatabasePlanV1>(
            page.Items.Select(plan => ToPlanV1(plan, now)).ToList(),
            page.Total,
            page.PageIndex,
            page.PageSize);
    }

    /// <inheritdoc />
    public async Task<EnvironmentPolicyV1> GetEffectivePolicyAsync(
        string tenantNId,
        CancellationToken cancellationToken)
    {
        var topology = _topologyProvider.GetTopology();
        var policy = await EnvironmentPolicyResolver.ResolveAsync(
            _store,
            _options.Value,
            tenantNId,
            topology.EnvironmentName,
            topology,
            cancellationToken);
        return new EnvironmentPolicyV1
        {
            TenantNId = tenantNId,
            EnvironmentNId = topology.EnvironmentName,
            EnvironmentKind = policy.EnvironmentKind.ToString(),
            ApprovalRequired = policy.ApprovalRequired,
            BackupRequired = policy.BackupRequired,
            PlanTtlSeconds = policy.PlanTtlSeconds,
            PlanTimeoutSeconds = policy.PlanTimeoutSeconds,
            ApplyTimeoutSeconds = policy.ApplyTimeoutSeconds,
            MaxPreMigrationRetries = policy.MaxPreMigrationRetries,
            PolicyRevision = policy.PolicyRevision,
            InitializationPolicy = policy.InitializationPolicy.ToString(),
            IsExplicit = policy.IsExplicit,
        };
    }

    private static DatabasePlanV1 ToPlanV1(DatabaseProvisionPlan plan, DateTimeOffset now) => new()
    {
        TenantNId = plan.TenantNId,
        PlanNId = plan.PlanNId,
        EnvironmentNId = plan.EnvironmentNId,
        ServiceKey = plan.ServiceKey,
        ModuleKey = plan.ModuleKey,
        RequestedMigrationVersion = plan.RequestedMigrationVersion,
        CurrentMigrationVersion = plan.CurrentMigrationVersion,
        TargetStateFingerprint = plan.TargetStateFingerprint,
        PlanChecksum = plan.PlanChecksum,
        RiskLevel = plan.RiskLevel.ToString(),
        DestructiveChangeDetected = plan.DestructiveChangeDetected,
        RequiredPolicies = plan.RequiredPolicies.ToString(),
        ExpiresOn = plan.ExpiresOn,
        IsExpired = plan.IsExpired(now),
        CreatedByUserNId = plan.CreatedByUserNId,
        CreatedOn = plan.CreatedOn,
        Steps = plan.Steps.Select(ToStepV1).ToList(),
    };

    private static DatabasePlanStepV1 ToStepV1(DatabasePlanStep step) => new()
    {
        Sequence = step.Sequence,
        StepKind = step.StepKind,
        RiskLevel = step.RiskLevel.ToString(),
        InputSummary = step.InputSummary,
        PreconditionSummary = step.PreconditionSummary,
        PostconditionSummary = step.PostconditionSummary,
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
