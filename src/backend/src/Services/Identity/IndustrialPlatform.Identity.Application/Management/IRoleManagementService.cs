using IndustrialPlatform.Identity.Contracts.Management;

namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// 角色管理用例(§16.2/§19.2)。租户编码与执行者由经过验证的令牌提供(§18),
/// 不信任请求体传入;跨租户资源统一返回 <see cref="ResourceNotFoundException"/>。
/// </summary>
public interface IRoleManagementService
{
    /// <summary>创建角色并分配初始权限;业务标识冲突 409,无效权限引用 400。</summary>
    Task<RoleSummary> CreateAsync(
        string tenantNId,
        string actorUserNId,
        CreateRoleRequest request,
        CancellationToken cancellationToken);

    /// <summary>修改角色名称/描述(带乐观并发)。</summary>
    Task<RoleSummary> UpdateAsync(
        string tenantNId,
        string actorUserNId,
        string roleNId,
        UpdateRoleRequest request,
        CancellationToken cancellationToken);

    /// <summary>分配权限(差量);变更后为全部持有者失效权限缓存。</summary>
    Task<RoleSummary> AssignPermissionsAsync(
        string tenantNId,
        string actorUserNId,
        string roleNId,
        AssignRolePermissionsRequest request,
        CancellationToken cancellationToken);

    /// <summary>按租户分页查询角色(NId/Name 包含匹配)。</summary>
    Task<ManagementPage<RoleSummary>> ListAsync(string tenantNId, RoleListFilter filter, CancellationToken cancellationToken);

    /// <summary>按业务标识查询角色详情;不存在或跨租户返回 404。</summary>
    Task<RoleSummary> GetAsync(string tenantNId, string roleNId, CancellationToken cancellationToken);
}
