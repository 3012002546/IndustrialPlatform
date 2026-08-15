using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Organizations;
using IndustrialPlatform.SystemData.Domain.Assignments;
using IndustrialPlatform.SystemData.Domain.Organizations;
using IndustrialPlatform.SystemData.Domain.Positions;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;

/// <summary>
/// 行政组织仓储(TASK-SD-005):按业务标识查询 + 同级名称可用性(部分唯一索引) +
/// 停用/移动预览依赖计数(组合横跨岗位/任职表) + 子树展开(内存 BFS,树浅)。
/// 写操作唯一键冲突或双版本不匹配抛 <see cref="SharedKernel.Exceptions.ConcurrencyException"/>。
/// </summary>
public sealed class AdministrativeOrganizationStore : IAdministrativeOrganizationStore
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化组织仓储。</summary>
    public AdministrativeOrganizationStore(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<AdministrativeOrganization?> GetAsync(
        string tenantNId,
        string organizationNId,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<AdministrativeOrganizationTable>()
            .Where(t => t.TenantNId == tenantNId && t.NId == organizationNId && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToAdministrativeOrganization(row);
    }

    /// <inheritdoc />
    public async Task<bool> NameAvailableAsync(
        string tenantNId,
        string? parentOrganizationNId,
        string name,
        string? excludeOrganizationNId,
        CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        var exists = await _dbContext.SqlSugar.Queryable<AdministrativeOrganizationTable>()
            .Where(t => t.TenantNId == tenantNId
                && t.ParentOrganizationNId == parentOrganizationNId
                && t.NormalizedName == normalized
                && !t.IsDeleted
                && (excludeOrganizationNId == null || t.NId != excludeOrganizationNId))
            .AnyAsync(cancellationToken);
        return !exists;
    }

    /// <inheritdoc />
    public async Task<OrganizationDependencyCounts> GetDependencyCountsAsync(
        string tenantNId,
        string organizationNId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;

        var activeChildCount = await sugar.Queryable<AdministrativeOrganizationTable>()
            .Where(t => t.TenantNId == tenantNId
                && t.ParentOrganizationNId == organizationNId
                && t.Status == (int)OrganizationStatus.Active
                && !t.IsDeleted)
            .CountAsync(cancellationToken);

        var activePositionCount = await sugar.Queryable<PositionTable>()
            .Where(t => t.TenantNId == tenantNId
                && t.OrganizationNId == organizationNId
                && t.Status == (int)PositionStatus.Active
                && !t.IsDeleted)
            .CountAsync(cancellationToken);

        var activeOrFutureAssignmentCount = await CountActiveOrFutureAssignmentsForOrganizationAsync(
            sugar, tenantNId, organizationNId, now, cancellationToken);

        return new OrganizationDependencyCounts(activeChildCount, activePositionCount, activeOrFutureAssignmentCount);
    }

    /// <inheritdoc />
    public async Task<OrganizationSubtreeCounts> GetSubtreeCountsAsync(
        string tenantNId,
        string rootOrganizationNId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        var subtreeNIds = await ExpandSubtreeNIdsAsync(sugar, tenantNId, rootOrganizationNId, cancellationToken);

        var positionCount = await sugar.Queryable<PositionTable>()
            .Where(t => t.TenantNId == tenantNId
                && subtreeNIds.Contains(t.OrganizationNId)
                && !t.IsDeleted)
            .CountAsync(cancellationToken);

        var assignmentCount = await CountActiveOrFutureAssignmentsForOrganizationSubtreeAsync(
            sugar, tenantNId, subtreeNIds, now, cancellationToken);

        return new OrganizationSubtreeCounts(subtreeNIds.Count, positionCount, assignmentCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetDescendantNIdsAsync(
        string tenantNId,
        string rootOrganizationNId,
        CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        return await ExpandSubtreeNIdsAsync(sugar, tenantNId, rootOrganizationNId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(AdministrativeOrganization organization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organization);
        await StoreConflictGuard.InsertWithConflictGuardAsync(
            _dbContext.SqlSugar, TableMapper.ToTable(organization), cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        AdministrativeOrganization organization,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organization);
        var affected = await _dbContext.SqlSugar.Updateable(TableMapper.ToTable(organization))
            .Where(t => t.Id == organization.Id
                && !t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);
        StoreConflictGuard.EnsureSingleRowAffected(affected, organization.EntityType, "更新");
    }

    // ===== 私有辅助 =====

    /// <summary>以根组织为起点沿父引用内存 BFS 展开子树业务标识(含根;树浅,租户内全量邻接一次载入)。</summary>
    private static async Task<List<string>> ExpandSubtreeNIdsAsync(
        ISqlSugarClient sugar,
        string tenantNId,
        string rootOrganizationNId,
        CancellationToken cancellationToken)
    {
        var nodes = await sugar.Queryable<AdministrativeOrganizationTable>()
            .Where(t => t.TenantNId == tenantNId && !t.IsDeleted)
            .Select(t => new OrgEdge { NId = t.NId, ParentOrganizationNId = t.ParentOrganizationNId })
            .ToListAsync(cancellationToken);

        var children = nodes
            .Where(n => n.ParentOrganizationNId is not null)
            .GroupBy(n => n.ParentOrganizationNId!)
            .ToDictionary(g => g.Key, g => g.Select(n => n.NId).ToList());

        var result = new List<string> { rootOrganizationNId };
        var queue = new Queue<string>();
        queue.Enqueue(rootOrganizationNId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!children.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var child in next)
            {
                result.Add(child);
                queue.Enqueue(child);
            }
        }

        return result;
    }

    /// <summary>组织下当前或未来有效任职计数(State=Enabled 且未结束)。</summary>
    private static async Task<int> CountActiveOrFutureAssignmentsForOrganizationAsync(
        ISqlSugarClient sugar,
        string tenantNId,
        string organizationNId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await sugar.Queryable<UserAssignmentTable>()
            .Where(t => t.TenantNId == tenantNId
                && t.OrganizationNId == organizationNId
                && t.State == (int)AssignmentState.Enabled
                && (t.EffectiveTo == null || t.EffectiveTo > now)
                && !t.IsDeleted)
            .CountAsync(cancellationToken);

    /// <summary>子树内当前或未来有效任职计数(同停用门条件,按组织 NId 集合过滤)。</summary>
    private static async Task<int> CountActiveOrFutureAssignmentsForOrganizationSubtreeAsync(
        ISqlSugarClient sugar,
        string tenantNId,
        List<string> organizationNIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (organizationNIds.Count == 0)
        {
            return 0;
        }

        return await sugar.Queryable<UserAssignmentTable>()
            .Where(t => t.TenantNId == tenantNId
                && organizationNIds.Contains(t.OrganizationNId)
                && t.State == (int)AssignmentState.Enabled
                && (t.EffectiveTo == null || t.EffectiveTo > now)
                && !t.IsDeleted)
            .CountAsync(cancellationToken);
    }

    /// <summary>BFS 邻接边(私有投影模型)。</summary>
    private sealed class OrgEdge
    {
        public string NId { get; set; } = string.Empty;

        public string? ParentOrganizationNId { get; set; }
    }
}
