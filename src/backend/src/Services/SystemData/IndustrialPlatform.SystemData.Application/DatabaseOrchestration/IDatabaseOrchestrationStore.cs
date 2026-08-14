using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration;

/// <summary>
/// 数据库编排持久化端口(05 方案 §8.1 九张控制面表)。
/// 组合领域表模型实现;所有按业务标识的查询返回 <c>null</c> 时由应用层映射为 404;
/// 写操作(Add/Update)在唯一键冲突或双版本不匹配时由实现抛
/// <see cref="SharedKernel.Exceptions.ConcurrencyException"/>,应用层映射为 SD_DB_OPERATION_CONFLICT。
/// </summary>
public interface IDatabaseOrchestrationStore
{
    // ===== 环境策略 =====

    /// <summary>按 (TenantNId, EnvironmentNId) 查询环境策略;不存在返回 <c>null</c>。</summary>
    Task<DatabaseEnvironmentPolicy?> GetEnvironmentPolicyAsync(string tenantNId, string environmentNId, CancellationToken cancellationToken);

    /// <summary>新增环境策略。</summary>
    Task AddEnvironmentPolicyAsync(DatabaseEnvironmentPolicy policy, CancellationToken cancellationToken);

    // ===== 注册清单 =====

    /// <summary>按 (TenantNId, EnvironmentNId, ServiceKey) 查询注册清单;不存在返回 <c>null</c>。</summary>
    Task<DatabaseRegistration?> GetRegistrationAsync(string tenantNId, string environmentNId, string serviceKey, CancellationToken cancellationToken);

    /// <summary>新增注册清单(唯一键冲突抛并发异常)。</summary>
    Task AddRegistrationAsync(DatabaseRegistration registration, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新注册清单;版本不匹配抛并发异常。</summary>
    Task UpdateRegistrationAsync(DatabaseRegistration registration, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken);

    /// <summary>分页查询注册清单(含 ServiceKey 过滤),按创建时间倒序。</summary>
    Task<DatabaseOrchestrationPageResult<DatabaseRegistration>> QueryRegistrationsAsync(RegistrationListFilter filter, CancellationToken cancellationToken);

    // ===== 不可变计划 =====

    /// <summary>按 (TenantNId, PlanNId) 查询计划(含步骤);不存在返回 <c>null</c>。</summary>
    Task<DatabaseProvisionPlan?> GetPlanAsync(string tenantNId, string planNId, CancellationToken cancellationToken);

    /// <summary>新增计划(PlanChecksum 唯一冲突抛并发异常)。</summary>
    Task AddPlanAsync(DatabaseProvisionPlan plan, CancellationToken cancellationToken);

    /// <summary>分页查询计划(含步骤),按创建时间倒序。</summary>
    Task<DatabaseOrchestrationPageResult<DatabaseProvisionPlan>> QueryPlansAsync(PlanListFilter filter, CancellationToken cancellationToken);

    // ===== 审批 =====

    /// <summary>按 (TenantNId, ApprovalNId) 查询审批;不存在返回 <c>null</c>。</summary>
    Task<DatabaseApproval?> GetApprovalAsync(string tenantNId, string approvalNId, CancellationToken cancellationToken);

    /// <summary>查询计划全部审批(按审批时间倒序)。</summary>
    Task<IReadOnlyList<DatabaseApproval>> GetApprovalsForPlanAsync(string tenantNId, string planNId, CancellationToken cancellationToken);

    /// <summary>新增审批(只追加)。</summary>
    Task AddApprovalAsync(DatabaseApproval approval, CancellationToken cancellationToken);

    // ===== 备份证据 =====

    /// <summary>按 (TenantNId, EvidenceNId) 查询备份证据;不存在返回 <c>null</c>。</summary>
    Task<DatabaseBackupEvidence?> GetBackupEvidenceAsync(string tenantNId, string evidenceNId, CancellationToken cancellationToken);

    /// <summary>查询计划全部备份证据(按捕获时间倒序)。</summary>
    Task<IReadOnlyList<DatabaseBackupEvidence>> GetBackupEvidenceForPlanAsync(string tenantNId, string planNId, CancellationToken cancellationToken);

    /// <summary>新增备份证据。</summary>
    Task AddBackupEvidenceAsync(DatabaseBackupEvidence evidence, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新备份证据(验证转换);版本不匹配抛并发异常。</summary>
    Task UpdateBackupEvidenceAsync(DatabaseBackupEvidence evidence, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken);

    // ===== Operation =====

    /// <summary>按 (TenantNId, OperationNId) 查询操作(含步骤);不存在返回 <c>null</c>。</summary>
    Task<DatabaseProvisionOperation?> GetOperationAsync(string tenantNId, string operationNId, CancellationToken cancellationToken);

    /// <summary>按 (TenantNId, IdempotencyKey) 查询已入队操作(幂等重放依据);不存在返回 <c>null</c>。</summary>
    Task<DatabaseProvisionOperation?> FindOperationByIdempotencyKeyAsync(string tenantNId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>新增操作(幂等键唯一冲突抛并发异常)。</summary>
    Task AddOperationAsync(DatabaseProvisionOperation operation, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新操作(状态机转换);版本不匹配抛并发异常。</summary>
    Task UpdateOperationAsync(DatabaseProvisionOperation operation, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken);

    /// <summary>分页查询操作(含步骤),按入队时间倒序。</summary>
    Task<DatabaseOrchestrationPageResult<DatabaseProvisionOperation>> QueryOperationsAsync(OperationListFilter filter, CancellationToken cancellationToken);

    // ===== 迁移观察 =====

    /// <summary>查询某数据库身份最近一次观察;不存在返回 <c>null</c>。</summary>
    Task<DatabaseMigrationObservation?> GetLatestObservationAsync(string tenantNId, string environmentNId, string serviceKey, CancellationToken cancellationToken);

    /// <summary>新增迁移观察(只追加)。</summary>
    Task AddObservationAsync(DatabaseMigrationObservation observation, CancellationToken cancellationToken);
}

/// <summary>
/// 数据库编排分页结果(应用层承载,避免 Application 反向引用 BuildingBlocks Web 的 PageResult)。
/// Api 控制器映射为统一 PageResult 信封。
/// </summary>
public sealed record DatabaseOrchestrationPageResult<T>(IReadOnlyList<T> Items, long Total, int PageIndex, int PageSize);

/// <summary>注册清单列表过滤。租户隔离在存储层实施;ServiceKey 为包含匹配。</summary>
public sealed record RegistrationListFilter(string TenantNId, string? ServiceKey, int PageIndex, int PageSize);

/// <summary>计划列表过滤。</summary>
public sealed record PlanListFilter(string TenantNId, int PageIndex, int PageSize);

/// <summary>操作列表过滤。Kind/Status 可选精确匹配。</summary>
public sealed record OperationListFilter(string TenantNId, OperationKind? Kind, OperationStatus? Status, int PageIndex, int PageSize);
