using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Tests;
using IndustrialPlatform.SystemData.Application.Organizations;
using IndustrialPlatform.SystemData.Application.IdentityDirectory;
using IndustrialPlatform.SystemData.Contracts.ControlPlane;
using IndustrialPlatform.SystemData.Domain.ControlPlane;
using IndustrialPlatform.SystemData.Domain.Organizations;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class ControlPlaneServiceTests
{
    [Fact]
    public async Task Unpublished_navigation_and_unconfigured_theme_are_available_defaults_not_degraded()
    {
        var store = new ControlPlaneTestStore();
        var navigation = new ResourceNavigationService(store, new TestPermissionRegistry());
        var theme = new ThemePolicyControlService(store, new TestPermissionRegistry());

        var navigationRuntime = await navigation.RuntimeAsync("tenant-a", UiTerminal.Pc, CancellationToken.None);
        var themeRuntime = await theme.RuntimeAsync("tenant-a", CancellationToken.None);

        Assert.False(navigationRuntime.Configured);
        Assert.False(navigationRuntime.Degraded);
        Assert.Empty(navigationRuntime.Nodes);
        Assert.False(themeRuntime.Configured);
        Assert.False(themeRuntime.Degraded);
    }

    [Fact]
    public async Task Module_manifest_registers_full_permission_manifest_and_trusted_declarations_idempotently()
    {
        var store = new ControlPlaneTestStore();
        var service = new ResourceNavigationService(store, new TestPermissionRegistry());
        var request = new RegisterModuleManifestRequest
        {
            ModuleNId = "module-a",
            ManifestVersion = "1.0.0",
            Checksum = "checksum-a",
            Permissions = [new PermissionDeclarationRequest { PermissionNId = "module-a.view", Name = "View", ResourceType = "page", ParentPermissionNId = null }],
            Resources = [new RegisterUiResourceRequest { ResourceNId = "module-a.page", OwnerModuleNId = "module-a", ManifestVersion = "1.0.0", Type = "Page", Name = "Page", RouteName = "/module-a", RequiredPermissionNId = "module-a.view", SupportedTerminals = ["Pc"] }],
            Features = [new RegisterFeatureDefinitionRequest { FeatureNId = "module-a.feature", OwnerModuleNId = "module-a", Name = "Feature", Description = "Description", DefaultEnabled = true }],
        };

        var first = await service.RegisterManifestAsync("tenant-a", request, CancellationToken.None);
        var second = await service.RegisterManifestAsync("tenant-a", request, CancellationToken.None);

        Assert.Single(first);
        Assert.Single(first.Single().Permissions);
        Assert.Single(store.Snapshot.Resources);
        Assert.Single(store.Snapshot.Features);
        Assert.Single(second);
        await Assert.ThrowsAsync<ValidationException>(() => service.RegisterManifestAsync("tenant-a", request with { Checksum = "checksum-b" }, CancellationToken.None));
    }

    [Fact]
    public async Task Module_manifest_upgrade_rebinds_existing_resources_and_adds_new_declarations()
    {
        var store = new ControlPlaneTestStore();
        var service = new ResourceNavigationService(store, new TestPermissionRegistry());
        var first = new RegisterModuleManifestRequest
        {
            ModuleNId = "module-upgrade",
            ManifestVersion = "1",
            Checksum = "checksum-v1",
            Permissions = [new PermissionDeclarationRequest { PermissionNId = "module.view", Name = "View", ResourceType = "Page" }],
            Resources = [new RegisterUiResourceRequest
            {
                ResourceNId = "module.page",
                OwnerModuleNId = "module-upgrade",
                ManifestVersion = "1",
                Type = "Page",
                Name = "旧页面",
                RouteName = "module-page",
                RequiredPermissionNId = "module.view",
                SupportedTerminals = ["Pc"],
            }],
        };
        await service.RegisterManifestAsync("tenant-a", first, CancellationToken.None);

        var second = first with
        {
            ManifestVersion = "2",
            Checksum = "checksum-v2",
            Resources = [
                first.Resources!.Single(),
                new RegisterUiResourceRequest
                {
                    ResourceNId = "module-new-page",
                    OwnerModuleNId = "module-upgrade",
                    ManifestVersion = "2",
                    Type = "Page",
                    Name = "新页面",
                    RouteName = "module-new-page",
                    RequiredPermissionNId = "module.view",
                    SupportedTerminals = ["Pc"],
                },
            ],
        };

        await service.RegisterManifestAsync("tenant-a", second, CancellationToken.None);
        var resources = await service.ListResourcesAsync("tenant-a", CancellationToken.None);

        Assert.Equal(2, resources.Count);
        Assert.Equal("2", resources.Single(resource => resource.ResourceNId == "module.page").ManifestVersion);
        Assert.Equal("2", resources.Single(resource => resource.ResourceNId == "module-new-page").ManifestVersion);
        await service.RegisterManifestAsync("tenant-a", second, CancellationToken.None);
        Assert.Equal(2, (await service.ListResourcesAsync("tenant-a", CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Module_manifest_upgrade_rejects_omitting_a_resource_still_used_by_navigation()
    {
        var store = new ControlPlaneTestStore();
        var service = new ResourceNavigationService(store, new TestPermissionRegistry());
        var request = new RegisterModuleManifestRequest
        {
            ModuleNId = "module-used",
            ManifestVersion = "1",
            Checksum = "checksum-v1",
            Permissions = [new PermissionDeclarationRequest { PermissionNId = "module.view", Name = "View", ResourceType = "Page" }],
            Resources = [new RegisterUiResourceRequest
            {
                ResourceNId = "module.used-page",
                OwnerModuleNId = "module-used",
                ManifestVersion = "1",
                Type = "Page",
                Name = "页面",
                RouteName = "module-used-page",
                RequiredPermissionNId = "module.view",
                SupportedTerminals = ["Pc"],
            }],
        };
        await service.RegisterManifestAsync("tenant-a", request, CancellationToken.None);
        await service.AddNodeAsync("tenant-a", new CreateNavigationNodeRequest
        {
            NodeNId = "module.used-node",
            Kind = "Link",
            Label = "页面",
            ResourceNId = "module.used-page",
            VisibleTerminals = ["Pc"],
            ExpectedDraftRevision = 1,
        }, CancellationToken.None);

        var upgrade = request with { ManifestVersion = "2", Checksum = "checksum-v2", Resources = [] };

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.RegisterManifestAsync("tenant-a", upgrade, CancellationToken.None));

        Assert.Contains("used", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Module_manifest_rejects_cross_module_resource_collisions()
    {
        var service = new ResourceNavigationService(new ControlPlaneTestStore(), new TestPermissionRegistry());
        await service.RegisterManifestAsync("tenant-a", new RegisterModuleManifestRequest
        {
            ModuleNId = "module-owner",
            ManifestVersion = "1",
            Checksum = "checksum-owner",
            PermissionNIds = ["owner.view"],
            Resources = [new RegisterUiResourceRequest
            {
                ResourceNId = "shared.resource",
                OwnerModuleNId = "module-owner",
                ManifestVersion = "1",
                Type = "Page",
                Name = "Owner",
                RouteName = "owner",
                RequiredPermissionNId = "owner.view",
                SupportedTerminals = ["Pc"],
            }],
        }, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => service.RegisterManifestAsync("tenant-a", new RegisterModuleManifestRequest
        {
            ModuleNId = "module-collision",
            ManifestVersion = "1",
            Checksum = "checksum-collision",
            PermissionNIds = ["collision.view"],
            Resources = [new RegisterUiResourceRequest
            {
                ResourceNId = "shared.resource",
                OwnerModuleNId = "module-collision",
                ManifestVersion = "1",
                Type = "Page",
                Name = "Collision",
                RouteName = "collision",
                RequiredPermissionNId = "collision.view",
                SupportedTerminals = ["Pc"],
            }],
        }, CancellationToken.None));

        Assert.Contains("collision", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Module_manifest_upgrade_rejects_permission_identity_changes()
    {
        var service = new ResourceNavigationService(new ControlPlaneTestStore(), new TestPermissionRegistry());
        var first = new RegisterModuleManifestRequest
        {
            ModuleNId = "module-permission-change",
            ManifestVersion = "1",
            Checksum = "checksum-permission-v1",
            Permissions = [new PermissionDeclarationRequest { PermissionNId = "module.view", Name = "View", ResourceType = "Page" }],
        };
        await service.RegisterManifestAsync("tenant-a", first, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => service.RegisterManifestAsync("tenant-a", first with
        {
            ManifestVersion = "2",
            Checksum = "checksum-permission-v2",
            Permissions = [new PermissionDeclarationRequest { PermissionNId = "module.view", Name = "Changed", ResourceType = "Page" }],
        }, CancellationToken.None));

        Assert.Contains("identity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Navigation_contract_preserves_icon_key()
    {
        var service = new ResourceNavigationService(new ControlPlaneTestStore(), new TestPermissionRegistry());
        var node = await service.AddNodeAsync("tenant-a", new CreateNavigationNodeRequest { NodeNId = "group-a", Kind = "Group", Label = "Group", IconKey = "factory", VisibleTerminals = ["Pc"], ExpectedDraftRevision = 0 }, CancellationToken.None);
        Assert.Equal("factory", node.IconKey);
    }

    [Fact]
    public async Task Emergency_disabled_feature_wins_over_tenant_enabled_override()
    {
        var store = new ControlPlaneTestStore();
        store.SeedFeature(FeatureDefinition.Create("tenant-a", "feature-a", "module-a", "功能", false));
        var service = new FeatureControlService(store, new TestPermissionRegistry(), Options.Create(new ControlPlaneOptions { EmergencyDisabledFeatureNIds = ["feature-a"] }));
        await service.SetOverrideAsync("tenant-a", "feature-a", new SetFeatureOverrideRequest { Mode = "Enabled" }, CancellationToken.None);
        Assert.False((await service.RuntimeAsync("tenant-a", CancellationToken.None)).Items.Single().Enabled);
    }

    [Fact]
    public async Task External_catalog_and_theme_policy_return_safe_runtime_contracts()
    {
        var store = new ControlPlaneTestStore();
        var catalog = new ServiceCatalogControlService(store, new TestPermissionRegistry(), new FakeAdministrativeOrganizationStore(), new FakeIdentityUserDirectory());
        var theme = new ThemePolicyControlService(store, new TestPermissionRegistry());
        await catalog.CreateExternalAsync("tenant-a", new CreateExternalServiceCatalogRequest { ServiceNId = "external-a", Name = "外部", EntryPoint = "https://example.test/app", SupportedTerminals = ["Pc"] }, CancellationToken.None);
        await theme.UpdateAsync("tenant-a", new ThemePolicyRequest { AllowedPalettes = ["IndustrialCyan"], AllowedModes = ["Light"], AllowedPcDensities = ["Compact"], DefaultPalette = "IndustrialCyan", DefaultMode = "Light", DefaultPcDensity = "Compact", ExpectedPolicyRevision = 0 }, CancellationToken.None);
        Assert.Equal("External", (await catalog.RuntimeAsync("tenant-a", CancellationToken.None)).Items.Single().Kind);
        Assert.Equal("industrial-cyan", (await theme.GetAsync("tenant-a", CancellationToken.None)).DefaultPalette);
    }

    [Fact]
    public async Task Service_catalog_resolves_owner_ids_and_truncates_server_snapshots()
    {
        var store = new ControlPlaneTestStore();
        var organizations = new FakeAdministrativeOrganizationStore();
        await organizations.AddAsync(AdministrativeOrganization.CreateRootCompany("tenant-a", "org-a", "组织 A", 0), CancellationToken.None);
        var identity = new FakeIdentityUserDirectory().WithEntry("user-a", string.Concat(Enumerable.Repeat("负责人", 140)));
        var service = new ServiceCatalogControlService(store, new TestPermissionRegistry(), organizations, identity);

        var result = await service.CreateExternalAsync("tenant-a", new CreateExternalServiceCatalogRequest
        {
            ServiceNId = "external-a",
            Name = "外部",
            EntryPoint = "https://example.test/app",
            SupportedTerminals = ["Pc"],
            OwnerOrganizationNId = "org-a",
            TechnicalLeadUserNId = "user-a",
        }, CancellationToken.None);

        Assert.Equal("组织 A", result.OwnerOrganizationNameSnapshot);
        Assert.Equal(256, result.TechnicalLeadDisplayNameSnapshot!.Length);
    }

    [Theory]
    [InlineData("unknown-org", "user-a")]
    [InlineData("inactive-org", "user-a")]
    [InlineData("org-a", "unknown-user")]
    public async Task Service_catalog_rejects_unverified_owner_references(string organizationNId, string userNId)
    {
        var store = new ControlPlaneTestStore();
        var organizations = new FakeAdministrativeOrganizationStore();
        var organization = AdministrativeOrganization.CreateRootCompany("tenant-a", "org-a", "组织 A", 0);
        await organizations.AddAsync(organization, CancellationToken.None);
        if (organizationNId == "inactive-org")
        {
            var expectedVersion = organization.OptimisticVersion;
            var expectedConcurrency = organization.ConcurrencyVersion;
            organization.Deactivate(0, 0, 0);
            await organizations.UpdateAsync(organization, expectedVersion, expectedConcurrency, CancellationToken.None);
            organizationNId = "org-a";
        }

        var identity = new FakeIdentityUserDirectory().WithEntry("user-a", "负责人 A");
        var service = new ServiceCatalogControlService(store, new TestPermissionRegistry(), organizations, identity);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateExternalAsync("tenant-a", new CreateExternalServiceCatalogRequest
        {
            ServiceNId = Guid.NewGuid().ToString("N"),
            Name = "外部",
            EntryPoint = "https://example.test/app",
            SupportedTerminals = ["Pc"],
            OwnerOrganizationNId = organizationNId,
            TechnicalLeadUserNId = userNId,
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Service_catalog_fails_closed_when_identity_is_unavailable()
    {
        var organizations = new FakeAdministrativeOrganizationStore();
        await organizations.AddAsync(AdministrativeOrganization.CreateRootCompany("tenant-a", "org-a", "组织 A", 0), CancellationToken.None);
        var service = new ServiceCatalogControlService(new ControlPlaneTestStore(), new TestPermissionRegistry(), organizations, new FakeIdentityUserDirectory().Unavailable());

        await Assert.ThrowsAsync<IdentityDirectoryUnavailableException>(() => service.CreateExternalAsync("tenant-a", new CreateExternalServiceCatalogRequest
        {
            ServiceNId = "external-a",
            Name = "外部",
            EntryPoint = "https://example.test/app",
            SupportedTerminals = ["Pc"],
            OwnerOrganizationNId = "org-a",
            TechnicalLeadUserNId = "user-a",
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Platform_catalog_allows_safe_fields_but_keeps_platform_endpoint_metadata_protected()
    {
        var store = new ControlPlaneTestStore();
        var organizations = new FakeAdministrativeOrganizationStore();
        await organizations.AddAsync(AdministrativeOrganization.CreateRootCompany("tenant-a", "org-a", "组织 A", 0), CancellationToken.None);
        var identity = new FakeIdentityUserDirectory().WithEntry("user-a", "负责人 A");
        var service = new ServiceCatalogControlService(store, new TestPermissionRegistry(), organizations, identity);
        var platform = ServiceCatalogEntry.CreatePlatform("tenant-a", "platform-a", "旧名称", "/systemdata", "/health", [UiTerminal.Pc]);
        store.SeedCatalog(platform);

        var result = await service.UpdateAsync("tenant-a", "platform-a", new UpdateServiceCatalogRequest
        {
            Name = "新名称",
            Description = "新描述",
            OwnerOrganizationNId = "org-a",
            TechnicalLeadUserNId = "user-a",
            Status = "Inactive",
        }, CancellationToken.None);

        Assert.Equal("新名称", result.Name);
        Assert.Equal("新描述", result.Description);
        Assert.Equal("Inactive", result.Status);
        Assert.Equal("/systemdata", result.EntryPoint);
        Assert.Equal("/health", result.HealthPath);
    }

    [Fact]
    public async Task External_catalog_entrypoint_update_revalidates_https_ssrf_rules()
    {
        var store = new ControlPlaneTestStore();
        var service = new ServiceCatalogControlService(store, new TestPermissionRegistry(), new FakeAdministrativeOrganizationStore(), new FakeIdentityUserDirectory());
        await service.CreateExternalAsync("tenant-a", new CreateExternalServiceCatalogRequest { ServiceNId = "external-a", Name = "外部", EntryPoint = "https://example.test/app", SupportedTerminals = ["Pc"] }, CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync("tenant-a", "external-a", new UpdateServiceCatalogRequest { EntryPoint = "http://127.0.0.1/admin" }, CancellationToken.None));
    }

    [Fact]
    public async Task Retired_feature_cannot_be_reenabled()
    {
        var store = new ControlPlaneTestStore();
        var feature = FeatureDefinition.Create("tenant-a", "feature-a", "module-a", "功能", true);
        feature.Retire();
        store.SeedFeature(feature);
        var service = new FeatureControlService(store, new TestPermissionRegistry());
        await service.SetOverrideAsync("tenant-a", "feature-a", new SetFeatureOverrideRequest { Mode = "Enabled" }, CancellationToken.None);
        Assert.False((await service.RuntimeAsync("tenant-a", CancellationToken.None)).Items.Single().Enabled);
    }
}
