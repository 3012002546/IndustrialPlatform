using IndustrialPlatform.PlatformStudio.Application.Integration;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Publishing;

/// <summary>资源能力授权与可信功能权限分别校验；所有者的维护通路不能被普通覆盖保存移除。</summary>
public sealed class StudioGrants(IStudioResourceStore store,StudioPublishing publishing,IStudioAuthorization authorization,IStudioSubjectDirectory directory,TimeProvider clock)
{
    private static readonly string[] Capabilities=["View","Design","Publish","Run","Export"];
    public async Task<IReadOnlyList<ResourceGrant>> GetAsync(StudioPrincipal principal,string nId,CancellationToken ct)
    {
        authorization.RequirePermission(principal,"studio.resource.grant");
        var resource=await publishing.RequireResourceAsync(principal,nId,"design","Design",ct);
        return (await store.GetGrantsAsync(principal.TenantNId,resource.Id,ct)).Select(g=>new ResourceGrant(g.SubjectKind,g.SubjectNId,g.Capability,g.RevokedOn)).ToArray();
    }
    public async Task<ResourceDto> SaveAsync(StudioPrincipal principal,string nId,GrantSave request,CancellationToken ct)
    {
        authorization.RequirePermission(principal,"studio.resource.grant");
        var resource=await publishing.RequireResourceAsync(principal,nId,"design","Design",ct);
        StudioPublishing.GuardWritable(resource);StudioPublishing.MatchVersion(resource,request);
        var grants=request.Grants??throw new StudioException("STUDIO_VALIDATION","授权列表缺失。");
        if(grants.Count>1000||grants.Any(g=>g.SubjectKind is not ("User" or "Role")||!Capabilities.Contains(g.Capability,StringComparer.Ordinal)||string.IsNullOrWhiteSpace(g.SubjectNId)||g.SubjectNId.Length>128||g.RevokedOn is not null)||grants.Select(g=>(g.SubjectKind,g.SubjectNId,g.Capability)).Distinct().Count()!=grants.Count) throw new StudioException("STUDIO_VALIDATION","授权主体、能力或列表无效。");
        foreach(var grant in grants)
            if(!await directory.ExistsAsync(principal,grant.SubjectKind,grant.SubjectNId,ct)) throw new StudioException("STUDIO_VALIDATION","授权主体未在当前租户验证。",path:"grants.subjectNId");
        if(Capabilities.Any(capability=>!grants.Any(g=>g.SubjectKind=="User"&&g.SubjectNId==resource.OwnerUserNId&&g.Capability==capability))) throw new StudioException("STUDIO_VALIDATION","必须保留资源所有者的能力。",path:"grants");
        return await store.TransactionAsync(async ()=>
        {
            var version=resource.OptimisticVersion;var token=resource.ConcurrencyVersion;resource.MarkChanged(clock.GetUtcNow());
            if(!await store.UpdateResourceAsync(resource,version,token,ct)) throw new StudioException("STUDIO_VERSION_CONFLICT","资源授权已被修改。",409);
            var now=clock.GetUtcNow();
            await store.ReplaceGrantsAsync(principal.TenantNId,resource.Id,grants.Select(g=>new StudioGrant { NId="grant-"+Guid.NewGuid().ToString("N"),TenantNId=principal.TenantNId,ResourceId=resource.Id,SubjectKind=g.SubjectKind,SubjectNId=g.SubjectNId,Capability=g.Capability,GrantedBy=principal.SubjectNId,CreatedOn=now }).ToArray(),ct);
            await store.AppendAuditAsync(principal,"studio.resource.grant",nId,new() { ["grantCount"]=grants.Count },ct);
            return await publishing.ToDtoAsync(resource,ct);
        },ct);
    }
}
