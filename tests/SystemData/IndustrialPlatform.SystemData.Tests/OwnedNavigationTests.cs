using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Contracts.ControlPlane;
using IndustrialPlatform.SystemData.Domain.ControlPlane;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class OwnedNavigationTests
{
    private static readonly JsonSerializerOptions LegacyOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    [Fact]
    public void Legacy_snapshot_checksum_is_unchanged_when_owned_metadata_is_absent()
    {
        var node = new PublishedNavigationNode("g", null, NavigationNodeKind.Group, "目录", null, null, null, null, null, 0, [UiTerminal.Pc]);
        var legacy = new[] { new { node.NodeNId, node.ParentNodeNId, node.Kind, node.Label, node.IconKey, node.ResourceNId, node.RouteName, node.RequiredPermissionNId, node.FeatureNId, node.DisplayOrder, node.VisibleTerminals, node.ActionResourceNIds } };
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(legacy, LegacyOptions))));
        Assert.Equal(expected, NavigationSnapshotChecksum.Compute([node]));
    }

    [Fact]
    public async Task Targeted_mount_preserves_other_drafts_and_detach_survives_publication_and_rollback()
    {
        var ct = CancellationToken.None;
        var store = new ControlPlaneTestStore();
        var service = new ResourceNavigationService(store, new TestPermissionRegistry());
        await service.RegisterManifestAsync("tenant-a", new RegisterModuleManifestRequest {
            ModuleNId = "studio", ManifestVersion = "1", Checksum = "v1", PermissionNIds = ["studio.run"],
            Resources = [new RegisterUiResourceRequest { ResourceNId = "studio.runtime", OwnerModuleNId = "studio", ManifestVersion = "1", Type = "Page", Name = "Runtime", RouteName = "runtime", RequiredPermissionNId = "studio.run", SupportedTerminals = ["Pc"] }]
        }, ct);
        await service.AddNodeAsync("tenant-a", new CreateNavigationNodeRequest { NodeNId = "group", Kind = "Group", Label = "正式目录", ExpectedDraftRevision = store.Snapshot.Revision }, ct);
        await service.AddNodeAsync("tenant-a", new CreateNavigationNodeRequest { NodeNId = "baseline", Kind = "Link", Label = "原有入口", ParentNodeNId = "group", ResourceNId = "studio.runtime", VisibleTerminals = ["Pc"], ExpectedDraftRevision = store.Snapshot.Revision }, ct);
        await service.PublishAsync("tenant-a", "actor", store.Snapshot.Revision, ct);
        await service.AddNodeAsync("tenant-a", new CreateNavigationNodeRequest { NodeNId = "unpublished", Kind = "Link", Label = "其他草稿", ResourceNId = "studio.runtime", VisibleTerminals = ["Pc"], ExpectedDraftRevision = store.Snapshot.Revision }, ct);
        var result = await service.MountAsync("tenant-a", "actor", "studio", "page-a", "studio.runtime", new(null, "group", "业务页", "editpen", 1, store.Snapshot.Revision), ct);
        var link = Assert.Single(result.Links);
        var runtime = await service.RuntimeAsync("tenant-a", UiTerminal.Pc, ct);
        Assert.DoesNotContain(runtime.Nodes, n => n.NodeNId == "unpublished");
        Assert.Equal("page-a", Assert.Single(Assert.Single(runtime.Nodes).Children, n => n.NodeNId == link.NodeNId).RouteParams!["nId"]);
        Assert.Contains(store.Snapshot.DraftNodes, n => n.NId == "unpublished");
        await service.DeleteNodeAsync("tenant-a", link.NodeNId, store.Snapshot.Revision, ct);
        Assert.True(Assert.Single((await service.ReadAsync("tenant-a", "studio", "page-a", ct)).Links).Detached);
        Assert.DoesNotContain(Assert.Single((await service.RuntimeAsync("tenant-a", UiTerminal.Pc, ct)).Nodes).Children, n => n.NodeNId == link.NodeNId);
        await service.RollbackAsync("tenant-a", "actor", store.Snapshot.Revision, ct);
        Assert.True(Assert.Single((await service.ReadAsync("tenant-a", "studio", "page-a", ct)).Links).Detached);
        await service.PublishAsync("tenant-a", "actor", store.Snapshot.Revision, ct);
        Assert.True(Assert.Single((await service.ReadAsync("tenant-a", "studio", "page-a", ct)).Links).Detached);
        var options = await service.ReadAsync("tenant-a", "studio", "page-a", ct);
        await service.MountAsync("tenant-a", "actor", "studio", "page-a", "studio.runtime", new(link.NodeNId, "group", "重新挂载", null, 0, options.Revision), ct);
        Assert.False(Assert.Single((await service.ReadAsync("tenant-a", "studio", "page-a", ct)).Links).Detached);
        await Assert.ThrowsAsync<IndustrialPlatform.SharedKernel.Exceptions.ValidationException>(() => service.MountAsync("tenant-a", "actor", "studio", "other-page", "studio.runtime", new(link.NodeNId, "group", "wrong owner", null, 0, store.Snapshot.Revision), ct));
    }
}
