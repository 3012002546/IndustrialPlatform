using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Contracts.ControlPlane;
using IndustrialPlatform.SystemData.Domain.ControlPlane;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class ControlPlanePersistenceTests
{
    [Fact]
    public async Task A_new_application_service_instance_reads_the_committed_control_plane_state()
    {
        var store = new ControlPlaneTestStore();
        var first = new ResourceNavigationService(store, new TestPermissionRegistry());

        await first.RegisterManifestAsync("tenant-a", new RegisterModuleManifestRequest
        {
            ModuleNId = "module-a",
            ManifestVersion = "1.0.0",
            Checksum = "checksum-a",
            PermissionNIds = ["permission-a"]
        }, CancellationToken.None);
        await first.RegisterResourceAsync("tenant-a", new RegisterUiResourceRequest
        {
            ResourceNId = "resource-a", OwnerModuleNId = "module-a", ManifestVersion = "1.0.0",
            Type = "Page", Name = "页面", RouteName = "route-a", RequiredPermissionNId = "permission-a", SupportedTerminals = ["Pc"]
        }, CancellationToken.None);

        var second = new ResourceNavigationService(store, new TestPermissionRegistry());
        var resources = await second.ListResourcesAsync("tenant-a", CancellationToken.None);

        Assert.Single(resources);
        Assert.Equal("resource-a", resources.Single().ResourceNId);
    }

    [Fact]
    public async Task A_failed_commit_does_not_leave_business_audit_or_outbox_rows()
    {
        var store = new FailingControlPlaneStore();
        store.SeedFeature(FeatureDefinition.Create("tenant-a", "feature-a", "module-a", "功能", false));
        var service = new FeatureControlService(store, new TestPermissionRegistry());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetOverrideAsync(
            "tenant-a", "feature-a", new SetFeatureOverrideRequest { Mode = "Enabled" }, CancellationToken.None));

        Assert.Equal(0, store.Snapshot.Revision);
        Assert.Empty(store.Snapshot.Overrides);
    }

    private sealed class FailingControlPlaneStore : ControlPlaneTestStore
    {
        public override Task<long> CommitAsync(ControlPlaneSnapshot snapshot, long expectedRevision, ControlPlaneCommit commit, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("transaction failed");
        }
    }

}
