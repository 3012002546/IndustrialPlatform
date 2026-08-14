using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Infrastructure.Persistence;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Exceptions;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration;

/// <summary>
/// 数据库编排持久化端口实现(05 方案 §8.1 九张控制面表)。
/// 自定义仓储风格:软删除过滤(自身 + 父引用影子列)、唯一键冲突映射并发异常、
/// 双版本原子更新影响行数非 1 抛并发异常、父子聚合(计划/操作)步骤批量装载与事务内原子写入。
/// 跨服务计划/审批/备份证据只按 PlanNId 引用,不建立数据库外键(方案 §8.1)。
/// </summary>
public sealed class DatabaseOrchestrationStore : IDatabaseOrchestrationStore
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化编排存储。</summary>
    public DatabaseOrchestrationStore(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    // ===== 环境策略 =====

    /// <inheritdoc />
    public async Task<DatabaseEnvironmentPolicy?> GetEnvironmentPolicyAsync(
        string tenantNId,
        string environmentNId,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<DatabaseEnvironmentPolicyTable>()
            .Where(t => t.TenantNId == tenantNId && t.EnvironmentNId == environmentNId && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToEnvironmentPolicy(row);
    }

    /// <inheritdoc />
    public async Task AddEnvironmentPolicyAsync(
        DatabaseEnvironmentPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        await InsertWithConflictGuardAsync(TableMapper.ToTable(policy), cancellationToken);
    }

    // ===== 注册清单 =====

    /// <inheritdoc />
    public async Task<DatabaseRegistration?> GetRegistrationAsync(
        string tenantNId,
        string environmentNId,
        string serviceKey,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<DatabaseRegistrationTable>()
            .Where(t => t.TenantNId == tenantNId
                && t.EnvironmentNId == environmentNId
                && t.ServiceKey == serviceKey
                && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToRegistration(row);
    }

    /// <inheritdoc />
    public async Task AddRegistrationAsync(
        DatabaseRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        await InsertWithConflictGuardAsync(TableMapper.ToTable(registration), cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateRegistrationAsync(
        DatabaseRegistration registration,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var affected = await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(registration))
            .Where(t => t.Id == registration.Id
                && !t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);
        EnsureSingleRowAffected(affected, registration.EntityType, "更新");
    }

    /// <inheritdoc />
    public async Task<DatabaseOrchestrationPageResult<DatabaseRegistration>> QueryRegistrationsAsync(
        RegistrationListFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _dbContext.SqlSugar.Queryable<DatabaseRegistrationTable>()
            .Where(t => t.TenantNId == filter.TenantNId && !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.ServiceKey))
        {
            query = query.Where(t => t.ServiceKey.Contains(filter.ServiceKey.Trim()));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(t => t.CreatedOn, OrderByType.Desc)
            .Skip((filter.PageIndex - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new DatabaseOrchestrationPageResult<DatabaseRegistration>(
            rows.Select(TableMapper.ToRegistration).ToList(),
            total,
            filter.PageIndex,
            filter.PageSize);
    }

    // ===== 不可变计划 =====

    /// <inheritdoc />
    public async Task<DatabaseProvisionPlan?> GetPlanAsync(
        string tenantNId,
        string planNId,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<DatabaseProvisionPlanTable>()
            .Where(t => t.TenantNId == tenantNId && t.PlanNId == planNId && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var steps = await LoadPlanStepsAsync(row.Id, cancellationToken);
        return TableMapper.ToPlan(row, steps);
    }

    /// <inheritdoc />
    public async Task AddPlanAsync(DatabaseProvisionPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await InsertWithConflictGuardAsync(TableMapper.ToTable(plan), cancellationToken);
            foreach (var step in plan.Steps.OrderBy(s => s.Sequence))
            {
                await InsertWithConflictGuardAsync(
                    TableMapper.ToTable(step, plan.Id, plan.IsDeleted),
                    cancellationToken);
            }

            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<DatabaseOrchestrationPageResult<DatabaseProvisionPlan>> QueryPlansAsync(
        PlanListFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _dbContext.SqlSugar.Queryable<DatabaseProvisionPlanTable>()
            .Where(t => t.TenantNId == filter.TenantNId && !t.IsDeleted);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(t => t.CreatedOn, OrderByType.Desc)
            .Skip((filter.PageIndex - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var stepsByPlan = await LoadPlanStepsMapAsync(rows.Select(r => r.Id).ToArray(), cancellationToken);
        var items = rows
            .Select(row => TableMapper.ToPlan(row, stepsByPlan.TryGetValue(row.Id, out var steps) ? steps : []))
            .ToList();

        return new DatabaseOrchestrationPageResult<DatabaseProvisionPlan>(
            items,
            total,
            filter.PageIndex,
            filter.PageSize);
    }

    // ===== 审批 =====

    /// <inheritdoc />
    public async Task<DatabaseApproval?> GetApprovalAsync(
        string tenantNId,
        string approvalNId,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<DatabaseApprovalTable>()
            .Where(t => t.TenantNId == tenantNId && t.ApprovalNId == approvalNId && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToApproval(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DatabaseApproval>> GetApprovalsForPlanAsync(
        string tenantNId,
        string planNId,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<DatabaseApprovalTable>()
            .Where(t => t.TenantNId == tenantNId && t.PlanNId == planNId && !t.IsDeleted)
            .OrderBy(t => t.ApprovedOn, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        return rows.Select(TableMapper.ToApproval).ToList();
    }

    /// <inheritdoc />
    public async Task AddApprovalAsync(DatabaseApproval approval, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        await InsertWithConflictGuardAsync(TableMapper.ToTable(approval), cancellationToken);
    }

    // ===== 备份证据 =====

    /// <inheritdoc />
    public async Task<DatabaseBackupEvidence?> GetBackupEvidenceAsync(
        string tenantNId,
        string evidenceNId,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<DatabaseBackupEvidenceTable>()
            .Where(t => t.TenantNId == tenantNId && t.EvidenceNId == evidenceNId && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToBackupEvidence(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DatabaseBackupEvidence>> GetBackupEvidenceForPlanAsync(
        string tenantNId,
        string planNId,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<DatabaseBackupEvidenceTable>()
            .Where(t => t.TenantNId == tenantNId && t.PlanNId == planNId && !t.IsDeleted)
            .OrderBy(t => t.CapturedOn, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        return rows.Select(TableMapper.ToBackupEvidence).ToList();
    }

    /// <inheritdoc />
    public async Task AddBackupEvidenceAsync(
        DatabaseBackupEvidence evidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        await InsertWithConflictGuardAsync(TableMapper.ToTable(evidence), cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateBackupEvidenceAsync(
        DatabaseBackupEvidence evidence,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var affected = await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(evidence))
            .Where(t => t.Id == evidence.Id
                && !t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);
        EnsureSingleRowAffected(affected, evidence.EntityType, "更新");
    }

    // ===== Operation =====

    /// <inheritdoc />
    public async Task<DatabaseProvisionOperation?> GetOperationAsync(
        string tenantNId,
        string operationNId,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<DatabaseProvisionOperationTable>()
            .Where(t => t.TenantNId == tenantNId && t.OperationNId == operationNId && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var steps = await LoadOperationStepsAsync(row.Id, cancellationToken);
        return TableMapper.ToOperation(row, steps);
    }

    /// <inheritdoc />
    public async Task<DatabaseProvisionOperation?> FindOperationByIdempotencyKeyAsync(
        string tenantNId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<DatabaseProvisionOperationTable>()
            .Where(t => t.TenantNId == tenantNId && t.IdempotencyKey == idempotencyKey && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var steps = await LoadOperationStepsAsync(row.Id, cancellationToken);
        return TableMapper.ToOperation(row, steps);
    }

    /// <inheritdoc />
    public async Task AddOperationAsync(DatabaseProvisionOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await InsertWithConflictGuardAsync(TableMapper.ToTable(operation), cancellationToken);
            foreach (var step in operation.Steps.OrderBy(s => s.Sequence))
            {
                await InsertWithConflictGuardAsync(
                    TableMapper.ToTable(step, operation.Id, operation.IsDeleted),
                    cancellationToken);
            }

            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateOperationAsync(
        DatabaseProvisionOperation operation,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            var affected = await sugar.Updateable(TableMapper.ToTable(operation))
                .Where(t => t.Id == operation.Id
                    && !t.IsDeleted
                    && t.OptimisticVersion == expectedOptimisticVersion
                    && t.ConcurrencyVersion == expectedConcurrencyVersion)
                .ExecuteCommandAsync(cancellationToken);
            EnsureSingleRowAffected(affected, operation.EntityType, "更新");

            foreach (var step in operation.Steps)
            {
                await sugar.Updateable(TableMapper.ToTable(step, operation.Id, operation.IsDeleted))
                    .Where(t => t.Id == step.Id
                        && t.OptimisticVersion == step.OptimisticVersion
                        && t.ConcurrencyVersion == step.ConcurrencyVersion)
                    .ExecuteCommandAsync(cancellationToken);
            }

            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<DatabaseOrchestrationPageResult<DatabaseProvisionOperation>> QueryOperationsAsync(
        OperationListFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _dbContext.SqlSugar.Queryable<DatabaseProvisionOperationTable>()
            .Where(t => t.TenantNId == filter.TenantNId && !t.IsDeleted);

        if (filter.Kind is { } kind)
        {
            query = query.Where(t => t.Kind == kind);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(t => t.QueuedOn, OrderByType.Desc)
            .Skip((filter.PageIndex - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var stepsByOperation = await LoadOperationStepsMapAsync(rows.Select(r => r.Id).ToArray(), cancellationToken);
        var items = rows
            .Select(row => TableMapper.ToOperation(
                row,
                stepsByOperation.TryGetValue(row.Id, out var steps) ? steps : []))
            .ToList();

        return new DatabaseOrchestrationPageResult<DatabaseProvisionOperation>(
            items,
            total,
            filter.PageIndex,
            filter.PageSize);
    }

    // ===== 迁移观察 =====

    /// <inheritdoc />
    public async Task<DatabaseMigrationObservation?> GetLatestObservationAsync(
        string tenantNId,
        string environmentNId,
        string serviceKey,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<DatabaseMigrationObservationTable>()
            .Where(t => t.TenantNId == tenantNId
                && t.EnvironmentNId == environmentNId
                && t.ServiceKey == serviceKey
                && !t.IsDeleted)
            .OrderBy(t => t.ObservedOn, OrderByType.Desc)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToObservation(row);
    }

    /// <inheritdoc />
    public async Task AddObservationAsync(
        DatabaseMigrationObservation observation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        await InsertWithConflictGuardAsync(TableMapper.ToTable(observation), cancellationToken);
    }

    // ===== 私有辅助 =====

    private async Task<IReadOnlyList<DatabasePlanStep>> LoadPlanStepsAsync(
        Guid planId,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<DatabasePlanStepTable>()
            .Where(t => t.PlanId == planId && !t.IsDeleted && !t.PlanIsDeleted)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);
        return rows.Select(TableMapper.ToPlanStep).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<DatabasePlanStep>>> LoadPlanStepsMapAsync(
        Guid[] planIds,
        CancellationToken cancellationToken)
    {
        if (planIds.Length == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<DatabasePlanStep>>();
        }

        var rows = await _dbContext.SqlSugar.Queryable<DatabasePlanStepTable>()
            .Where(t => planIds.Contains(t.PlanId) && !t.IsDeleted && !t.PlanIsDeleted)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(t => t.PlanId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DatabasePlanStep>)g
                    .OrderBy(t => t.Sequence)
                    .Select(TableMapper.ToPlanStep)
                    .ToList());
    }

    private async Task<IReadOnlyList<DatabaseOperationStep>> LoadOperationStepsAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<DatabaseOperationStepTable>()
            .Where(t => t.OperationId == operationId && !t.IsDeleted && !t.OperationIsDeleted)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);
        return rows.Select(TableMapper.ToOperationStep).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<DatabaseOperationStep>>> LoadOperationStepsMapAsync(
        Guid[] operationIds,
        CancellationToken cancellationToken)
    {
        if (operationIds.Length == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<DatabaseOperationStep>>();
        }

        var rows = await _dbContext.SqlSugar.Queryable<DatabaseOperationStepTable>()
            .Where(t => operationIds.Contains(t.OperationId) && !t.IsDeleted && !t.OperationIsDeleted)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(t => t.OperationId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DatabaseOperationStep>)g
                    .OrderBy(t => t.Sequence)
                    .Select(TableMapper.ToOperationStep)
                    .ToList());
    }

    /// <summary>
    /// 插入并守卫唯一键冲突:唯一约束错误映射为并发异常,其余原样抛出。
    /// 必须用泛型保留具体表类型(SqlSugar <c>Insertable&lt;object&gt;</c> 会直接拒绝);
    /// 底层驱动可能抛裸 <see cref="DbException"/>(SQLite/PostgreSQL)而非 <c>SqlSugarException</c>,故用带过滤器捕获。
    /// </summary>
    private async Task InsertWithConflictGuardAsync<T>(
        T row,
        CancellationToken cancellationToken)
        where T : class, new()
    {
        try
        {
            await _dbContext.SqlSugar.Insertable(row).ExecuteCommandAsync(cancellationToken);
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new ConcurrencyException("唯一键冲突:记录已存在。", ex);
        }
    }

    /// <summary>沿内部异常链判定是否为唯一约束/主键冲突(SQLite "UNIQUE constraint failed"、PostgreSQL "duplicate key"/SQLSTATE 23505)。</summary>
    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            var message = ex.Message;
            if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                || message.Contains("23505", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureSingleRowAffected(int affected, string entityType, string operation)
    {
        if (affected != 1)
        {
            throw new ConcurrencyException($"实体 {entityType} {operation}失败:并发版本不匹配或记录不存在。");
        }
    }
}
