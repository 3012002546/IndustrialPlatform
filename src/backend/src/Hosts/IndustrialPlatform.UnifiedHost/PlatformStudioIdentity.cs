using System.Globalization;
using System.Diagnostics;
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
public sealed class PlatformStudioIdentity(IHttpContextAccessor accessor,IAuthorizationDataStore snapshots,IManagementStore directory,ISessionRevocationStore revocations,IStudioFactoryScope factories,StudioDeploymentProfile profile,TimeProvider clock) : IStudioIdentity,IndustrialPlatform.PlatformStudio.Application.Dashboard.IStudioScreenIdentity
{
    public async Task<StudioPrincipal> GetPublisherAsync(StudioScreenLink link, CancellationToken ct)
    {
        try
        {
            var stage = Stopwatch.GetTimestamp();
            var snapshot = await snapshots.GetSnapshotAsync(link.TenantNId, link.CreatedBy, ct);
            stage = StudioRuntimeTiming.Mark("publisher-snapshot", stage);
            if (snapshot?.RoleNIds is { } roles && factories is PlatformStudioFactoryScope native)
                return await ReadNativePublisherAsync(link, snapshot, roles, native, ct);
            var entry = await directory.GetUserAsync(link.CreatedBy, ct);
            StudioRuntimeTiming.Mark("publisher-directory", stage);
            if (link.IssuerKey != "platform" || snapshot is null || snapshot.Status != UserStatus.Active || snapshot.AuthVersion.ToString(CultureInfo.InvariantCulture) != link.AuthVersion
                || entry is null || entry.IsDeleted || entry.TenantNId != link.TenantNId || entry.Status != UserStatus.Active)
                throw new StudioException("STUDIO_SCREEN_UNAVAILABLE", "大屏发布者已失效。", 404);
            return new(link.TenantNId, link.CreatedBy, "platform", entry.Name, entry.EffectiveRoleNIds, snapshot.PermissionNIds,
                await FactoryScopeAsync(link.TenantNId, link.CreatedBy, entry, ct), link.AuthVersion, profile.Identity.PolicyVersion, clock.GetUtcNow(),
                link.NeverExpires ? clock.GetUtcNow().AddMinutes(5) : link.ExpiresOn);
        }
        catch (StudioException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE", "平台身份服务暂不可用。", 503); }
    }
    private async Task<StudioPrincipal> ReadNativePublisherAsync(StudioScreenLink link, AuthorizationSnapshot snapshot, IReadOnlyList<string> roles, PlatformStudioFactoryScope native, CancellationToken ct)
    {
        var stage = Stopwatch.GetTimestamp();
        // 保留独立用户读取与删除/禁用检查，并额外复核安全版本；只消除重复角色投影。
        var entry = await directory.GetUserSecurityStateAsync(link.CreatedBy, ct);
        StudioRuntimeTiming.Mark("publisher-security-state", stage);
        if (link.IssuerKey != "platform" || snapshot.TenantNId != link.TenantNId || snapshot.UserNId != link.CreatedBy || snapshot.Status != UserStatus.Active || snapshot.AuthVersion.ToString(CultureInfo.InvariantCulture) != link.AuthVersion
            || entry is null || entry.NId != link.CreatedBy || entry.IsDeleted || entry.TenantNId != link.TenantNId || entry.Status != UserStatus.Active || entry.AuthVersion != snapshot.AuthVersion)
            throw new StudioException("STUDIO_SCREEN_UNAVAILABLE", "大屏发布者已失效。", 404);
        return new(link.TenantNId, link.CreatedBy, "platform", entry.Name, roles, snapshot.PermissionNIds,
            native.ForUser(link.TenantNId,link.CreatedBy,entry,roles), link.AuthVersion, profile.Identity.PolicyVersion, clock.GetUtcNow(),
            link.NeverExpires ? clock.GetUtcNow().AddMinutes(5) : link.ExpiresOn);
    }
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
            return new(tenant,subject,"platform",entry.Name,entry.EffectiveRoleNIds,snapshot.PermissionNIds,await FactoryScopeAsync(tenant,subject,entry,cancellationToken),version.ToString(CultureInfo.InvariantCulture),profile.Identity.PolicyVersion,now,expires);
        }
        catch(StudioException){throw;}
        catch(Exception ex) when(ex is not OperationCanceledException){throw new StudioException("STUDIO_IDENTITY_UNAVAILABLE","平台身份服务暂不可用。",503);}
    }
    public Task<StudioPrincipal> RefreshAsync(StudioPrincipal principal,CancellationToken cancellationToken)=>GetCurrentAsync(cancellationToken);
    private Task<IReadOnlyList<string>> FactoryScopeAsync(string tenant,string subject,StoredUser entry,CancellationToken ct) =>
        factories is PlatformStudioFactoryScope native
            ? Task.FromResult(native.ForUser(tenant,subject,entry))
            : factories.GetAsync(tenant,subject,ct);
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
