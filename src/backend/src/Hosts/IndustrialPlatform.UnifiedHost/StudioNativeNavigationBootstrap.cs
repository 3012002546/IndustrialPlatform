using System.Text.Json;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Domain.ControlPlane;

namespace IndustrialPlatform.UnifiedHost;

/// <summary>平台公开控制面端口的Studio增量种子；只追加自身节点，保留原有草稿和发布快照。</summary>
public sealed class StudioNativeNavigationBootstrap(IConfiguration configuration,IControlPlaneStore store,IControlPlaneTenantSource tenants,IIdentityPermissionRegistry registry,RuntimeSnapshotLoader runtime) : IHostedService
{
    private sealed record Page(string ResourceNId,string RouteName,string Label,string Permission);
    private const string Module="platform-studio";
    private const string Group="navigation.group.studio";
    private static readonly Page[] CorePages=[new("studio.page.sources","studio-sources","数据源","studio.source.view"),new("studio.page.datasets","studio-datasets","数据集","studio.dataset.view")];
    private static readonly Page[] LowCodePages=[new("studio.page.lowcode","studio-lowcode","低代码","studio.lowcode.view"),new("studio.page.ledger-models","studio-ledger-models","台账模型","studio.lowcode.design")];
    private static readonly Page[] DashboardPages=[new("studio.page.dashboards","studio-dashboards","看板","studio.dashboard.view"),new("studio.page.playlists","studio-playlists","轮播","studio.dashboard.view")];
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var profile=configuration.GetSection("PlatformStudio").Get<StudioDeploymentProfile>()??new();
        var tenantIds=(await tenants.GetTenantNIdsAsync(cancellationToken)).Concat(new[] {configuration["SystemData:BaselineTenantNId"],configuration["Identity:Bootstrap:TenantNId"]}.Where(t=>!string.IsNullOrWhiteSpace(t)).Select(t=>t!)).Distinct(StringComparer.Ordinal).ToArray();
        foreach(var tenant in tenantIds)await ApplyAsync(tenant,profile,cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken)=>Task.CompletedTask;
    public async Task ApplyAsync(string tenant,StudioDeploymentProfile profile,CancellationToken cancellationToken)
    {
        var pages=profile.Enabled?CorePages.Concat(profile.ActiveModules.Contains("LowCode",StringComparer.Ordinal)?LowCodePages:Array.Empty<Page>()).Concat(profile.ActiveModules.Contains("Dashboard",StringComparer.Ordinal)?DashboardPages:Array.Empty<Page>()).ToArray():Array.Empty<Page>();
        var permissions=StudioKinds.Permissions.Where(p=>(!p.StartsWith("studio.dashboard.",StringComparison.Ordinal)||profile.ActiveModules.Contains("Dashboard",StringComparer.Ordinal))&&!p.StartsWith("studio.report.",StringComparison.Ordinal)&&!p.StartsWith("studio.trace.",StringComparison.Ordinal)&&!p.StartsWith("studio.workbook.",StringComparison.Ordinal)).ToArray();
        var version=profile.ProfileVersion;var checksum=StudioJson.Hash(JsonSerializer.Serialize(new {version,profile.Enabled,pages,permissions}));
        var key=profile.Enabled?"studio.native.navigation":"studio.native.navigation-disabled";
        if(await store.SeedAppliedAsync(tenant,key,version,checksum,cancellationToken))return;
        var state=await store.LoadAsync(tenant,cancellationToken);
        if(!profile.Enabled&&!state.Manifests.Any(m=>m.ModuleNId==Module))return;
        PermissionRegistrationReceipt? verified=null;
        if(profile.Enabled)
        {
            verified=await registry.VerifyAsync(new(Module,version,checksum,permissions.Select(p=>new PermissionManifestEntry(p,p,p.EndsWith(".view",StringComparison.Ordinal)?"Page":"Action",null)).ToArray()),cancellationToken);
            if(verified is not {Verified:true}||verified.ModuleNId!=Module||verified.ManifestVersion!=version||verified.Checksum!=checksum)throw new InvalidOperationException("Studio权限目录未被Identity验证。");
        }
        var resources=state.Resources.ToList();var nodes=state.DraftNodes.ToList();
        foreach(var page in pages)
        {
            var existing=resources.SingleOrDefault(r=>r.NId==page.ResourceNId);
            if(existing is not null&&existing.OwnerModuleNId!=Module)throw new InvalidOperationException("Studio资源编号与其它模块冲突。");
            if(existing is null)resources.Add(UiResource.Create(tenant,page.ResourceNId,Module,version,UiResourceType.Page,page.Label,page.RouteName,page.Permission,[UiTerminal.Pc]));
            else {resources[resources.IndexOf(existing)]=existing.RebindManifestVersion(version);if(existing.Status==UiResourceStatus.Retired)continue;}
            if(!nodes.Any(n=>n.NId==page.ResourceNId))
            {
                var node=NavigationNode.CreateLink(tenant,page.ResourceNId,page.Label,Group,"PLATFORM_NAVIGATION",page.ResourceNId,null,[UiTerminal.Pc]);node.SetDisplayOrder(Array.IndexOf(pages,page));nodes.Add(node);
            }
        }
        if(pages.Length>0&&!nodes.Any(n=>n.NId==Group)){var group=NavigationNode.CreateGroup(tenant,Group,"工业应用",null,"PLATFORM_NAVIGATION","setting");group.SetDisplayOrder(4);nodes.Add(group);}
        var ownIds=CorePages.Concat(LowCodePages).Concat(DashboardPages).Select(p=>p.ResourceNId).Append(Group).ToHashSet(StringComparer.Ordinal);
        // 已有自定义节点及其它模块节点不覆盖；停用只撤下本模块发布入口，保留草稿和业务数据。
        var active=state.ActiveSnapshotRevision is {} activeRevision?state.Snapshots.SingleOrDefault(s=>s.Revision==activeRevision):null;
        var livePages=pages.Where(p=>resources.Any(r=>r.NId==p.ResourceNId&&r.Status==UiResourceStatus.Active)&&nodes.Any(n=>n.NId==p.ResourceNId&&n.Status==NavigationNodeStatus.Active)).ToArray();
        var selected=livePages.Select(p=>p.ResourceNId).ToHashSet(StringComparer.Ordinal);if(livePages.Length>0)selected.Add(Group);
        var published=(active?.Nodes??[]).Where(n=>!ownIds.Contains(n.NodeNId)||selected.Contains(n.NodeNId)).ToList();
        if(livePages.Length>0)
        {
            if(!published.Any(n=>n.NodeNId==Group))published.Add(new(Group,null,NavigationNodeKind.Group,"工业应用","setting",null,null,null,null,4,[UiTerminal.Pc]));
            foreach(var page in livePages)if(!published.Any(n=>n.NodeNId==page.ResourceNId))published.Add(new(page.ResourceNId,Group,NavigationNodeKind.Link,page.Label,null,page.ResourceNId,page.RouteName,page.Permission,null,Array.IndexOf(pages,page),[UiTerminal.Pc]));
        }
        var nextRevision=state.Snapshots.Select(s=>s.Revision).DefaultIfEmpty(0).Max()+1;var now=DateTimeOffset.UtcNow;
        var manifest=new ModuleManifestState(tenant,Module,version,checksum,permissions,verified?.ManifestVersion,verified?.Checksum,verified?.VerifiedOn)
        {
            PermissionDeclarationItems=permissions.Select(p=>new PermissionManifestEntry(p,p,p.EndsWith(".view",StringComparison.Ordinal)?"Page":"Action",null)).ToArray(),
            ResourceDeclarations=pages.Select(p=>new ModuleResourceDeclaration(p.ResourceNId,version,"Page",p.Label,p.RouteName,p.Permission,[UiTerminal.Pc])).ToArray(),
        };
        var next=state with {Resources=resources,DraftNodes=nodes,Manifests=state.Manifests.Where(m=>m.ModuleNId!=Module).Append(manifest).ToArray(),PermissionReceipts=state.PermissionReceipts.Where(r=>r.ModuleNId!=Module).Concat(profile.Enabled?[new PermissionReceipt(Module,version,checksum,true)]:Array.Empty<PermissionReceipt>()).ToArray(),Snapshots=state.Snapshots.Append(new(nextRevision,now,published,NavigationSnapshotChecksum.Compute(published))).ToArray(),PreviousSnapshotRevision=state.ActiveSnapshotRevision,ActiveSnapshotRevision=nextRevision};
        var payload=JsonSerializer.Serialize(new {moduleNId=Module,revision=nextRevision,profile.Enabled});
        await store.CommitAsync(next,state.Revision,new([new ControlPlaneEvent(Guid.NewGuid(),"SystemData.NavigationPublished.v1","v1",tenant,payload,now)],[new LocalAuditEntry(tenant,"system","studio.navigation.seed","Module",Module,null,null,payload,Guid.NewGuid().ToString("N"))],[new(tenant,key,version,checksum)]),cancellationToken);
        await runtime.InvalidateAsync(tenant,"navigation",cancellationToken);
    }
}
