using System.Reflection;
using IndustrialPlatform.Identity.Application.Management;
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
    public class DirectoryProxy : DispatchProxy
    {
        public StoredUser? User {get;set;}
        protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>targetMethod?.Name=="GetUserAsync"?Task.FromResult(User):throw new NotSupportedException();
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
