using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;
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
        Assert.Contains("SDM-013", harness.Store.AppliedSeedKeys);
        Assert.Contains("SDM-017", harness.Store.AppliedSeedKeys);

        var revision = harness.Store.Snapshot.Revision;
        var commitCount = harness.Store.CommitCount;
        await harness.Initializer.ApplyAsync(harness.Context, plan, CancellationToken.None);

        var second = await harness.Initializer.InspectAsync(harness.Context, CancellationToken.None);
        Assert.True(second.Ready);
        Assert.Equal(revision, harness.Store.Snapshot.Revision);
        Assert.Equal(commitCount, harness.Store.CommitCount);
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
                var legacyChecksum = Checksum("SDM-013");
                var manifest = new ModuleManifestState(Tenant, "systemdata", "1", legacyChecksum, SystemDataBaselineSeedRunner.RequiredPermissionNIds, "1", legacyChecksum, DateTimeOffset.UtcNow)
                {
                    PermissionDeclarationItems = SystemDataBaselineSeedRunner.RequiredPermissionNIds
                        .Select(permission => new PermissionManifestEntry(permission, permission, "permission", null))
                        .ToArray(),
                };
                var legacyResource = UiResource.Create(Tenant, "systemdata.navigation", "systemdata", "1", UiResourceType.Page, "旧导航入口", "/systemdata/navigation", "systemdata.navigation.view", [UiTerminal.Pc]);
                var customResource = UiResource.Create(Tenant, "tenant.navigation.custom", "tenant-custom", "1", UiResourceType.Page, "租户自定义入口", "tenant-custom", "tenant.custom.view", [UiTerminal.Pc]);
                var node = NavigationNode.CreateLink(Tenant, "legacy.navigation.node", "旧导航", null, "PLATFORM_NAVIGATION", "systemdata.navigation", null, [UiTerminal.Pc]);
                await store.CommitAsync(
                    new ControlPlaneSnapshot(Tenant, 0, [manifest], [legacyResource, customResource], [node], [], null, null, [], [], [], null, [new PermissionReceipt("systemdata", "1", legacyChecksum, true)]),
                    0,
                    new ControlPlaneCommit([], [], [new SeedLedgerEntry(Tenant, "SDM-013", "1", legacyChecksum)]),
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

        public BaselineHarness(bool includeLegacySeed = false)
        {
            Store = new BaselineStore(Tenant);
            if (includeLegacySeed) Store.SeedLegacyState();

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

        public void SeedLegacyState()
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
            Snapshot = Snapshot with
            {
                Resources = [legacyResource, customResource],
                Manifests = [manifest],
                PermissionReceipts = [new PermissionReceipt("systemdata", "1", legacyChecksum, true)],
            };
            _seeds.Add(new("SDM-013", "1", legacyChecksum));
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
