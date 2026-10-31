using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private static readonly object RequestPrincipalKey = new();
    public Task<StudioPrincipal> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var http = accessor.HttpContext;
        // The endpoint filter and nested services share one authenticated
        // request. Never reuse this result across requests or background jobs.
        if (http?.Items.TryGetValue(RequestPrincipalKey, out var cached) == true && cached is Task<StudioPrincipal> current) return current;
        var read = ReadCurrentAsync(cancellationToken);
        if (http is not null) http.Items[RequestPrincipalKey] = read;
        return read;
    }
    private async Task<StudioPrincipal> ReadCurrentAsync(CancellationToken cancellationToken)
    {
        var user=accessor.HttpContext?.User;
        if(user?.Identity?.IsAuthenticated!=true) throw new StudioException("STUDIO_UNAUTHENTICATED","请登录平台。",401);
        var tenant=user.FindFirst(ClaimConstants.TenantId)?.Value;var subject=user.FindFirst(ClaimConstants.UserNId)?.Value;var session=user.FindFirst(ClaimConstants.SessionId)?.Value;
        if(string.IsNullOrWhiteSpace(tenant)||string.IsNullOrWhiteSpace(subject)||string.IsNullOrWhiteSpace(session)||!int.TryParse(user.FindFirst(ClaimConstants.AuthVersion)?.Value,NumberStyles.None,CultureInfo.InvariantCulture,out var version)) throw new StudioException("STUDIO_UNAUTHENTICATED","平台会话声明不完整。",401);
        var expires=long.TryParse(user.FindFirst("exp")?.Value,NumberStyles.None,CultureInfo.InvariantCulture,out var epoch)?DateTimeOffset.FromUnixTimeSeconds(epoch):clock.GetUtcNow();
        return await ReadSessionPrincipalAsync(tenant,subject,session,version,expires,cancellationToken);
    }
    public Task<string> CaptureExecutionEvidenceAsync(StudioPrincipal principal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var session = user?.FindFirst(ClaimConstants.SessionId)?.Value;
        if (user?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(session) || user.FindFirst(ClaimConstants.TenantId)?.Value != principal.TenantNId || user.FindFirst(ClaimConstants.UserNId)?.Value != principal.SubjectNId) throw new StudioException("STUDIO_UNAUTHENTICATED", "缺少原平台执行会话。", 401);
        return Task.FromResult(JsonSerializer.Serialize(new { principal, sessionId = session }, StudioCommands.JsonOptions));
    }
    public async Task<StudioPrincipal> RefreshExecutionAsync(string evidence, CancellationToken cancellationToken)
    {
        var value = JsonNode.Parse(evidence)!.AsObject();
        var principal = value["principal"]!.Deserialize<StudioPrincipal>(StudioCommands.JsonOptions)!;
        return await ReadSessionPrincipalAsync(principal.TenantNId, principal.SubjectNId, StudioJson.RequiredString(value,"sessionId"), int.Parse(principal.AuthVersion,CultureInfo.InvariantCulture), principal.ExpiresOn, cancellationToken);
    }
    private async Task<StudioPrincipal> ReadSessionPrincipalAsync(string tenant,string subject,string session,int version,DateTimeOffset expires,CancellationToken cancellationToken)
    {
        try
        {
            if(await revocations.IsRevokedAsync(session,cancellationToken)) throw new StudioException("STUDIO_UNAUTHENTICATED","平台会话已撤销。",401);
            var snapshot=await snapshots.GetSnapshotAsync(tenant,subject,cancellationToken);
            if(snapshot is null||snapshot.Status!=UserStatus.Active||snapshot.AuthVersion!=version) throw new StudioException("STUDIO_UNAUTHENTICATED","平台用户状态或安全版本已改变。",401);
            var now=clock.GetUtcNow();
            if(expires<=now) throw new StudioException("STUDIO_UNAUTHENTICATED","平台会话已过期。",401);
            // The authority snapshot already contains effective roles. Reuse
            // those facts, as screen publishing does, while independently
            // rereading the user's deletion/status/security version each time.
            if (snapshot.RoleNIds is { } roles && factories is PlatformStudioFactoryScope native)
            {
                var state = await directory.GetUserSecurityStateAsync(subject, cancellationToken);
                if (snapshot.TenantNId != tenant || snapshot.UserNId != subject || state is null || state.NId != subject || state.IsDeleted || state.TenantNId != tenant || state.Status != UserStatus.Active || state.AuthVersion != version)
                    throw new StudioException("STUDIO_UNAUTHENTICATED", "平台主体已不可用。", 401);
                return new(tenant, subject, "platform", state.Name, roles, snapshot.PermissionNIds, native.ForUser(tenant, subject, state, roles), version.ToString(CultureInfo.InvariantCulture), profile.Identity.PolicyVersion, now, expires);
            }
            var entry=await directory.GetUserAsync(subject,cancellationToken);
            if(entry is null||entry.IsDeleted||entry.TenantNId!=tenant||entry.Status!=UserStatus.Active) throw new StudioException("STUDIO_UNAUTHENTICATED","平台主体已不可用。",401);
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
