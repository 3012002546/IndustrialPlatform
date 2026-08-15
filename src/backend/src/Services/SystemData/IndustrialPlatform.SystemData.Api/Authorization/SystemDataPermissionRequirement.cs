using Microsoft.AspNetCore.Authorization;

namespace IndustrialPlatform.SystemData.Api.Authorization;

/// <summary>
/// 权限授权需求(TASK-SD-006):携带所需权限 NId,由 <see cref="SystemDataPermissionAuthorizationHandler"/>
/// 从令牌 <see cref="SystemDataClaimTypes.PermissionNId"/> 声明裁决。通过
/// <see cref="SystemDataPermissionPolicies"/> 注册为策略。
/// </summary>
public sealed class SystemDataPermissionRequirement : IAuthorizationRequirement
{
    /// <summary>所需权限 NId(如 <c>systemdata.organization.view</c>)。</summary>
    public string PermissionNId { get; }

    /// <summary>初始化权限需求。</summary>
    /// <param name="permissionNId">所需权限 NId。</param>
    public SystemDataPermissionRequirement(string permissionNId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionNId);
        PermissionNId = permissionNId;
    }
}
