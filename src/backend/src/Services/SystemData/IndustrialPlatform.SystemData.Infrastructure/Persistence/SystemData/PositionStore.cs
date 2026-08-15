using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Positions;
using IndustrialPlatform.SystemData.Domain.Positions;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;

/// <summary>
/// 岗位仓储(TASK-SD-005):按业务标识查询 + 组织内名称可用性(部分唯一索引)。
/// 写操作唯一键冲突或双版本不匹配抛 <see cref="SharedKernel.Exceptions.ConcurrencyException"/>。
/// </summary>
public sealed class PositionStore : IPositionStore
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化岗位仓储。</summary>
    public PositionStore(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<Position?> GetAsync(
        string tenantNId,
        string positionNId,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<PositionTable>()
            .Where(t => t.TenantNId == tenantNId && t.NId == positionNId && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToPosition(row);
    }

    /// <inheritdoc />
    public async Task<bool> NameAvailableAsync(
        string tenantNId,
        string organizationNId,
        string name,
        string? excludePositionNId,
        CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        var exists = await _dbContext.SqlSugar.Queryable<PositionTable>()
            .Where(t => t.TenantNId == tenantNId
                && t.OrganizationNId == organizationNId
                && t.NormalizedName == normalized
                && !t.IsDeleted
                && (excludePositionNId == null || t.NId != excludePositionNId))
            .AnyAsync(cancellationToken);
        return !exists;
    }

    /// <inheritdoc />
    public async Task AddAsync(Position position, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        await StoreConflictGuard.InsertWithConflictGuardAsync(
            _dbContext.SqlSugar, TableMapper.ToTable(position), cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        Position position,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        var affected = await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(position))
            .Where(t => t.Id == position.Id
                && !t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);
        StoreConflictGuard.EnsureSingleRowAffected(affected, position.EntityType, "更新");
    }
}
