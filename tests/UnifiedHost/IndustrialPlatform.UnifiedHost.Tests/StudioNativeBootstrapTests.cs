using System.Reflection;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Domain.ControlPlane;
using IndustrialPlatform.UnifiedHost;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.UnifiedHost.Tests;

public sealed class StudioNativeBootstrapTests
{
    private static readonly string[] BothFactories = ["F1","F2"];
    private static readonly string[] FirstFactory = ["F1"];
    private sealed class Revocations : IndustrialPlatform.Identity.Application.Authentication.ISessionRevocationStore
    {
        public bool Revoked { get; set; }
        public int Reads { get; private set; }
        public Task RevokeAsync(string sessionNId, TimeSpan ttl, CancellationToken token) { Revoked = true; return Task.CompletedTask; }
        public Task<bool> IsRevokedAsync(string sessionNId, CancellationToken token) { Reads++; return Task.FromResult(Revoked); }
    }
    [Fact]
    public async Task Native_request_principal_is_shared_only_within_the_same_http_request()
    {
        var directory = DispatchProxy.Create<IManagementStore, DirectoryProxy>(); var proxy = (DirectoryProxy)(object)directory;
        proxy.User = new(Guid.NewGuid(), "t", "u", "login", "name", null, null, UserStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, [], [], [], 0, Guid.NewGuid(), false);
        var snapshots = new Snapshots(); var revocations = new Revocations(); var profile = new StudioDeploymentProfile();
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new(IndustrialPlatform.Security.ClaimConstants.TenantId,"t"), new(IndustrialPlatform.Security.ClaimConstants.UserNId,"u"),
            new(IndustrialPlatform.Security.ClaimConstants.SessionId,"sid"),new(IndustrialPlatform.Security.ClaimConstants.AuthVersion,"1"),
            new("exp",DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))], "test"));
        var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = user } };
        var identity = new PlatformStudioIdentity(accessor, snapshots, directory, revocations, new PlatformStudioFactoryScope(directory,profile), profile, TimeProvider.System);
        try
        {
            var first = await identity.GetCurrentAsync(default);
            Assert.Same(first, await identity.RefreshAsync(first, default));
            Assert.Equal(1, snapshots.Reads); Assert.Equal(1, proxy.Reads); Assert.Equal(1, revocations.Reads);
            revocations.Revoked = true;
            accessor.HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = user };
            await Assert.ThrowsAsync<StudioException>(() => identity.GetCurrentAsync(default));
            Assert.Equal(2, revocations.Reads);
        }
        finally { accessor.HttpContext = null; }
    }
    [Fact]
    public async Task Background_session_reuses_snapshot_roles_but_rechecks_security_and_scope_every_time()
    {
        var directory = DispatchProxy.Create<IManagementStore, DirectoryProxy>(); var proxy = (DirectoryProxy)(object)directory;
        proxy.User = new(Guid.NewGuid(), "t", "u", "login", "name", null, null, UserStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, [], [], [], 0, Guid.NewGuid(), false);
        var profile = new StudioDeploymentProfile { Identity = new() { NativeFactoryScopes = [new("t", "u", null, ["F1"]), new("t", null, "role", ["F2"])] } };
        var snapshots = new Snapshots(); snapshots.Current = snapshots.Current with { RoleNIds = ["role"] };
        var revoked = new Revocations();
        var identity = new PlatformStudioIdentity(new Microsoft.AspNetCore.Http.HttpContextAccessor(), snapshots, directory, revoked, new PlatformStudioFactoryScope(directory, profile), profile, TimeProvider.System);
        var principal = new StudioPrincipal("t", "u", "platform", "name", [], [], [], "1", profile.Identity.PolicyVersion, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        var evidence = System.Text.Json.JsonSerializer.Serialize(new { principal, sessionId = "sid" }, IndustrialPlatform.PlatformStudio.Application.StudioCommands.JsonOptions);
        Assert.Equal(BothFactories, (await identity.RefreshExecutionAsync(evidence, default)).FactoryNIds);
        snapshots.Current = snapshots.Current with { RoleNIds = [], PermissionNIds = [] };
        var next = await identity.RefreshExecutionAsync(evidence, default);
        Assert.Equal(FirstFactory, next.FactoryNIds); Assert.Empty(next.Permissions);
        Assert.Equal(2, proxy.SecurityReads); Assert.Equal(0, proxy.FullReads);
        proxy.SecurityVersion = 2;
        await Assert.ThrowsAsync<StudioException>(() => identity.RefreshExecutionAsync(evidence, default));
        proxy.SecurityVersion = 1; proxy.User = proxy.User with { IsDeleted = true };
        await Assert.ThrowsAsync<StudioException>(() => identity.RefreshExecutionAsync(evidence, default));
        proxy.User = proxy.User with { IsDeleted = false, Status = UserStatus.Disabled };
        await Assert.ThrowsAsync<StudioException>(() => identity.RefreshExecutionAsync(evidence, default));
        proxy.User = proxy.User with { Status = UserStatus.Active }; revoked.Revoked = true;
        await Assert.ThrowsAsync<StudioException>(() => identity.RefreshExecutionAsync(evidence, default));
    }
    private sealed class Store : IControlPlaneStore,IControlPlaneTenantSource
    {
        public ControlPlaneSnapshot State {get;set;}=ControlPlaneSnapshot.Empty("t");
        private readonly Dictionary<(string,string),string> _seeds=[];
        public Task<ControlPlaneSnapshot> LoadAsync(string tenantNId,CancellationToken cancellationToken)=>Task.FromResult(State);
        public Task<IReadOnlyCollection<string>> GetTenantNIdsAsync(CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyCollection<string>>(["t"]);
        public Task<bool> SeedAppliedAsync(string tenantNId,string seedKey,string seedVersion,string checksum,CancellationToken cancellationToken)
        {
            if(!_seeds.TryGetValue((seedKey,seedVersion),out var existing))return Task.FromResult(false);
            if(existing!=checksum)throw new InvalidOperationException("seed drift");
            return Task.FromResult(true);
        }
        public Task<long> CommitAsync(ControlPlaneSnapshot snapshot,long expectedRevision,ControlPlaneCommit commit,CancellationToken cancellationToken)
        {
            Assert.Equal(State.Revision,expectedRevision);State=snapshot with {Revision=expectedRevision+1};
            foreach(var seed in commit.Seeds??[])_seeds[(seed.SeedKey,seed.SeedVersion)]=seed.Checksum;
            return Task.FromResult(State.Revision);
        }
    }
    private sealed class Registry(bool verified=true) : IIdentityPermissionRegistry
    {
        public Task<PermissionRegistrationReceipt?> VerifyAsync(PermissionManifestV1 manifest,CancellationToken cancellationToken)=>Task.FromResult<PermissionRegistrationReceipt?>(new(manifest.ModuleNId,manifest.ManifestVersion,manifest.Checksum,verified,DateTimeOffset.UtcNow));
    }
    [Fact]
    public async Task Native_menu_seed_preserves_customer_published_nodes_is_idempotent_and_disables_only_studio()
    {
        var store=new Store();
        var custom=new PublishedNavigationNode("customer",null,NavigationNodeKind.Link,"客户入口",null,"customer.page","customer-route","customer.view",null,0,[UiTerminal.Pc]);
        store.State=store.State with {Snapshots=[new(1,DateTimeOffset.UtcNow,[custom])],ActiveSnapshotRevision=1};
        var bootstrap=new StudioNativeNavigationBootstrap(new ConfigurationBuilder().Build(),store,store,new Registry(),new RuntimeSnapshotLoader(store));
        var profile=new StudioDeploymentProfile {Enabled=true,InstalledModules=["LowCode"]};
        await bootstrap.ApplyAsync("t",profile,CancellationToken.None);
        Assert.Contains(store.State.Snapshots.Last().Nodes,n=>n==custom);
        Assert.Equal(4,store.State.Resources.Count);Assert.Equal(5,store.State.DraftNodes.Count);
        Assert.Single(store.State.Manifests);Assert.True(store.State.PermissionReceipts.Single().Verified);
        var revision=store.State.Revision;await bootstrap.ApplyAsync("t",profile,CancellationToken.None);Assert.Equal(revision,store.State.Revision);
        profile.Enabled=false;profile.ProfileVersion="2";await bootstrap.ApplyAsync("t",profile,CancellationToken.None);
        Assert.Equal(custom,Assert.Single(store.State.Snapshots.Last().Nodes));Assert.Equal(4,store.State.Resources.Count);Assert.Equal(5,store.State.DraftNodes.Count);
        profile.Enabled=true;profile.ProfileVersion="3";await bootstrap.ApplyAsync("t",profile,CancellationToken.None);
        Assert.Equal(6,store.State.Snapshots.Last().Nodes.Count);
    }
    [Fact]
    public async Task Missing_identity_permission_receipt_cannot_publish_native_menu()
    {
        var store=new Store();var bootstrap=new StudioNativeNavigationBootstrap(new ConfigurationBuilder().Build(),store,store,new Registry(false),new RuntimeSnapshotLoader(store));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>bootstrap.ApplyAsync("t",new() {Enabled=true,InstalledModules=["LowCode"]},CancellationToken.None));
        Assert.Empty(store.State.Resources);Assert.Empty(store.State.Snapshots);
    }
    [Fact]
    public async Task Report_navigation_upgrades_legacy_seed_fills_only_missing_icons_and_replays_without_duplicates()
    {
        var store=new Store();
        var group=NavigationNode.CreateGroup("t","navigation.group.studio","工业应用",null,"PLATFORM_NAVIGATION","setting");
        var report=NavigationNode.CreateLink("t","studio.page.reports","单报表","navigation.group.studio","PLATFORM_NAVIGATION","studio.page.reports",null,[UiTerminal.Pc]);
        var bundle=NavigationNode.CreateLink("t","studio.page.report-bundles","我的组合","navigation.group.studio","PLATFORM_NAVIGATION","studio.page.report-bundles",null,[UiTerminal.Pc],"tools");
        PublishedNavigationNode[] legacy=[new(group.NId,null,NavigationNodeKind.Group,"工业应用","setting",null,null,null,null,4,[UiTerminal.Pc]),new(report.NId,group.NId,NavigationNodeKind.Link,"单报表",null,report.NId,"studio-reports","studio.report.view",null,0,[UiTerminal.Pc]),new(bundle.NId,group.NId,NavigationNodeKind.Link,"我的组合","tools",bundle.NId,"studio-report-bundles","studio.report.view",null,1,[UiTerminal.Pc])];
        store.State=store.State with {DraftNodes=[group,report,bundle],Snapshots=[new(1,DateTimeOffset.UtcNow,legacy)],ActiveSnapshotRevision=1};
        await store.CommitAsync(store.State,0,new([],[],[new("t","studio.native.navigation","1","legacy-checksum")]),default);
        var bootstrap=new StudioNativeNavigationBootstrap(new ConfigurationBuilder().Build(),store,store,new Registry(),new RuntimeSnapshotLoader(store));
        var profile=new StudioDeploymentProfile {Enabled=true,InstalledModules=["Report"]};
        await bootstrap.ApplyAsync("t",profile,default);
        var nodes=store.State.Snapshots.Last().Nodes;
        Assert.Equal("document",nodes.Single(n=>n.NodeNId==report.NId).IconKey);
        Assert.Equal("tools",nodes.Single(n=>n.NodeNId==bundle.NId).IconKey);
        Assert.Equal("我的组合",nodes.Single(n=>n.NodeNId==bundle.NId).Label);
        Assert.Equal("grid",nodes.Single(n=>n.NodeNId=="studio.page.workbooks").IconKey);
        Assert.Equal("cpu",nodes.Single(n=>n.NodeNId==group.NId).IconKey);
        Assert.Equal("document",report.IconKey);Assert.Equal("tools",bundle.IconKey);
        Assert.Contains(Assert.Single(store.State.Manifests).PermissionDeclarationItems,p=>p.PermissionNId=="studio.report.reconcile");
        var revision=store.State.Revision;
        await bootstrap.ApplyAsync("t",profile,default);Assert.Equal(revision,store.State.Revision);
        Assert.Equal(nodes.Count,nodes.Select(n=>n.NodeNId).Distinct().Count());
    }
    [Fact]
    public async Task Dashboard_only_registers_its_pages_and_verified_permissions_without_lowcode_pages()
    {
        var store = new Store();
        var bootstrap = new StudioNativeNavigationBootstrap(new ConfigurationBuilder().Build(), store, store, new Registry(), new RuntimeSnapshotLoader(store));
        var profile = new StudioDeploymentProfile { Enabled = true, InstalledModules = ["Dashboard"] };

        await bootstrap.ApplyAsync("t", profile, CancellationToken.None);

        var nodes = store.State.Snapshots.Last().Nodes;
        Assert.Equal(4, store.State.Resources.Count);
        Assert.Equal(5, nodes.Count);
        Assert.Contains(nodes, node => node.NodeNId == "studio.page.dashboards" && node.RequiredPermissionNId == "studio.dashboard.view");
        Assert.Contains(nodes, node => node.NodeNId == "studio.page.playlists" && node.RequiredPermissionNId == "studio.dashboard.view");
        Assert.DoesNotContain(nodes, node => node.NodeNId is "studio.page.lowcode" or "studio.page.ledger-models");
        var manifest = Assert.Single(store.State.Manifests);
        Assert.Contains(manifest.PermissionDeclarationItems, item => item.PermissionNId == "studio.dashboard.view");
        Assert.Contains(manifest.PermissionDeclarationItems, item => item.PermissionNId == "studio.dashboard.design");
        Assert.Contains(manifest.PermissionDeclarationItems, item => item.PermissionNId == "studio.dashboard.publish");
        Assert.True(Assert.Single(store.State.PermissionReceipts).Verified);
        var revision = store.State.Revision;
        await bootstrap.ApplyAsync("t", profile, CancellationToken.None);
        Assert.Equal(revision, store.State.Revision);
    }

    [Fact]
    public async Task Disabling_dashboard_withdraws_only_its_live_entries_and_retains_saved_resources()
    {
        var store = new Store();
        var custom = new PublishedNavigationNode("customer", null, NavigationNodeKind.Link, "客户入口", null, "customer.page", "customer-route", "customer.view", null, 0, [UiTerminal.Pc]);
        store.State = store.State with { Snapshots = [new(1, DateTimeOffset.UtcNow, [custom])], ActiveSnapshotRevision = 1 };
        var bootstrap = new StudioNativeNavigationBootstrap(new ConfigurationBuilder().Build(), store, store, new Registry(), new RuntimeSnapshotLoader(store));
        var profile = new StudioDeploymentProfile { Enabled = true, InstalledModules = ["LowCode", "Dashboard"] };
        await bootstrap.ApplyAsync("t", profile, CancellationToken.None);
        Assert.Equal(6, store.State.Resources.Count);

        profile.EnabledModules = ["LowCode"];
        profile.ProfileVersion = "2";
        await bootstrap.ApplyAsync("t", profile, CancellationToken.None);

        var nodes = store.State.Snapshots.Last().Nodes;
        Assert.Contains(nodes, node => node == custom);
        Assert.Contains(nodes, node => node.NodeNId == "studio.page.lowcode");
        Assert.DoesNotContain(nodes, node => node.NodeNId is "studio.page.dashboards" or "studio.page.playlists");
        Assert.DoesNotContain(Assert.Single(store.State.Manifests).PermissionDeclarationItems, item => item.PermissionNId.StartsWith("studio.dashboard.", StringComparison.Ordinal));
        Assert.Equal(6, store.State.Resources.Count);
        Assert.Contains(store.State.DraftNodes, node => node.NId == "studio.page.dashboards");

        profile.EnabledModules = ["LowCode", "Dashboard"];
        profile.ProfileVersion = "3";
        await bootstrap.ApplyAsync("t", profile, CancellationToken.None);
        Assert.Contains(store.State.Snapshots.Last().Nodes, node => node.NodeNId == "studio.page.dashboards");
        Assert.Contains(store.State.Snapshots.Last().Nodes, node => node == custom);
        Assert.Equal(6, store.State.Resources.Count);
    }
    public class DirectoryProxy : DispatchProxy
    {
        public StoredUser? User {get;set;}
        public int SecurityVersion {get;set;}=1;
        public int Reads {get;private set;}
        public int SecurityReads {get;private set;}
        public int FullReads {get;private set;}
        protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)
        {
            if(targetMethod?.Name=="GetUserSecurityStateAsync")
            {
                Reads++; SecurityReads++;
                return Task.FromResult(User is null ? null : new StoredUserSecurityState(User.TenantNId,User.NId,User.Name,User.Status,User.IsDeleted,SecurityVersion));
            }
            if(targetMethod?.Name!="GetUserAsync")throw new NotSupportedException();
            Reads++;FullReads++;return Task.FromResult(User);
        }
    }
    private sealed class Snapshots : IAuthorizationDataStore
    {
        public AuthorizationSnapshot Current {get;set;}=new("t","u",UserStatus.Active,1,["studio.dashboard.publish"]);
        public int Reads {get;private set;}
        public Task<AuthorizationSnapshot?> GetSnapshotAsync(string tenantNId,string userNId,CancellationToken cancellationToken)
        { Reads++;return Task.FromResult<AuthorizationSnapshot?>(Current); }
    }
    [Fact]
    public async Task Native_publisher_reads_directory_once_per_request_without_caching_roles_or_authority()
    {
        var directory=DispatchProxy.Create<IManagementStore,DirectoryProxy>();var proxy=(DirectoryProxy)(object)directory;
        proxy.User=new(Guid.NewGuid(),"t","u","login","name",null,null,UserStatus.Active,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null,false,[],["group-role"],["group-role"],0,Guid.NewGuid(),false);
        var profile=new StudioDeploymentProfile {Enabled=true,InstalledModules=["Dashboard"],Identity=new() {NativeFactoryScopes=[new("t","u",null,["F1"]),new("t",null,"group-role",["F2"])]}};
        var snapshots=new Snapshots();
        var identity=new PlatformStudioIdentity(new Microsoft.AspNetCore.Http.HttpContextAccessor(),snapshots,directory,null!,new PlatformStudioFactoryScope(directory,profile),profile,TimeProvider.System);
        var link=new StudioScreenLink {TenantNId="t",CreatedBy="u",IssuerKey="platform",AuthVersion="1",NeverExpires=true};
        var first=await identity.GetPublisherAsync(link,CancellationToken.None);
        Assert.Equal(BothFactories,first.FactoryNIds);Assert.Equal(1,proxy.Reads);
        proxy.User=proxy.User with {EffectiveRoleNIds=[]};
        snapshots.Current=snapshots.Current with {PermissionNIds=[]};
        var second=await identity.GetPublisherAsync(link,CancellationToken.None);
        Assert.Equal(FirstFactory,second.FactoryNIds);Assert.Empty(second.Permissions);
        Assert.Equal(2,proxy.Reads);Assert.Equal(2,snapshots.Reads);
        snapshots.Current=snapshots.Current with {AuthVersion=2};
        await Assert.ThrowsAsync<StudioException>(()=>identity.GetPublisherAsync(link,CancellationToken.None));
        snapshots.Current=snapshots.Current with {AuthVersion=1};
        proxy.User=proxy.User with {IsDeleted=true};
        await Assert.ThrowsAsync<StudioException>(()=>identity.GetPublisherAsync(link,CancellationToken.None));
        proxy.User=proxy.User with {IsDeleted=false,Status=UserStatus.Disabled};
        await Assert.ThrowsAsync<StudioException>(()=>identity.GetPublisherAsync(link,CancellationToken.None));
    }
    [Fact]
    public async Task Native_publisher_reuses_current_role_facts_but_rechecks_user_state_and_security_version()
    {
        var directory=DispatchProxy.Create<IManagementStore,DirectoryProxy>();var proxy=(DirectoryProxy)(object)directory;
        proxy.User=new(Guid.NewGuid(),"t","u","login","name",null,null,UserStatus.Active,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null,false,[],[],[],0,Guid.NewGuid(),false);
        var profile=new StudioDeploymentProfile {Enabled=true,InstalledModules=["Dashboard"],Identity=new() {NativeFactoryScopes=[new("t","u",null,["F1"]),new("t",null,"group-role",["F2"])]}};
        var snapshots=new Snapshots();snapshots.Current=snapshots.Current with {RoleNIds=["group-role"]};
        var identity=new PlatformStudioIdentity(new Microsoft.AspNetCore.Http.HttpContextAccessor(),snapshots,directory,null!,new PlatformStudioFactoryScope(directory,profile),profile,TimeProvider.System);
        var link=new StudioScreenLink {TenantNId="t",CreatedBy="u",IssuerKey="platform",AuthVersion="1",NeverExpires=true};
        Assert.Equal(BothFactories,(await identity.GetPublisherAsync(link,CancellationToken.None)).FactoryNIds);
        snapshots.Current=snapshots.Current with {RoleNIds=[],PermissionNIds=[]};
        var refreshed=await identity.GetPublisherAsync(link,CancellationToken.None);
        Assert.Equal(FirstFactory,refreshed.FactoryNIds);Assert.Empty(refreshed.Permissions);Assert.Equal(2,proxy.Reads);
        proxy.User=proxy.User with {IsDeleted=true};
        await Assert.ThrowsAsync<StudioException>(()=>identity.GetPublisherAsync(link,CancellationToken.None));
        proxy.User=proxy.User with {IsDeleted=false,Status=UserStatus.Disabled};
        await Assert.ThrowsAsync<StudioException>(()=>identity.GetPublisherAsync(link,CancellationToken.None));
        proxy.User=proxy.User with {Status=UserStatus.Active};proxy.SecurityVersion=2;
        await Assert.ThrowsAsync<StudioException>(()=>identity.GetPublisherAsync(link,CancellationToken.None));
    }
    [Fact]
    public async Task Native_factory_scope_uses_effective_roles_and_current_tenant_without_all_fallback()
    {
        var directory=DispatchProxy.Create<IManagementStore,DirectoryProxy>();var proxy=(DirectoryProxy)(object)directory;
        proxy.User=new(Guid.NewGuid(),"t","u","login","name",null,null,UserStatus.Active,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null,false,[],["group-role"],["group-role"],0,Guid.NewGuid(),false);
        var profile=new StudioDeploymentProfile {Enabled=true,InstalledModules=["LowCode"],Identity=new() {NativeFactoryScopes=[new("t","u",null,["F1"]),new("t",null,"group-role",["F2"])]}};
        var scope=new PlatformStudioFactoryScope(directory,profile);
        Assert.Equal(BothFactories,await scope.GetAsync("t","u",CancellationToken.None));
        Assert.Empty(await scope.GetAsync("other","u",CancellationToken.None));
        proxy.User=proxy.User with {EffectiveRoleNIds=[]};
        Assert.Equal(FirstFactory,await scope.GetAsync("t","u",CancellationToken.None));
        profile.Identity.NativeFactoryScopes=[];
        Assert.Empty(await scope.GetAsync("t","u",CancellationToken.None));
    }
}
