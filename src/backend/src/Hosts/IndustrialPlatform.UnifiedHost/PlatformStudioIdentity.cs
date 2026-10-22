using System.Globalization;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Integration;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.Security;

namespace IndustrialPlatform.UnifiedHost;

/// <summary>直接消费Identity权威快照和活动会话，不复制登录、权限缓存或用户数据库。</summary>
public sealed class PlatformStudioIdentity(IHttpContextAccessor accessor,IAuthorizationDataStore snapshots,IManagementStore directory,ISessionRevocationStore revocations,IStudioFactoryScope factories,StudioDeploymentProfile profile,TimeProvider clock) : IStudioIdentity
{
    public async Task<StudioPrincipal> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var user=accessor.HttpContext?.User;
        if(user?.Identity?.IsAuthenticated!=true) throw new StudioException("STUDIO_UNAUTHENTICATED","请登录平台。",401);
        var tenant=user.FindFirst(ClaimConstants.TenantId)?.Value;var subject=user.FindFirst(ClaimConstants.UserNId)?.Value;var session=user.FindFirst(ClaimConstants.SessionId)?.Value;
        if(string.IsNullOrWhiteSpace(tenant)||string.IsNullOrWhiteSpace(subject)||string.IsNullOrWhiteSpace(session)||!int.TryParse(user.FindFirst(ClaimConstants.AuthVersion)?.Value,NumberStyles.None,CultureInfo.InvariantCulture,out var version)) throw new StudioException("STUDIO_UNAUTHENTICATED","平台会话声明不完整。",401);
        try
        {
            if(await revocations.IsRevokedAsync(session,cancellationToken)) throw new StudioException("STUDIO_UNAUTHENTICATED","平台会话已撤销。",401);
            var snapshot=await snapshots.GetSnapshotAsync(tenant,subject,cancellationToken);
            if(snapshot is null||snapshot.Status!=UserStatus.Active||snapshot.AuthVersion!=version) throw new StudioException("STUDIO_UNAUTHENTICATED","平台用户状态或安全版本已改变。",401);
            var entry=await directory.GetUserAsync(subject,cancellationToken);
            if(entry is null||entry.IsDeleted||entry.TenantNId!=tenant||entry.Status!=UserStatus.Active) throw new StudioException("STUDIO_UNAUTHENTICATED","平台主体已不可用。",401);
            var now=clock.GetUtcNow();
            var expires=long.TryParse(user.FindFirst("exp")?.Value,NumberStyles.None,CultureInfo.InvariantCulture,out var epoch)?DateTimeOffset.FromUnixTimeSeconds(epoch):now;
            if(expires<=now) throw new StudioException("STUDIO_UNAUTHENTICATED","平台会话已过期。",401);
            return new(tenant,subject,"platform",entry.Name,entry.EffectiveRoleNIds,snapshot.PermissionNIds,await factories.GetAsync(tenant,subject,cancellationToken),version.ToString(CultureInfo.InvariantCulture),profile.Identity.PolicyVersion,now,expires);
        }
        catch(StudioException){throw;}
        catch(Exception ex) when(ex is not OperationCanceledException){throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","平台身份服务暂不可用。",503);}
    }
    public Task<StudioPrincipal> RefreshAsync(StudioPrincipal principal,CancellationToken cancellationToken)=>GetCurrentAsync(cancellationToken);
}
/// <summary>授权编辑仅接受当前租户公开目录中实际存在的用户/角色。</summary>
public sealed class PlatformStudioSubjectDirectory(IManagementStore directory) : IStudioSubjectDirectory
{
    public async Task<bool> ExistsAsync(StudioPrincipal principal,string subjectKind,string subjectNId,CancellationToken cancellationToken)
    {
        if(subjectKind=="User") {var user=await directory.GetUserAsync(subjectNId,cancellationToken);return user is not null&&!user.IsDeleted&&user.TenantNId==principal.TenantNId&&user.Status==UserStatus.Active;}
        if(subjectKind=="Role") {var role=await directory.GetRoleAsync(subjectNId,cancellationToken);return role?.TenantNId==principal.TenantNId;}
        return false;
    }
}
