using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.PlatformStudio.Application.Integration;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.UnifiedHost;

/// <summary>原生可信范围适配：角色从Identity权威目录获取，工厂仅来自运维明确范围事实。未配置为空，绝不推导ALL。</summary>
public sealed class PlatformStudioFactoryScope(IManagementStore directory,StudioDeploymentProfile profile) : IStudioFactoryScope
{
    public async Task<IReadOnlyList<string>> GetAsync(string tenantNId,string userNId,CancellationToken cancellationToken)
    {
        var user=await directory.GetUserAsync(userNId,cancellationToken);
        return ForUser(tenantNId,userNId,user);
    }
    /// <summary>复用同次请求已从权威目录取得的主体；不缓存到下一次请求。</summary>
    public IReadOnlyList<string> ForUser(string tenantNId,string userNId,StoredUser? user)
    {
        if(user is null||user.NId!=userNId||user.IsDeleted||user.TenantNId!=tenantNId||user.Status!=UserStatus.Active)return [];
        return MapRoles(tenantNId,userNId,user.EffectiveRoleNIds);
    }
    /// <summary>角色来自同次权威快照；仍单独核对新读取的用户状态，不缓存授权。</summary>
    public IReadOnlyList<string> ForUser(string tenantNId,string userNId,StoredUserSecurityState? user,IReadOnlyList<string> roles)
    {
        if(user is null||user.NId!=userNId||user.IsDeleted||user.TenantNId!=tenantNId||user.Status!=UserStatus.Active)return [];
        return MapRoles(tenantNId,userNId,roles);
    }
    private string[] MapRoles(string tenantNId,string userNId,IReadOnlyList<string> roles)
    {
        var mappings=profile.Identity.NativeFactoryScopes;
        if(mappings.Any(m=>string.IsNullOrWhiteSpace(m.TenantNId)||(string.IsNullOrWhiteSpace(m.UserNId)==string.IsNullOrWhiteSpace(m.RoleNId))||m.FactoryNIds.Any(f=>string.IsNullOrWhiteSpace(f)||f=="*"||f.Length>128)))throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","原生工厂范围配置未通过明确主体校验。",503);
        return mappings.Where(m=>m.TenantNId==tenantNId&&(m.UserNId==userNId||m.RoleNId is {} role&&roles.Contains(role,StringComparer.Ordinal))).SelectMany(m=>m.FactoryNIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}
