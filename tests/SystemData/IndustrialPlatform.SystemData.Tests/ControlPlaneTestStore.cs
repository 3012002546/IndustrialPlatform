using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Domain.ControlPlane;

namespace IndustrialPlatform.SystemData.Tests;

internal class ControlPlaneTestStore : IControlPlaneStore
{
    public ControlPlaneSnapshot Snapshot { get; private set; } = ControlPlaneSnapshot.Empty("tenant-a");
    public virtual Task<ControlPlaneSnapshot> LoadAsync(string tenantNId, CancellationToken cancellationToken) => Task.FromResult(Snapshot with { TenantNId = tenantNId });
    public virtual Task<long> CommitAsync(ControlPlaneSnapshot snapshot, long expectedRevision, ControlPlaneCommit commit, CancellationToken cancellationToken)
    {
        if (Snapshot.Revision != expectedRevision) throw new InvalidOperationException("revision conflict");
        Snapshot = snapshot with { Revision = expectedRevision + 1 };
        return Task.FromResult(Snapshot.Revision);
    }
    public Task<bool> SeedAppliedAsync(string tenantNId, string seedKey, string seedVersion, string checksum, CancellationToken cancellationToken) => Task.FromResult(false);
    public void SeedFeature(FeatureDefinition feature) => Snapshot = Snapshot with { Features = Snapshot.Features.Append(feature).ToArray() };
    public void SeedCatalog(ServiceCatalogEntry entry) => Snapshot = Snapshot with { Catalog = Snapshot.Catalog.Append(entry).ToArray() };
}

internal sealed class TestPermissionRegistry : IIdentityPermissionRegistry
{
    public Task<PermissionRegistrationReceipt?> VerifyAsync(PermissionManifestV1 manifest, CancellationToken cancellationToken) => Task.FromResult<PermissionRegistrationReceipt?>(new PermissionRegistrationReceipt(manifest.ModuleNId, manifest.ManifestVersion, manifest.Checksum, true, DateTimeOffset.UtcNow));
}
