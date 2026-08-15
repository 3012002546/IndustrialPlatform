using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Assignments;
using IndustrialPlatform.SystemData.Domain.Assignments;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;

/// <summary>
/// 用户任职仓储(TASK-SD-005):按业务标识查询 + 用户任职集(供区间重叠/主任职覆盖裁决) +
/// 岗位停用门存在性。写操作唯一键冲突或双版本不匹配抛
/// <see cref="SharedKernel.Exceptions.ConcurrencyException"/>。
/// 用户级并发由 <see cref="UserAssignmentAdvisoryLock"/> 在应用层编排。
/// </summary>
public sealed class UserAssignmentStore : IUserAssignmentStore
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化任职仓储。</summary>
    public UserAssignmentStore(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<UserAssignment?> GetAsync(
        string tenantNId,
        string assignmentNId,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<UserAssignmentTable>()
            .Where(t => t.TenantNId == tenantNId && t.NId == assignmentNId && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToUserAssignment(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAssignment>> GetAssignmentsForUserAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<UserAssignmentTable>()
            .Where(t => t.TenantNId == tenantNId && t.UserNId == userNId && !t.IsDeleted)
            .OrderBy(t => t.EffectiveFrom)
            .ToListAsync(cancellationToken);
        return rows.Select(TableMapper.ToUserAssignment).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveOrFutureByPositionAsync(
        string tenantNId,
        string positionNId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Queryable<UserAssignmentTable>()
            .Where(t => t.TenantNId == tenantNId
                && t.PositionNId == positionNId
                && t.State == (int)AssignmentState.Enabled
                && (t.EffectiveTo == null || t.EffectiveTo > now)
                && !t.IsDeleted)
            .AnyAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(UserAssignment assignment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        await StoreConflictGuard.InsertWithConflictGuardAsync(
            _dbContext.SqlSugar, TableMapper.ToTable(assignment), cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        UserAssignment assignment,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var affected = await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(assignment))
            .Where(t => t.Id == assignment.Id
                && !t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);
        StoreConflictGuard.EnsureSingleRowAffected(affected, assignment.EntityType, "更新");
    }
}
