using Microsoft.AspNetCore.Authorization;

namespace IndustrialPlatform.Identity.Api.Authorization;

/// <summary>
/// 权限授权需求(§18):携带所需权限 NId,由 <see cref="PermissionAuthorizationHandler"/> 裁决。
/// 通过 <see cref="PermissionPolicies"/> 注册为策略。
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    /// <summary>所需权限 NId(如 <c>identity.user.view</c>)。</summary>
    public string PermissionNId { get; }

    /// <summary>初始化权限需求。</summary>
    /// <param name="permissionNId">所需权限 NId。</param>
    public PermissionRequirement(string permissionNId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionNId);
        PermissionNId = permissionNId;
    }
}
