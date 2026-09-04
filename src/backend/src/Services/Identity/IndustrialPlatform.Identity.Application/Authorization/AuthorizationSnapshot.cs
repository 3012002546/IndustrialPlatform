using IndustrialPlatform.Identity.Domain.Users;

namespace IndustrialPlatform.Identity.Application.Authorization;

/// <summary>
/// 授权快照:用户在租户下的安全状态与活动权限集(§18)。
/// 由 <see cref="IAuthorizationDataStore"/> 装载,供 <see cref="IPermissionEvaluator"/>
/// 对照 JWT 声明(sub/tenant_id/ver)裁决。
/// </summary>
public sealed record AuthorizationSnapshot(
    string TenantNId,
    string UserNId,
    UserStatus Status,
    int AuthVersion,
    IReadOnlyList<string> PermissionNIds,
    bool IsSystemAdmin = false);
