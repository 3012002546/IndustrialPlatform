using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Contracts.ControlPlane;
using IndustrialPlatform.SystemData.Domain.ControlPlane;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;
using IndustrialPlatform.SystemData.Infrastructure.Reliability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

public sealed class SystemDataBaselineSeedRunnerTests
{
    private const string Tenant = "tenant-baseline";

    static SystemDataBaselineSeedRunnerTests() => Batteries_V2.Init();

    [Fact]
    public async Task Apply_upgrades_an_existing_manifest_seed_from_v2_to_current_version()
    {
        using var harness = new BaselineHarness();
        harness.Store.SeedLegacyCurrentManifest();

        var initial = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        await harness.Initializer.ApplyAsync(
            harness.Context,
            await harness.Initializer.PlanAsync(harness.Context, initial, CancellationToken.None),
            CancellationToken.None);

        Assert.True(await harness.Store.SeedAppliedAsync(
            Tenant,
            SystemDataBaselineSeedRunner.CurrentManifestSeedKey,
            SystemDataBaselineSeedRunner.CurrentManifestVersion,
            SystemDataBaselineSeedRunner.CurrentManifestChecksum,
            CancellationToken.None));
        Assert.All(harness.Store.Snapshot.Resources.Where(resource => resource.OwnerModuleNId == "systemdata"), resource =>
            Assert.Equal(SystemDataBaselineSeedRunner.CurrentManifestVersion, resource.ManifestVersion));
    }

    [Fact]
    public async Task Apply_upgrades_a_legacy_navigation_resource_and_remains_idempotent()
    {
        using var harness = new BaselineHarness(includeLegacySeed: true);

        var initial = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        var plan = await harness.Initializer.PlanAsync(harness.Context, initial, CancellationToken.None);

        await harness.Initializer.ApplyAsync(harness.Context, plan, CancellationToken.None);

        var ready = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        Assert.True(ready.Ready);
        Assert.Contains(harness.Store.Snapshot.Resources, resource =>
            resource.NId == "systemdata.navigation"
            && resource.ManifestVersion == SystemDataBaselineSeedRunner.CurrentManifestVersion
            && resource.RouteName == "/systemdata/navigation"
            && resource.RequiredPermissionNId == "systemdata.navigation.view");
        Assert.Contains(harness.Store.Snapshot.Resources, resource =>
            resource.NId == "systemdata.navigation.pc-home"
            && resource.RouteName == "pc-home"
            && resource.RequiredPermissionNId == "platform.home.view");
        Assert.Contains(harness.Store.Snapshot.Resources, resource => resource.NId == "tenant.navigation.custom");
        Assert.Equal("基础配置", harness.Store.Snapshot.Resources.Single(resource => resource.NId == "referencedata.navigation.parameters").Name);
        Assert.Equal("元数据定义", harness.Store.Snapshot.Resources.Single(resource => resource.NId == "referencedata.navigation.metadata").Name);
        Assert.Contains("SDM-013", harness.Store.AppliedSeedKeys);
        Assert.Contains("SDM-017", harness.Store.AppliedSeedKeys);
        Assert.Contains(SystemDataBaselineSeedRunner.ReferenceDataManifestSeedKey, harness.Store.AppliedSeedKeys);
        var referenceDataManifest = harness.Store.Snapshot.Manifests.Single(manifest => manifest.ModuleNId == "referencedata");
        Assert.Equal(38, referenceDataManifest.PermissionNIds.Count);
        Assert.Equal(7, referenceDataManifest.ResourceDeclarations.Count);
        Assert.Equal(7, harness.Store.Snapshot.Resources.Count(resource => resource.OwnerModuleNId == "referencedata"));
        Assert.True(SystemDataBaselineSeedRunner.IsReferenceDataBootstrapReady(harness.Store.Snapshot));

        var revision = harness.Store.Snapshot.Revision;
        var commitCount = harness.Store.CommitCount;
        await harness.Initializer.ApplyAsync(harness.Context, plan, CancellationToken.None);

        var second = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        Assert.True(second.Ready);
        Assert.Equal(revision, harness.Store.Snapshot.Revision);
        Assert.Equal(commitCount, harness.Store.CommitCount);
    }

