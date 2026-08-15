using IndustrialPlatform.SystemData.Domain.Organizations;

namespace IndustrialPlatform.SystemData.Application.Organizations;

/// <summary>
/// 行政组织持久化端口(TASK-SD-005,05 方案 §7.2/§8.1 <c>system_data_administrative_organization</c>)。
/// 按业务标识查询返回 <c>null</c> 时由应用层映射为 404;
/// 写操作(Add/Update)在唯一键冲突或双版本不匹配时由实现抛
/// <see cref="SharedKernel.Exceptions.ConcurrencyException"/>,应用层映射为业务冲突。
/// 依赖计数与子树计数横跨岗位/任职表,由本组合存储一次查询提供。
/// </summary>
public interface IAdministrativeOrganizationStore
{
    /// <summary>按 (TenantNId, OrganizationNId) 查询组织(含软删过滤);不存在返回 <c>null</c>。</summary>
    Task<AdministrativeOrganization?> GetAsync(string tenantNId, string organizationNId, CancellationToken cancellationToken);

    /// <summary>
    /// 同级名称是否可用(部分唯一索引 <c>(tenant_nid, parent_organization_nid, normalized_name)</c> where is_deleted=0)。
    /// 根公司父级为 <c>null</c>;改名/移入时传 <paramref name="excludeOrganizationNId"/> 排除自身。
    /// </summary>
    Task<bool> NameAvailableAsync(
        string tenantNId,
        string? parentOrganizationNId,
        string name,
        string? excludeOrganizationNId,
        CancellationToken cancellationToken);

    /// <summary>停用门依赖计数(05 方案 §7.2):活动直接子组织 / 活动岗位 / 当前或未来有效任职(未结束的 Enabled)。</summary>
    Task<OrganizationDependencyCounts> GetDependencyCountsAsync(string tenantNId, string organizationNId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>子树计数(含根组织):组织数 / 岗位数 / 当前或未来任职数,用于移动预览(05 方案 §7.2)。</summary>
    Task<OrganizationSubtreeCounts> GetSubtreeCountsAsync(string tenantNId, string rootOrganizationNId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>子树全部组织业务标识(含根),用于祖先循环校验。</summary>
    Task<IReadOnlyList<string>> GetDescendantNIdsAsync(string tenantNId, string rootOrganizationNId, CancellationToken cancellationToken);

    /// <summary>新增组织(同级名称唯一冲突抛并发异常)。</summary>
    Task AddAsync(AdministrativeOrganization organization, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新组织;版本不匹配抛并发异常。</summary>
    Task UpdateAsync(
        AdministrativeOrganization organization,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken);
}

/// <summary>停用组织前依赖计数(05 方案 §8.2)。</summary>
public sealed record OrganizationDependencyCounts(
    int ActiveChildCount,
    int ActivePositionCount,
    int ActiveOrFutureAssignmentCount);

/// <summary>组织子树资源计数(05 方案 §7.2 移动预览)。</summary>
public sealed record OrganizationSubtreeCounts(
    int OrganizationCount,
    int PositionCount,
    int AssignmentCount);
