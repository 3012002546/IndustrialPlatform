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
    public async Task Navigation_contract_preserves_icon_key()
    {
        var service = new ResourceNavigationService(new ControlPlaneTestStore(), new TestPermissionRegistry());
        var node = await service.AddNodeAsync("tenant-a", new CreateNavigationNodeRequest { NodeNId = "group-a", Kind = "Group", Label = "Group", IconKey = "factory", VisibleTerminals = ["Pc"] }, CancellationToken.None);
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
        await theme.UpdateAsync("tenant-a", new ThemePolicyRequest { AllowedPalettes = ["IndustrialCyan"], AllowedModes = ["Light"], AllowedPcDensities = ["Compact"], DefaultPalette = "IndustrialCyan", DefaultMode = "Light", DefaultPcDensity = "Compact" }, CancellationToken.None);
        Assert.Equal("External", (await catalog.RuntimeAsync("tenant-a", CancellationToken.None)).Items.Single().Kind);
        Assert.Equal("IndustrialCyan", (await theme.GetAsync("tenant-a", CancellationToken.None)).DefaultPalette);
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