    [Fact]
    public async Task Apply_adds_collaboration_chat_after_terminal_preview_and_preserves_custom_navigation()
    {
        using var harness = new BaselineHarness();
        var initial = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        await harness.Initializer.ApplyAsync(
            harness.Context,
            await harness.Initializer.PlanAsync(harness.Context, initial, CancellationToken.None),
            CancellationToken.None);

        var collaborationManifest = harness.Store.Snapshot.Manifests.Single(manifest => manifest.ModuleNId == "collaboration");
        Assert.Equal(SystemDataBaselineSeedRunner.CollaborationManifestVersion, collaborationManifest.ManifestVersion);
        Assert.Equal(25, collaborationManifest.PermissionNIds.Count);
        Assert.Equal(7, collaborationManifest.ResourceDeclarations.Count);
        Assert.Equal(7, harness.Store.Snapshot.Resources.Count(resource => resource.OwnerModuleNId == "collaboration"));
        Assert.Contains(SystemDataBaselineSeedRunner.CollaborationManifestSeedKey, harness.Store.AppliedSeedKeys);
        Assert.Contains(SystemDataBaselineSeedRunner.CollaborationNavigationSeedKey, harness.Store.AppliedSeedKeys);

        var customGroup = NavigationNode.CreateGroup(Tenant, "tenant.custom.group", "租户自定义", null, "PLATFORM_NAVIGATION");
        customGroup.SetDisplayOrder(99);
        var home = NavigationNode.CreateLink(
            Tenant,
            "navigation.link.pc-home",
            "首页",
            "navigation.group.workspace",
            "PLATFORM_NAVIGATION",
            "systemdata.navigation.pc-home",
            null,
            [UiTerminal.Pc]);
        home.SetDisplayOrder(0);
        var terminalPreview = NavigationNode.CreateLink(
            Tenant,
            "navigation.link.terminal-preview",
            "终端预览",
            "navigation.group.workspace",
            "PLATFORM_NAVIGATION",
            "systemdata.navigation.terminal-preview",
            null,
            [UiTerminal.Pc]);
        terminalPreview.SetDisplayOrder(1);
        var customLink = NavigationNode.CreateLink(
            Tenant,
            "tenant.custom.link",
            "租户入口",
            customGroup.NId,
            "PLATFORM_NAVIGATION",
            "systemdata.navigation.pc-home",
            null,
            [UiTerminal.Pc]);
        var navigationService = new ResourceNavigationService(harness.Store, new VerifiedPermissionRegistry());
        harness.Store.ReplaceSnapshot(harness.Store.Snapshot with
        {
            DraftNodes = harness.Store.Snapshot.DraftNodes
                .Append(home)
                .Append(terminalPreview)
                .Append(customGroup)
                .Append(customLink)
                .ToArray(),
        });
        await navigationService.PublishAsync(Tenant, "acceptance", harness.Store.Snapshot.Revision, CancellationToken.None);

        // Simulate the already-published pre-PF05 tenant: remove only the
        // Collaboration-owned declaration and nodes, leaving the custom menu,
        // workspace and published revision history intact.
        var legacy = harness.Store.Snapshot;
        var collaborationNodeIds = SystemDataBaselineSeedRunner.RequiredCollaborationNavigationFacts
            .Select(item => item.NId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        harness.Store.ReplaceSnapshot(legacy with
        {
            Manifests = legacy.Manifests.Where(item => !item.ModuleNId.Equals("collaboration", StringComparison.OrdinalIgnoreCase)).ToArray(),
            PermissionReceipts = legacy.PermissionReceipts.Where(item => !item.ModuleNId.Equals("collaboration", StringComparison.OrdinalIgnoreCase)).ToArray(),
            Resources = legacy.Resources.Where(item => !item.OwnerModuleNId.Equals("collaboration", StringComparison.OrdinalIgnoreCase)).ToArray(),
            DraftNodes = legacy.DraftNodes.Where(item => !collaborationNodeIds.Contains(item.NId)).ToArray(),
            Snapshots = legacy.Snapshots
                .Select(snapshot =>
                {
                    var nodes = snapshot.Nodes.Where(item => !collaborationNodeIds.Contains(item.NodeNId)).ToArray();
                    return snapshot with { Nodes = nodes, Checksum = NavigationSnapshotChecksum.Compute(nodes) };
                })
                .ToArray(),
        });
        harness.Store.RemoveSeed(SystemDataBaselineSeedRunner.CollaborationManifestSeedKey);
        harness.Store.RemoveSeed(SystemDataBaselineSeedRunner.CollaborationNavigationSeedKey);

        var inspection = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        Assert.False(inspection.BootstrapReady);
        await harness.Initializer.ApplyAsync(
            harness.Context,
            await harness.Initializer.PlanAsync(harness.Context, inspection, CancellationToken.None),
            CancellationToken.None);

        var runtime = await navigationService.RuntimeAsync(Tenant, UiTerminal.Pc, CancellationToken.None);
        var workspace = runtime.Nodes.Single(node => node.NodeNId == "navigation.group.workspace");
        Assert.Equal(["navigation.link.pc-home", "navigation.link.terminal-preview", "collaboration.nav.pc-chat"],
            workspace.Children.Select(node => node.NodeNId).ToArray());
        var collaboration = runtime.Nodes.Single(node => node.NodeNId == "navigation.group.collaboration");
        Assert.Equal(
            ["collaboration.nav.pc-compliance-search", "collaboration.nav.pc-legal-holds", "collaboration.nav.pc-exports", "collaboration.nav.pc-retention"],
            collaboration.Children.Select(node => node.NodeNId).ToArray());
        Assert.Contains(runtime.Nodes, node => node.NodeNId == customGroup.NId);
        Assert.True(SystemDataBaselineSeedRunner.IsCollaborationBootstrapReady(harness.Store.Snapshot));

        var revision = harness.Store.Snapshot.Revision;
        var commitCount = harness.Store.CommitCount;
        await harness.Initializer.ApplyAsync(
            harness.Context,
            await harness.Initializer.PlanAsync(harness.Context, await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None), CancellationToken.None),
            CancellationToken.None);
        Assert.Equal(revision, harness.Store.Snapshot.Revision);
        Assert.Equal(commitCount, harness.Store.CommitCount);
    }

