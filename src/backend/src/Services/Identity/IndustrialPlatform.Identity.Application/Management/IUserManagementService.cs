using IndustrialPlatform.Identity.Contracts.Management;

namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// 用户管理用例(§16.1/§19.2)。租户编码与执行者由经过验证的令牌提供(§18),
/// 不信任请求体传入;跨租户资源统一返回 <see cref="ResourceNotFoundException"/>。
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    /// 创建用户并分配初始角色(§29A.4):业务标识冲突 409,登录名冲突/保留 409/400;
    /// 服务端生成独立随机临时密码,只在本次 201 响应(<see cref="CreateUserResult.TemporaryPassword"/>)出现一次,
    /// 新用户强制首次登录改密(<c>MustChangePassword=true</c>)。
    /// </summary>
    Task<CreateUserResult> CreateAsync(
        string tenantNId,
        string actorUserNId,
        CreateUserRequest request,
        CancellationToken cancellationToken);

    /// <summary>修改用户资料/登录名(带乐观并发);登录名变更推进安全版本使旧会话失效。</summary>
    Task<UserSummary> UpdateAsync(
        string tenantNId,
        string actorUserNId,
        string userNId,
        UpdateUserRequest request,
        CancellationToken cancellationToken);

    /// <summary>启用/禁用用户;禁止禁用当前登录用户或最后一名系统管理员。</summary>
    Task<UserSummary> SetStatusAsync(
        string tenantNId,
        string actorUserNId,
        string userNId,
        SetUserStatusRequest request,
        CancellationToken cancellationToken);

    /// <summary>分配角色(差量);解除最后一名系统管理员由领域保护。</summary>
    Task<UserSummary> AssignRolesAsync(
        string tenantNId,
        string actorUserNId,
        string userNId,
        AssignUserRolesRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 管理员重置密码(§29A.4/§29A.5):服务端生成独立随机临时密码,只在本次响应出现一次;
    /// 重置后强制首次改密、推进安全版本并撤销该用户全部会话。
    /// </summary>
    Task<ResetPasswordResult> ResetPasswordAsync(
        string tenantNId,
        string actorUserNId,
        string userNId,
        ResetPasswordRequest request,
        CancellationToken cancellationToken);

    /// <summary>安全删除用户(墓碑,§29A.3):禁删自己/内置 ADMIN/最后一名系统管理员;推进安全版本、撤销全部会话并软删授权关系。</summary>
    Task DeleteAsync(
        string tenantNId,
        string actorUserNId,
        string userNId,
        DeleteUserRequest request,
        CancellationToken cancellationToken);

    /// <summary>恢复用户墓碑(§29A.3):仅恢复为 Disabled,不自动恢复授权/凭据/会话。</summary>
    Task<UserSummary> RestoreAsync(
        string tenantNId,
        string actorUserNId,
        string userNId,
        RestoreUserRequest request,
        CancellationToken cancellationToken);

    /// <summary>按租户分页查询用户(NId/LoginName/Name 包含匹配,Status 枚举名)。</summary>
    Task<ManagementPage<UserSummary>> ListAsync(string tenantNId, UserListFilter filter, CancellationToken cancellationToken);

    /// <summary>按业务标识查询用户详情;不存在或跨租户返回 404。</summary>
    Task<UserSummary> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken);
}
