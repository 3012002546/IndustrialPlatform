using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Contracts.ControlPlane;
using IndustrialPlatform.SystemData.Domain.ControlPlane;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class ResourceNavigationFeatureTests
{
    [Fact]
    public void Page_resource_requires_route_and_permission() => Assert.Throws<ArgumentException>(() => UiResource.Create("tenant-a", "resource-a", "module-a", "1", UiResourceType.Page, "页面", null, null, [UiTerminal.Pc]));

    [Fact]
    public void Navigation_validator_rejects_retired_resource_and_empty_group()
    {
        var resource = UiResource.Create("tenant-a", "resource-a", "module-a", "1", UiResourceType.Page, "页面", "route-a", "permission-a", [UiTerminal.Pc]);
        resource.Retire();
        var nodes = new[] { NavigationNode.CreateGroup("tenant-a", "group-a", "分组", null, "menu"), NavigationNode.CreateLink("tenant-a", "link-a", "页面", "group-a", "menu", "resource-a", null, [UiTerminal.Pc]) };
        var result = NavigationValidator.Validate(nodes, [resource], [], new PermissionReceipt("module-a", "1", "checksum", true));
        Assert.Contains(result.Errors, error => error.Code == "RESOURCE_RETIRED");
    }

    [Fact]
    public void Navigation_node_preserves_safe_icon_key_for_runtime_projection()
    {
        var node = NavigationNode.CreateLink(
            "tenant-a",
            "link-a",
            "页面",
            null,
            "menu",
            "resource-a",
            null,
            [UiTerminal.Pc],
            "database");

        Assert.Equal("database", node.IconKey);
        Assert.Throws<ArgumentException>(() => NavigationNode.CreateLink(
            "tenant-a",
            "link-b",
            "页面",
            null,
            "menu",
            "resource-a",
            null,
            [UiTerminal.Pc],
            "<script>"));
    }

    [Fact]
    public void Feature_effective_value_obeys_emergency_override_first()
    {
        var definition = FeatureDefinition.Create("tenant-a", "feature-a", "module-a", "功能", true);
        Assert.False(definition.Resolve(FeatureOverride.Create("tenant-a", "feature-a", FeatureOverrideMode.Enabled, "tenant reason"), ["feature-a"]));
    }

    [Fact]
    public void Feature_declaration_preserves_description_and_revision()
    {
        var definition = FeatureDefinition.Create("tenant-a", "feature-a", "module-a", "功能", true, "用于测试的功能开关").WithRevision(7);

        Assert.Equal("用于测试的功能开关", definition.Description);
        Assert.Equal(7, definition.FeatureRevision);
    }

    [Fact]
    public async Task Feature_service_registers_a_module_declaration_idempotently()
    {
        var store = new ControlPlaneTestStore();
        var service = new FeatureControlService(store, new TestPermissionRegistry());
        var request = new RegisterFeatureDefinitionRequest
        {
            FeatureNId = "feature-a",
            OwnerModuleNId = "module-a",
            Name = "功能",
            Description = "声明",
            DefaultEnabled = true,
        };

        var first = await service.RegisterAsync("tenant-a", request, CancellationToken.None);
        var again = await service.RegisterAsync("tenant-a", request, CancellationToken.None);

        Assert.Equal("声明", first.Description);
        Assert.Equal(first.FeatureRevision, again.FeatureRevision);
        Assert.Single(await service.ListAsync("tenant-a", CancellationToken.None));
    }

    [Fact]
    public void Theme_policy_requires_defaults_to_be_allowed() => Assert.Throws<ArgumentException>(() => ThemePolicy.Create("tenant-a", [ThemePalette.IndustrialCyan], [ThemeMode.Light], [PcDensity.Compact], ThemePalette.TechnologyBlue, ThemeMode.Light, PcDensity.Compact));

    [Fact]
    public async Task Navigation_publish_creates_runtime_snapshot_and_keeps_previous_snapshot()
    {
        var store = new ControlPlaneTestStore();
        var service = new ResourceNavigationService(store, new TestPermissionRegistry());
        await service.RegisterManifestAsync("tenant-a", new RegisterModuleManifestRequest { ModuleNId = "module-a", ManifestVersion = "1", Checksum = "checksum", PermissionNIds = ["permission-a"] }, CancellationToken.None);
        await service.RegisterResourceAsync("tenant-a", new RegisterUiResourceRequest { ResourceNId = "resource-a", OwnerModuleNId = "module-a", ManifestVersion = "1", Type = "Page", Name = "页面", RouteName = "route-a", RequiredPermissionNId = "permission-a", SupportedTerminals = ["Pc"] }, CancellationToken.None);
        await service.AddNodeAsync("tenant-a", new CreateNavigationNodeRequest { NodeNId = "group-a", Kind = "Group", Label = "分组", VisibleTerminals = ["Pc"], ExpectedDraftRevision = 2 }, CancellationToken.None);
        await service.AddNodeAsync("tenant-a", new CreateNavigationNodeRequest { NodeNId = "link-a", Kind = "Link", Label = "页面", ParentNodeNId = "group-a", ResourceNId = "resource-a", VisibleTerminals = ["Pc"], ExpectedDraftRevision = 3 }, CancellationToken.None);
        var firstRevision = await service.PublishAsync("tenant-a", "user-a", 4, CancellationToken.None);
        var runtime = await service.RuntimeAsync("tenant-a", UiTerminal.Pc, CancellationToken.None);
        Assert.Equal(firstRevision, runtime.Revision);
        Assert.Single(runtime.Nodes);
        Assert.Single(runtime.Nodes.Single().Children);
        await Assert.ThrowsAsync<ValidationException>(() => service.RollbackAsync("tenant-a", "user-a", firstRevision, CancellationToken.None));
    }

    [Fact]
    public async Task Navigation_action_associations_round_trip_and_reject_page_resources()
    {
        var store = new ControlPlaneTestStore();
        var service = new ResourceNavigationService(store, new TestPermissionRegistry());
        await service.RegisterManifestAsync("tenant-actions", new RegisterModuleManifestRequest
        {
            ModuleNId = "module-actions",
            ManifestVersion = "1",
            Checksum = "actions-checksum",
            Permissions =
            [
                new PermissionDeclarationRequest { PermissionNId = "page.view", Name = "查看页面", ResourceType = "Page" },
                new PermissionDeclarationRequest { PermissionNId = "action.execute", Name = "执行操作", ResourceType = "Action" },
            ],
            Resources =
            [
                new RegisterUiResourceRequest
                {
                    ResourceNId = "page-a",
                    OwnerModuleNId = "module-actions",
                    ManifestVersion = "1",
                    Type = "Page",
                    Name = "页面 A",
                    RouteName = "page-a",
                    RequiredPermissionNId = "page.view",
                    SupportedTerminals = ["Pc"],
                },
                new RegisterUiResourceRequest
                {
                    ResourceNId = "action-a",
                    OwnerModuleNId = "module-actions",
                    ManifestVersion = "1",
                    Type = "Action",
                    Name = "操作 A",
                    RequiredPermissionNId = "action.execute",
                    SupportedTerminals = ["Pc"],
                },
            ],
        }, CancellationToken.None);

        await service.AddNodeAsync("tenant-actions", new CreateNavigationNodeRequest
        {
            NodeNId = "page-menu-a",
            Kind = "Link",
            Label = "页面 A",
            ResourceNId = "page-a",
            VisibleTerminals = ["Pc"],
            ActionResourceNIds = ["action-a"],
            ExpectedDraftRevision = 1,
        }, CancellationToken.None);

        var draft = await new ResourceNavigationService(store, new TestPermissionRegistry())
            .GetDraftAsync("tenant-actions", CancellationToken.None);
        var node = Assert.Single(draft.Nodes);
        Assert.Equal(["action-a"], node.ActionResourceNIds);
        Assert.True((await service.ValidateAsync("tenant-actions", CancellationToken.None)).IsValid);

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateNodeAsync("tenant-actions", "page-menu-a", new UpdateNavigationNodeRequest
        {
            Label = "页面 A",
            ResourceNId = "page-a",
            VisibleTerminals = ["Pc"],
            ActionResourceNIds = ["page-a"],
            ExpectedDraftRevision = 2,
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Navigation_add_allows_three_levels_but_rejects_a_fourth_level()
    {
        var service = new ResourceNavigationService(new ControlPlaneTestStore(), new TestPermissionRegistry());

        await service.AddNodeAsync("tenant-depth", new CreateNavigationNodeRequest
        {
            NodeNId = "level-1",
            Kind = "Group",
            Label = "一级",
            VisibleTerminals = ["Pc"],
            ExpectedDraftRevision = 0,
        }, CancellationToken.None);
        await service.AddNodeAsync("tenant-depth", new CreateNavigationNodeRequest
        {
            NodeNId = "level-2",
            Kind = "Group",
            Label = "二级",
            ParentNodeNId = "level-1",
            VisibleTerminals = ["Pc"],
            ExpectedDraftRevision = 1,
        }, CancellationToken.None);
        await service.AddNodeAsync("tenant-depth", new CreateNavigationNodeRequest
        {
            NodeNId = "level-3",
            Kind = "Group",
            Label = "三级",
            ParentNodeNId = "level-2",
            VisibleTerminals = ["Pc"],
            ExpectedDraftRevision = 2,
        }, CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() => service.AddNodeAsync("tenant-depth", new CreateNavigationNodeRequest
        {
            NodeNId = "level-4",
            Kind = "Group",
            Label = "四级",
            ParentNodeNId = "level-3",
            VisibleTerminals = ["Pc"],
            ExpectedDraftRevision = 3,
        }, CancellationToken.None));
    }

    [Fact]
    public void Navigation_snapshot_checksum_is_stable_for_the_same_nodes()
    {
        var nodes = new[] { new PublishedNavigationNode("node-a", null, NavigationNodeKind.Group, "节点", "database", null, null, null, null, 0, [UiTerminal.Pc]) };

        Assert.Equal(NavigationSnapshotChecksum.Compute(nodes), NavigationSnapshotChecksum.Compute(nodes));
        Assert.NotEqual(NavigationSnapshotChecksum.Compute(nodes), NavigationSnapshotChecksum.Compute(nodes.Select(x => x with { Label = "不同" }).ToArray()));
    }

    [Theory]
    [InlineData("http://example.test")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://localhost")]
    public void External_catalog_entry_rejects_unsafe_endpoint(string endpoint) => Assert.Throws<ArgumentException>(() => ServiceCatalogEntry.CreateExternal("tenant-a", "service-a", "服务", endpoint, [UiTerminal.Pc]));

    [Fact]
    public void Service_catalog_keeps_owner_snapshots_and_rejects_partial_owner_data()
    {
        var entry = ServiceCatalogEntry.CreateExternal("tenant-a", "service-a", "服务", "https://example.test/app", [UiTerminal.Pc]);
        entry.SetOwnerSnapshot("org-a", "组织 A", "user-a", "负责人 A");

        Assert.Equal("org-a", entry.OwnerOrganizationNId);
        Assert.Equal("负责人 A", entry.TechnicalLeadDisplayNameSnapshot);
        Assert.Throws<ArgumentException>(() => entry.SetOwnerSnapshot("org-a", null, "user-a", "负责人 A"));
    }
}