    [Fact]
    public async Task Apply_preserves_a_customized_reference_data_resource_title()
    {
        using var harness = new BaselineHarness(includeLegacySeed: true, legacyParameterName: "租户参数入口");

        var initial = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        await harness.Initializer.ApplyAsync(
            harness.Context,
            await harness.Initializer.PlanAsync(harness.Context, initial, CancellationToken.None),
            CancellationToken.None);

        Assert.Equal("租户参数入口", harness.Store.Snapshot.Resources.Single(resource => resource.NId == "referencedata.navigation.parameters").Name);
    }

    [Fact]
    public async Task Reference_data_defaults_preview_import_publish_and_remain_additive_to_custom_draft_nodes()
    {
        using var harness = new BaselineHarness();
        var initial = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        await harness.Initializer.ApplyAsync(
            harness.Context,
            await harness.Initializer.PlanAsync(harness.Context, initial, CancellationToken.None),
            CancellationToken.None);

        var customGroup = NavigationNode.CreateGroup(Tenant, "tenant.custom.group", "租户自定义", null, "PLATFORM_NAVIGATION");
        customGroup.SetDisplayOrder(99);
        var customLink = NavigationNode.CreateLink(
            Tenant,
            "tenant.custom.link",
            "租户入口",
            customGroup.NId,
            "PLATFORM_NAVIGATION",
            "systemdata.navigation.pc-home",
            null,
            [UiTerminal.Pc]);
        harness.Store.ReplaceSnapshot(harness.Store.Snapshot with { DraftNodes = [customGroup, customLink] });
        var service = new ResourceNavigationService(harness.Store, new VerifiedPermissionRegistry());

        var preview = await service.PreviewDefaultImportAsync(Tenant, CancellationToken.None);
        Assert.Equal("基础配置", preview.Items.Single(item => item.NodeNId == "navigation.group.reference-data").Label);
        Assert.Equal(8, preview.Items.Count(item => item.NodeNId == "navigation.group.reference-data" || item.NodeNId.StartsWith("navigation.link.reference-data-", StringComparison.Ordinal)));
        Assert.All(preview.Items.Where(item => item.NodeNId.StartsWith("navigation.link.reference-data-", StringComparison.Ordinal)), item => Assert.Equal("Add", item.Action));

        var imported = await service.ImportDefaultsAsync(Tenant, new ImportNavigationDefaultsRequest { ExpectedDraftRevision = preview.DraftRevision }, CancellationToken.None);
        Assert.All(imported.Items.Where(item => item.NodeNId == "navigation.group.reference-data" || item.NodeNId.StartsWith("navigation.link.reference-data-", StringComparison.Ordinal)), item => Assert.Equal("Added", item.Action));
        Assert.Contains(harness.Store.Snapshot.DraftNodes, node => node.NId == customGroup.NId && node.Label == customGroup.Label);

        var repeated = await service.PreviewDefaultImportAsync(Tenant, CancellationToken.None);
        Assert.All(repeated.Items.Where(item => item.NodeNId == "navigation.group.reference-data" || item.NodeNId.StartsWith("navigation.link.reference-data-", StringComparison.Ordinal)), item => Assert.Equal("Skipped", item.Action));

        var validation = await service.ValidateAsync(Tenant, CancellationToken.None);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(error => $"{error.Code}:{error.NodeNId}")));
        var publishedRevision = await service.PublishAsync(Tenant, "acceptance", repeated.DraftRevision, CancellationToken.None);
        var runtime = await service.RuntimeAsync(Tenant, UiTerminal.Pc, CancellationToken.None);

        Assert.Equal(publishedRevision, runtime.Revision);
        var referenceDataGroup = runtime.Nodes.Single(node => node.NodeNId == "navigation.group.reference-data");
        Assert.Equal(7, referenceDataGroup.Children.Count);
        Assert.Contains(runtime.Nodes, node => node.NodeNId == customGroup.NId);
    }

    [Fact]
    public async Task Inspect_and_apply_keep_a_valid_customized_theme_ready_and_unchanged()
    {
        using var harness = new BaselineHarness();
        var initial = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        var plan = await harness.Initializer.PlanAsync(harness.Context, initial, CancellationToken.None);
        await harness.Initializer.ApplyAsync(harness.Context, plan, CancellationToken.None);

        var customized = ThemePolicy.Create(
            Tenant,
            [ThemePalette.IndustrialCyan],
            [ThemeMode.Dark],
            [PcDensity.Compact],
            ThemePalette.IndustrialCyan,
            ThemeMode.Dark,
            PcDensity.Compact);
        harness.Store.ReplaceSnapshot(harness.Store.Snapshot with { Theme = customized });

        var customizedInspection = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        Assert.True(customizedInspection.Ready);
        Assert.True(customizedInspection.RequiredSeedReady);
        Assert.True(customizedInspection.BootstrapReady);

        var customizedPlan = await harness.Initializer.PlanAsync(harness.Context, customizedInspection, CancellationToken.None);
        await harness.Initializer.ApplyAsync(harness.Context, customizedPlan, CancellationToken.None);

        var afterApply = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        Assert.True(afterApply.Ready);
        Assert.Same(customized, harness.Store.Snapshot.Theme);
    }

    [Fact]
    public async Task Inspect_is_not_ready_when_a_required_builtin_resource_has_a_stale_manifest_version()
    {
        using var harness = new BaselineHarness();
        var initial = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        await harness.Initializer.ApplyAsync(
            harness.Context,
            await harness.Initializer.PlanAsync(harness.Context, initial, CancellationToken.None),
            CancellationToken.None);

        var stale = harness.Store.Snapshot.Resources
            .Single(resource => resource.NId == "systemdata.navigation.pc-home");
        var replacement = UiResource.Create(
            stale.TenantNId,
            stale.NId,
            stale.OwnerModuleNId,
            "1",
            stale.Type,
            stale.Name,
            stale.RouteName,
            stale.RequiredPermissionNId,
            stale.SupportedTerminals);
        harness.Store.ReplaceSnapshot(harness.Store.Snapshot with
        {
            Resources = harness.Store.Snapshot.Resources
                .Select(resource => resource.NId == stale.NId ? replacement : resource)
                .ToArray(),
        });

        var inspection = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);

        Assert.False(inspection.Ready);
        Assert.False(inspection.BootstrapReady);
    }

    [Fact]
    public async Task Real_sql_store_persists_resource_rebind_and_publish_after_a_legacy_upgrade()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-systemdata-real-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = CreateDbContext(dbPath))
            {
                var migrations = new SchemaMigrationRunner(db, SystemDataSchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance);
                await migrations.ApplyPendingAsync();
                var store = new SqlControlPlaneStore(db);
                var legacyChecksum = Checksum(SystemDataBaselineSeedRunner.CurrentManifestSeedKey);
                var manifest = new ModuleManifestState(Tenant, "systemdata", "2", legacyChecksum, SystemDataBaselineSeedRunner.RequiredPermissionNIds, "2", legacyChecksum, DateTimeOffset.UtcNow)
                {
                    PermissionDeclarationItems = SystemDataBaselineSeedRunner.RequiredPermissionNIds
                        .Select(permission => new PermissionManifestEntry(permission, permission, "permission", null))
                        .ToArray(),
                };
                var legacyResource = UiResource.Create(Tenant, "systemdata.navigation", "systemdata", "1", UiResourceType.Page, "旧导航入口", "/systemdata/navigation", "systemdata.navigation.view", [UiTerminal.Pc]);
                var customResource = UiResource.Create(Tenant, "tenant.navigation.custom", "tenant-custom", "1", UiResourceType.Page, "租户自定义入口", "tenant-custom", "tenant.custom.view", [UiTerminal.Pc]);
                var versionTwoResources = SystemDataBaselineSeedRunner.RequiredResourceFacts
                    .Select(resource => UiResource.Create(
                        Tenant,
                        resource.NId,
                        "systemdata",
                        "2",
                        UiResourceType.Page,
                        resource.Name,
                        resource.RouteName,
                        resource.PermissionNId,
                        [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile]));
                var node = NavigationNode.CreateLink(Tenant, "legacy.navigation.node", "旧导航", null, "PLATFORM_NAVIGATION", "systemdata.navigation", null, [UiTerminal.Pc]);
                await store.CommitAsync(
                    new ControlPlaneSnapshot(Tenant, 0, [manifest], versionTwoResources.Append(legacyResource).Append(customResource).ToArray(), [node], [], null, null, [], [], [], null, [new PermissionReceipt("systemdata", "2", legacyChecksum, true)]),
                    0,
                    new ControlPlaneCommit([], [],
                    [
                        new SeedLedgerEntry(Tenant, SystemDataBaselineSeedRunner.CurrentManifestSeedKey, "2", legacyChecksum),
                        new SeedLedgerEntry(Tenant, SystemDataBaselineSeedRunner.ResourceConsistencySeedKey, "1", Checksum(SystemDataBaselineSeedRunner.ResourceConsistencySeedKey)),
                    ]),
                    CancellationToken.None);
            }

            using (var db = CreateDbContext(dbPath))
            using (var seeder = new SystemDataBaselineSeedRunner(new ConfigurationBuilder().Build(), new SqlControlPlaneStore(db), NullLogger<SystemDataBaselineSeedRunner>.Instance, new VerifiedPermissionRegistry()))
            {
                var store = new SqlControlPlaneStore(db);
                var initializer = new SystemDataServiceInitializer(
                    new SchemaMigrationRunner(db, SystemDataSchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance),
                    db,
                    store,
                    seeder);
                var context = CreateContext("operation-real-sql");
                var before = await initializer.InspectAsync(context, CancellationToken.None);
                await initializer.ApplyAsync(context, await initializer.PlanAsync(context, before, CancellationToken.None), CancellationToken.None);
                var after = await initializer.InspectAsync(context, CancellationToken.None);
                Assert.True(after.Ready);
            }

            using (var db = CreateDbContext(dbPath))
            {
                var store = new SqlControlPlaneStore(db);
                var state = await store.LoadAsync(Tenant, CancellationToken.None);
                Assert.Equal(SystemDataBaselineSeedRunner.CurrentManifestVersion, state.Resources.Single(resource => resource.NId == "systemdata.navigation").ManifestVersion);
                Assert.Equal("1", state.Resources.Single(resource => resource.NId == "tenant.navigation.custom").ManifestVersion);
                Assert.Contains(await db.SqlSugar.Queryable<SystemDataSeedLedgerTable>().Where(row => row.TenantNId == Tenant).Select(row => row.SeedKey).ToListAsync(), key => key == SystemDataBaselineSeedRunner.ResourceConsistencySeedKey);

                var service = new ResourceNavigationService(store, new VerifiedPermissionRegistry());
                var validation = await service.ValidateAsync(Tenant, CancellationToken.None);
                Assert.True(validation.IsValid);
                var publishedRevision = await service.PublishAsync(Tenant, "acceptance", state.Revision, CancellationToken.None);
                Assert.Equal(state.Revision + 1, publishedRevision);
            }

            using (var db = CreateDbContext(dbPath))
            {
                var state = await new SqlControlPlaneStore(db).LoadAsync(Tenant, CancellationToken.None);
                Assert.Equal(state.ActiveSnapshotRevision, state.Snapshots.Single().Revision);
                Assert.Equal(SystemDataBaselineSeedRunner.CurrentManifestVersion, state.Resources.Single(resource => resource.NId == "systemdata.navigation").ManifestVersion);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
            catch (IOException)
            {
                // SQLite connection pooling may briefly retain the file handle.
            }
        }
    }

    [Fact]
    public async Task Real_sql_store_upgrades_applied_chat_only_seeds_without_rewriting_history()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-collaboration-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            const string oldVersion = "1.0.0";
            var seedKeys = new[] { SystemDataBaselineSeedRunner.CollaborationManifestSeedKey, SystemDataBaselineSeedRunner.CollaborationNavigationSeedKey };
            long publishedRevision;
            string? publishedChecksum;
            using (var db = CreateDbContext(dbPath))
            {
                await new SchemaMigrationRunner(db, SystemDataSchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance).ApplyPendingAsync();
                var store = new SqlControlPlaneStore(db);
                var resources = SystemDataBaselineSeedRunner.RequiredCollaborationResourceFacts
                    .Where(item => item.NId.EndsWith("-chat", StringComparison.Ordinal))
                    .Select(item => UiResource.Create(Tenant, item.NId, "collaboration", oldVersion, UiResourceType.Page, item.Name, item.RouteName, item.PermissionNId, [item.Terminal]))
                    .ToArray();
                Assert.Equal(3, resources.Length);
                var manifest = new ModuleManifestState(Tenant, "collaboration", oldVersion, Checksum(seedKeys[0]),
                    SystemDataBaselineSeedRunner.RequiredCollaborationPermissionNIds, oldVersion, Checksum(seedKeys[0]), DateTimeOffset.UtcNow)
                {
                    ResourceDeclarations = resources.Select(item => new ModuleResourceDeclaration(item.NId, oldVersion, nameof(UiResourceType.Page), item.Name, item.RouteName, item.RequiredPermissionNId, item.SupportedTerminals)).ToArray(),
                };
                var nodes = SystemDataBaselineSeedRunner.RequiredCollaborationNavigationFacts
                    .Where(item => item.NId.EndsWith("-chat", StringComparison.Ordinal))
                    .Select(item =>
                    {
                        var node = NavigationNode.CreateLink(Tenant, item.NId, "聊天", item.ParentNId, "PLATFORM_NAVIGATION", item.ResourceNId, null, [item.Terminal]);
                        node.SetDisplayOrder(item.DisplayOrder);
                        return node;
                    })
                    .Append(NavigationNode.CreateGroup(Tenant, "navigation.group.workspace", "工作台", null, "PLATFORM_NAVIGATION"))
                    .Append(NavigationNode.CreateGroup(Tenant, "tenant.custom.group", "租户自定义", null, "PLATFORM_NAVIGATION"))
                    .Append(NavigationNode.CreateLink(Tenant, "tenant.custom.chat", "租户聊天入口", "tenant.custom.group", "PLATFORM_NAVIGATION", "collaboration.page.pc-chat", null, [UiTerminal.Pc]))
                    .ToArray();
                await store.CommitAsync(ControlPlaneSnapshot.Empty(Tenant) with
                {
                    Manifests = [manifest], Resources = resources, DraftNodes = nodes,
                    PermissionReceipts = [new PermissionReceipt("collaboration", oldVersion, Checksum(seedKeys[0]), true)],
                }, 0, new ControlPlaneCommit([], [], seedKeys.Select(key => new SeedLedgerEntry(Tenant, key, oldVersion, Checksum(key))).ToArray()), CancellationToken.None);
                var navigation = new ResourceNavigationService(store, new VerifiedPermissionRegistry());
                var legacy = await store.LoadAsync(Tenant, CancellationToken.None);
                publishedRevision = await navigation.PublishAsync(Tenant, "legacy", legacy.Revision, CancellationToken.None);
                publishedChecksum = (await store.LoadAsync(Tenant, CancellationToken.None)).Snapshots.Single().Checksum;
            }

            // Reopen the persisted legacy database, with both old seed ledger rows intact.
            using (var db = CreateDbContext(dbPath))
            {
                var store = new SqlControlPlaneStore(db);
                using var seeder = new SystemDataBaselineSeedRunner(new ConfigurationBuilder().Build(), store, NullLogger<SystemDataBaselineSeedRunner>.Instance, new VerifiedPermissionRegistry());
                var initializer = new SystemDataServiceInitializer(new SchemaMigrationRunner(db, SystemDataSchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance), db, store, seeder);
                var context = CreateContext("collaboration-legacy-upgrade");
                var before = await initializer.InspectAsync(context, CancellationToken.None);
                Assert.False(before.Ready);
                var after = await initializer.ApplyAsync(context, await initializer.PlanAsync(context, before, CancellationToken.None), CancellationToken.None);
                Assert.True(after.Ready, after.Reason);

                var state = await store.LoadAsync(Tenant, CancellationToken.None);
                Assert.Equal(publishedChecksum, state.Snapshots.Single(item => item.Revision == publishedRevision).Checksum);
                Assert.Contains(state.DraftNodes, node => node.NId == "tenant.custom.group" && node.Label == "租户自定义");
                var active = state.Snapshots.Single(item => item.Revision == state.ActiveSnapshotRevision);
                Assert.Contains(active.Nodes, node => node.NodeNId == "tenant.custom.group" && node.Label == "租户自定义");
                foreach (var resource in SystemDataBaselineSeedRunner.RequiredCollaborationResourceFacts)
                    Assert.Contains(active.Nodes, node => node.ResourceNId == resource.NId && node.Label == resource.Name);
                var navigation = new ResourceNavigationService(store, new VerifiedPermissionRegistry());
                foreach (var terminal in new[] { UiTerminal.Pda, UiTerminal.Mobile })
                {
                    var runtime = await navigation.RuntimeAsync(Tenant, terminal, CancellationToken.None);
                    var prefix = terminal == UiTerminal.Pda ? "pda" : "mobile";
                    var chat = Assert.Single(runtime.Nodes, node => node.NodeNId == $"collaboration.nav.{prefix}-chat");
                    Assert.Equal($"{prefix}-collaboration-chat", chat.RouteName);
                    Assert.Equal("collaboration.messaging.read", chat.RequiredPermissionNId);
                    Assert.Equal("聊天", chat.Label);
                    Assert.DoesNotContain(runtime.Nodes, node => node.NodeNId == "collaboration.nav.pc-chat");
                }
                foreach (var key in seedKeys)
                {
                    var ledger = await db.SqlSugar.Queryable<SystemDataSeedLedgerTable>().Where(row => row.TenantNId == Tenant && row.SeedKey == key).ToListAsync();
                    Assert.Equal(2, ledger.Count);
                    Assert.Contains(ledger, row => row.SeedVersion == oldVersion && row.Checksum == Checksum(key));
                    Assert.Contains(ledger, row => row.SeedVersion == SystemDataBaselineSeedRunner.CollaborationManifestVersion);
                }
                await initializer.ApplyAsync(context, await initializer.PlanAsync(context, after, CancellationToken.None), CancellationToken.None);
                Assert.Equal(state.Revision, (await store.LoadAsync(Tenant, CancellationToken.None)).Revision);
                Assert.True((await initializer.VerifyAsync(context, CancellationToken.None)).Ready);
            }
        }
        finally
        {
            try { if (File.Exists(dbPath)) File.Delete(dbPath); }
            catch (IOException) { /* SQLite pooling can briefly retain the temporary file. */ }
        }
    }

    private static SqlSugarDbContext CreateDbContext(string dbPath) => new(Options.Create(new SqlSugarOptions
    {
        ConnectionString = $"Data Source={dbPath}",
        DbType = DbType.Sqlite,
    }));

    private static ServiceInitializationContext CreateContext(string operationNId) => new(
        "Test",
        Tenant,
        operationNId,
        "systemdata",
        "systemdata",
        new ResolvedDatabaseTarget("Test", DatabaseTopologyMode.Shared, "systemdata", DatabaseProvider.Sqlite, "systemdata_db", "target", false),
        SystemDataSchemaMigrations.All[^1].Id,
        ServiceInitializationPolicy.Standard,
        "trace-real-sql");

    private static string Checksum(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class BaselineHarness : IDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-systemdata-baseline-{Guid.NewGuid():N}.db");
        private readonly SqlSugarDbContext _dbContext;

        public BaselineHarness(bool includeLegacySeed = false, string legacyParameterName = "参数管理")
        {
            Store = new BaselineStore(Tenant);
            if (includeLegacySeed) Store.SeedLegacyState(legacyParameterName);

            _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
            {
                ConnectionString = $"Data Source={_dbPath}",
                DbType = DbType.Sqlite,
            }));
            Seeder = new SystemDataBaselineSeedRunner(
                new ConfigurationBuilder().Build(),
                Store,
                NullLogger<SystemDataBaselineSeedRunner>.Instance,
                new VerifiedPermissionRegistry());
            Initializer = new SystemDataServiceInitializer(
                new SchemaMigrationRunner(_dbContext, SystemDataSchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance),
                _dbContext,
                Store,
                Seeder);
            Context = new ServiceInitializationContext(
                "Test",
                Tenant,
                "operation-baseline",
                "systemdata",
                "systemdata",
                new ResolvedDatabaseTarget(
                    "Test",
                    DatabaseTopologyMode.Shared,
                    "systemdata",
                    DatabaseProvider.Sqlite,
                    "systemdata_db",
                    "target",
                    false),
                SystemDataSchemaMigrations.All[^1].Id,
                ServiceInitializationPolicy.Standard,
                "trace-baseline");
        }

        public BaselineStore Store { get; }
        public SystemDataBaselineSeedRunner Seeder { get; }
        public SystemDataServiceInitializer Initializer { get; }
        public ServiceInitializationContext Context { get; }

        public void Dispose()
        {
            Seeder.Dispose();
            _dbContext.Dispose();
            try
            {
                if (File.Exists(_dbPath)) File.Delete(_dbPath);
            }
            catch (IOException)
            {
                // SQLite connection pooling may briefly retain the file handle.
            }
        }
    }

    private sealed class BaselineStore(string tenantNId) : IControlPlaneStore
    {
        private readonly HashSet<SeedIdentity> _seeds = [];

        public ControlPlaneSnapshot Snapshot { get; private set; } = ControlPlaneSnapshot.Empty(tenantNId);
        public int CommitCount { get; private set; }
        public IReadOnlyCollection<string> AppliedSeedKeys => _seeds.Select(seed => seed.Key).ToArray();

        public Task<ControlPlaneSnapshot> LoadAsync(string requestedTenantNId, CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot with { TenantNId = requestedTenantNId });

        public Task<long> CommitAsync(
            ControlPlaneSnapshot snapshot,
            long expectedRevision,
            ControlPlaneCommit commit,
            CancellationToken cancellationToken)
        {
            if (Snapshot.Revision != expectedRevision) throw new InvalidOperationException("revision conflict");
            Snapshot = snapshot with { Revision = expectedRevision + 1 };
            foreach (var seed in commit.Seeds ?? [])
                _seeds.Add(new(seed.SeedKey, seed.SeedVersion, seed.Checksum));
            CommitCount++;
            return Task.FromResult(Snapshot.Revision);
        }

        public Task<bool> SeedAppliedAsync(
            string requestedTenantNId,
            string seedKey,
            string seedVersion,
            string checksum,
            CancellationToken cancellationToken) =>
            Task.FromResult(_seeds.Contains(new(seedKey, seedVersion, checksum)));

        public void ReplaceSnapshot(ControlPlaneSnapshot snapshot) => Snapshot = snapshot;

        public void RemoveSeed(string key) => _seeds.RemoveWhere(seed => seed.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        public void SeedLegacyState(string legacyParameterName)
        {
            var legacyChecksum = Checksum("SDM-013");
            var manifest = new ModuleManifestState(
                tenantNId,
                "systemdata",
                "1",
                legacyChecksum,
                SystemDataBaselineSeedRunner.RequiredPermissionNIds,
                "1",
                legacyChecksum,
                DateTimeOffset.UtcNow)
            {
                PermissionDeclarationItems = SystemDataBaselineSeedRunner.RequiredPermissionNIds
                    .Select(permission => new PermissionManifestEntry(permission, permission, "permission", null))
                    .ToArray(),
            };
            var legacyResource = UiResource.Create(
                tenantNId,
                "systemdata.navigation",
                "systemdata",
                "1",
                UiResourceType.Page,
                "旧导航入口",
                "/systemdata/navigation",
                "systemdata.navigation.view",
                [UiTerminal.Pc]);
            var customResource = UiResource.Create(
                tenantNId,
                "tenant.navigation.custom",
                "tenant-custom",
                "1",
                UiResourceType.Page,
                "租户自定义入口",
                "tenant-custom",
                "tenant.custom.view",
                [UiTerminal.Pc]);
            var legacyReferenceDataResource = UiResource.Create(
                tenantNId,
                "referencedata.navigation.metadata",
                "referencedata",
                "1",
                UiResourceType.Page,
                "元数据 Schema",
                "reference-data-metadata",
                "referencedata.metadata.view",
                [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile]);
            var legacyParameterResource = UiResource.Create(
                tenantNId,
                "referencedata.navigation.parameters",
                "referencedata",
                "1",
                UiResourceType.Page,
                legacyParameterName,
                "reference-data-parameters",
                "referencedata.parameter.view",
                [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile]);
            Snapshot = Snapshot with
            {
                Resources = [legacyResource, customResource, legacyReferenceDataResource, legacyParameterResource],
                Manifests = [manifest],
                PermissionReceipts = [new PermissionReceipt("systemdata", "1", legacyChecksum, true)],
            };
            _seeds.Add(new("SDM-013", "1", legacyChecksum));
        }

        public void SeedLegacyCurrentManifest()
        {
            var checksum = Checksum(SystemDataBaselineSeedRunner.CurrentManifestSeedKey);
            var permissions = SystemDataBaselineSeedRunner.RequiredPermissionNIds.ToArray();
            var manifest = new ModuleManifestState(
                tenantNId,
                "systemdata",
                "2",
                checksum,
                permissions,
                "2",
                checksum,
                DateTimeOffset.UtcNow)
            {
                PermissionDeclarationItems = permissions
                    .Select(permission => new PermissionManifestEntry(permission, permission, "permission", null))
                    .ToArray(),
            };
            var resources = SystemDataBaselineSeedRunner.RequiredResourceFacts
                .Select(resource => UiResource.Create(
                    tenantNId,
                    resource.NId,
                    "systemdata",
                    "2",
                    UiResourceType.Page,
                    resource.Name,
                    resource.RouteName,
                    resource.PermissionNId,
                    [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile]))
                .ToArray();
            Snapshot = Snapshot with
            {
                Manifests = [manifest],
                PermissionReceipts = [new PermissionReceipt("systemdata", "2", checksum, true)],
                Resources = resources,
            };
            _seeds.Add(new(SystemDataBaselineSeedRunner.CurrentManifestSeedKey, "2", checksum));
            _seeds.Add(new(
                SystemDataBaselineSeedRunner.ResourceConsistencySeedKey,
                "1",
                Checksum(SystemDataBaselineSeedRunner.ResourceConsistencySeedKey)));
        }

        private static string Checksum(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        private readonly record struct SeedIdentity(string Key, string Version, string Checksum);
    }

    private sealed class VerifiedPermissionRegistry : IIdentityPermissionRegistry
    {
        public Task<PermissionRegistrationReceipt?> VerifyAsync(PermissionManifestV1 manifest, CancellationToken cancellationToken) =>
            Task.FromResult<PermissionRegistrationReceipt?>(new(
                manifest.ModuleNId,
                manifest.ManifestVersion,
                manifest.Checksum,
                true,
                DateTimeOffset.UtcNow));
    }
}
