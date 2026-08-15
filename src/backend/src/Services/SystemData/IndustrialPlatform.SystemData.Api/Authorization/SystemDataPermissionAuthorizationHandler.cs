using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace IndustrialPlatform.SystemData.Api.Authorization;

/// <summary>
/// 权限授权处理器(TASK-SD-006,§9.6):从认证用户令牌声明读取 <c>permission_nid</c>
/// (可多条,或单条以空格分隔),校验所需权限是否授予。令牌不携带所需权限 → fail;
/// 由 JwtBearer OnForbidden 映射 403 信封。无存储依赖,不产生 503 路径。
/// </summary>
public sealed class SystemDataPermissionAuthorizationHandler : AuthorizationHandler<SystemDataPermissionRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SystemDataPermissionRequirement requirement)
    {
        var granted = context.User
            .FindAll(SystemDataClaimTypes.PermissionNId)
            .SelectMany(ExpandPermissionClaim)
            .Contains(requirement.PermissionNId, StringComparer.Ordinal);

        if (granted)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    /// <summary>展开单条声明值(空格分隔的权限 NId 列表)。</summary>
    private static IEnumerable<string> ExpandPermissionClaim(Claim claim) =>
        claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
