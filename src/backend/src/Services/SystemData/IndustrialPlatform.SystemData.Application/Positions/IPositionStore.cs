using IndustrialPlatform.SystemData.Domain.Positions;

namespace IndustrialPlatform.SystemData.Application.Positions;

/// <summary>
/// 岗位持久化端口(TASK-SD-005,05 方案 §7.3/§8.1 <c>system_data_position</c>)。
/// 按业务标识查询返回 <c>null</c> 时由应用层映射为 404;
/// 写操作在唯一键冲突或双版本不匹配时由实现抛并发异常。
/// 岗位停用所需的任职存在性由 <see cref="Assignments.IUserAssignmentStore"/> 提供。
/// </summary>
public interface IPositionStore
{
    /// <summary>按 (TenantNId, PositionNId) 查询岗位(含软删过滤);不存在返回 <c>null</c>。</summary>
    Task<Position?> GetAsync(string tenantNId, string positionNId, CancellationToken cancellationToken);

    /// <summary>
    /// 组织内岗位名称是否可用(部分唯一索引 <c>(tenant_nid, organization_nid, normalized_name)</c> where is_deleted=0)。
    /// 改名时传 <paramref name="excludePositionNId"/> 排除自身。
    /// </summary>
    Task<bool> NameAvailableAsync(
        string tenantNId,
        string organizationNId,
        string name,
        string? excludePositionNId,
        CancellationToken cancellationToken);

    /// <summary>新增岗位(组织内名称唯一冲突抛并发异常)。</summary>
    Task AddAsync(Position position, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新岗位;版本不匹配抛并发异常。</summary>
    Task UpdateAsync(
        Position position,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken);
}
