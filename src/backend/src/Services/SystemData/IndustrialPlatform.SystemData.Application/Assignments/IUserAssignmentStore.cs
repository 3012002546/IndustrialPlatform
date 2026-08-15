using IndustrialPlatform.SystemData.Domain.Assignments;

namespace IndustrialPlatform.SystemData.Application.Assignments;

/// <summary>
/// 用户任职持久化端口(TASK-SD-005,05 方案 §7.4/§8.1 <c>system_data_user_assignment</c>)。
/// 按业务标识查询返回 <c>null</c> 时由应用层映射为 404;
/// 写操作在唯一键冲突或双版本不匹配时由实现抛并发异常。
/// 同用户任职集按用户 advisory lock 内读取,供 <see cref="AssignmentScheduleRules"/> 裁决。
/// </summary>
public interface IUserAssignmentStore
{
    /// <summary>按 (TenantNId, AssignmentNId) 查询任职(含软删过滤);不存在返回 <c>null</c>。</summary>
    Task<UserAssignment?> GetAsync(string tenantNId, string assignmentNId, CancellationToken cancellationToken);

    /// <summary>查询用户全部任职(含软删过滤),供区间重叠与主任职覆盖裁决。</summary>
    Task<IReadOnlyList<UserAssignment>> GetAssignmentsForUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken);

    /// <summary>岗位下是否存在当前或未来有效任职(岗位停用门;未结束的 Enabled,05 方案 §7.3)。</summary>
    Task<bool> HasActiveOrFutureByPositionAsync(string tenantNId, string positionNId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>新增任职。</summary>
    Task AddAsync(UserAssignment assignment, CancellationToken cancellationToken);

    /// <summary>按双版本原子更新任职;版本不匹配抛并发异常。</summary>
    Task UpdateAsync(
        UserAssignment assignment,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken);
}
