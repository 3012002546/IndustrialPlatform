using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Users;

namespace IndustrialPlatform.Identity.Application.Authentication;

/// <summary>
/// 认证查询快照:用户聚合(含活动角色关系)与其活动角色的标识。
/// 版本号用于登录状态变更后的乐观并发更新。
/// </summary>
public sealed record AuthenticatedUser(
    User User,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> RoleNIds,
    bool IsSystemAdmin = false);

/// <summary>
/// 认证用例的持久化端口:用户查询、双版本更新与角色权限查询。
/// 由基础设施仓储组合实现。
/// </summary>
public interface IAuthenticationStore
{
    /// <summary>按租户与规范化登录名查询用户(含活动角色关系);不存在返回 <c>null</c>。</summary>
    Task<AuthenticatedUser?> FindByNormalizedLoginNameAsync(
        string tenantNId,
        string normalizedLoginName,
        CancellationToken cancellationToken);

    /// <summary>按用户业务标识查询用户(含活动角色关系);不存在返回 <c>null</c>。</summary>
    Task<AuthenticatedUser?> FindByNIdAsync(string userNId, CancellationToken cancellationToken);

    /// <summary>按数据库主键查询用户(含活动角色关系);不存在返回 <c>null</c>。供刷新会话按 UserId 装载用户。</summary>
    Task<AuthenticatedUser?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>以调用方读取时保存的原始双版本原子更新用户;版本不匹配抛并发异常。</summary>
    Task UpdateUserAsync(
        User user,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken);

    /// <summary>查询给定角色集的活动权限(去重)。</summary>
    Task<IReadOnlyList<Permission>> GetPermissionsForRolesAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken);
}
