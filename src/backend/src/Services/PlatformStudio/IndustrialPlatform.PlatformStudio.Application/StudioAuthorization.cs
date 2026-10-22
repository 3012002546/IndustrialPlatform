using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application;

/// <summary>功能权限、资源grant与有效主体共同收口；负责人也不能绕过功能权限。</summary>
public sealed class StudioAuthorization(IStudioResourceStore store, TimeProvider clock) : IStudioAuthorization
{
    public void RequirePermission(StudioPrincipal principal, string permission)
    {
        if (principal.ExpiresOn <= clock.GetUtcNow()) throw new StudioException("STUDIO_UNAUTHENTICATED", "身份已过期，请重新登录。", 401);
        if (!principal.HasPermission(permission)) throw new StudioException("STUDIO_FORBIDDEN", "没有执行该操作的功能权限。", 403);
    }
    public async Task RequireAsync(StudioPrincipal principal, StudioResource resource, string permission, string capability, CancellationToken cancellationToken)
    {
        RequirePermission(principal, permission);
        if (resource.TenantNId != principal.TenantNId) throw new StudioException("STUDIO_NOT_FOUND", "资源不存在。", 404);
        var grants = await store.GetGrantsAsync(principal.TenantNId, resource.Id, cancellationToken);
        if (!grants.Any(grant => grant.RevokedOn is null && grant.Capability == capability && (grant.SubjectKind == "User" && grant.SubjectNId == principal.SubjectNId || grant.SubjectKind == "Role" && principal.Roles.Contains(grant.SubjectNId, StringComparer.Ordinal)))) throw new StudioException("STUDIO_NOT_FOUND", "资源不存在或未授予当前能力。", 404);
    }
}
