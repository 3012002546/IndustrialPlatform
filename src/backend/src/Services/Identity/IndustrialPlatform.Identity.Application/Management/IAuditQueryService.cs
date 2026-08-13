using IndustrialPlatform.Identity.Contracts.Management;

namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// 审计查询用例(§16.3/§19.1)。登录审计只读、只追加,按租户隔离查询。
/// </summary>
public interface IAuditQueryService
{
    /// <summary>按租户分页查询登录审计(UserNId 精确匹配,Success 可选过滤),按发生时间倒序。</summary>
    Task<ManagementPage<LoginAuditItem>> QueryLoginAuditsAsync(
        string tenantNId,
        LoginAuditFilter filter,
        CancellationToken cancellationToken);
}
