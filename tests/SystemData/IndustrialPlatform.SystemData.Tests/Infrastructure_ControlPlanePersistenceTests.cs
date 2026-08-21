using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;
using IndustrialPlatform.SystemData.Tests;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

public sealed class ControlPlanePersistenceTests : IDisposable
{
    static ControlPlanePersistenceTests() => Batteries_V2.Init();
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-control-plane-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext _db;

    public ControlPlanePersistenceTests() => _db = new SqlSugarDbContext(Options.Create(new SqlSugarOptions { ConnectionString = $"Data Source={_dbPath}", DbType = DbType.Sqlite }));

    [Fact]
    public async Task Sql_store_survives_a_new_service_instance_and_keeps_revisioned_rows()
    {
        var runner = new SchemaMigrationRunner(_db, SystemDataSchemaMigrations.All.Select(x => new SchemaMigrationStep(x.Id, x.Description, x.Apply)), NullLogger<SchemaMigrationRunner>.Instance);
        await runner.ApplyPendingAsync();
        var store = new SqlControlPlaneStore(_db);
        var registry = new TestPermissionRegistry();
        var first = new ResourceNavigationService(store, registry);
        await first.RegisterManifestAsync("tenant-a", new IndustrialPlatform.SystemData.Contracts.ControlPlane.RegisterModuleManifestRequest { ModuleNId = "module-a", ManifestVersion = "1", Checksum = "checksum", PermissionNIds = ["permission-a"] }, CancellationToken.None);
        await first.RegisterResourceAsync("tenant-a", new IndustrialPlatform.SystemData.Contracts.ControlPlane.RegisterUiResourceRequest { ResourceNId = "resource-a", OwnerModuleNId = "module-a", ManifestVersion = "1", Type = "Page", Name = "页面", RouteName = "route-a", RequiredPermissionNId = "permission-a", SupportedTerminals = ["Pc"] }, CancellationToken.None);

        var second = new ResourceNavigationService(new SqlControlPlaneStore(_db), registry);
        var resources = await second.ListResourcesAsync("tenant-a", CancellationToken.None);

        Assert.Equal("resource-a", Assert.Single(resources).ResourceNId);
        Assert.Equal(2, await _db.SqlSugar.Queryable<IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities.SystemDataProjectionRevisionTable>().Where(x => x.TenantNId == "tenant-a").Select(x => x.Revision).FirstAsync());
    }

    [Fact]
    public async Task Sql_store_stale_revision_fails_before_replacing_the_snapshot()
    {
        var runner = new SchemaMigrationRunner(_db, SystemDataSchemaMigrations.All.Select(x => new SchemaMigrationStep(x.Id, x.Description, x.Apply)), NullLogger<SchemaMigrationRunner>.Instance);
        await runner.ApplyPendingAsync();
        var store = new SqlControlPlaneStore(_db);
        var tenant = $"tenant-stale-{Guid.NewGuid():N}";
        await store.CommitAsync(ControlPlaneSnapshot.Empty(tenant), 0, ProbeCommit(tenant), CancellationToken.None);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            store.CommitAsync(ControlPlaneSnapshot.Empty(tenant), 0, ProbeCommit(tenant), CancellationToken.None));

        var loaded = await store.LoadAsync(tenant, CancellationToken.None);
        Assert.Equal(1, loaded.Revision);
        Assert.Equal(1, await _db.SqlSugar.Queryable<IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities.SystemDataProjectionRevisionTable>()
            .Where(x => x.TenantNId == tenant)
            .Select(x => x.Revision)
            .FirstAsync());
    }

    [Fact]
    public async Task Sql_store_rejects_nonzero_initial_expected_revision()
    {
        var runner = new SchemaMigrationRunner(_db, SystemDataSchemaMigrations.All.Select(x => new SchemaMigrationStep(x.Id, x.Description, x.Apply)), NullLogger<SchemaMigrationRunner>.Instance);
        await runner.ApplyPendingAsync();
        var tenant = $"tenant-initial-cas-{Guid.NewGuid():N}";

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            new SqlControlPlaneStore(_db).CommitAsync(ControlPlaneSnapshot.Empty(tenant), 1, ProbeCommit(tenant), CancellationToken.None));
    }

    [Fact]
    public async Task Projection_revision_rejects_duplicate_tenant_area_rows()
    {
        var runner = new SchemaMigrationRunner(_db, SystemDataSchemaMigrations.All.Select(x => new SchemaMigrationStep(x.Id, x.Description, x.Apply)), NullLogger<SchemaMigrationRunner>.Instance);
        await runner.ApplyPendingAsync();
        var tenant = $"tenant-unique-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var first = new IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities.SystemDataProjectionRevisionTable
        {
            Id = Guid.NewGuid(), TenantNId = tenant, Area = "control-plane", Revision = 1, GeneratedOn = now,
            IsFrozen = false, IsLocked = false, IsDeleted = false, EntityType = "SystemData.ProjectionRevision",
            CreatedOn = now, LastUpdatedOn = now, OptimisticVersion = 1, ConcurrencyVersion = Guid.NewGuid(),
        };
        await _db.SqlSugar.Insertable(first).ExecuteCommandAsync();

        var duplicate = new IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities.SystemDataProjectionRevisionTable
        {
            Id = Guid.NewGuid(), TenantNId = tenant, Area = "control-plane", Revision = 2, GeneratedOn = now,
            IsFrozen = false, IsLocked = false, IsDeleted = false, EntityType = "SystemData.ProjectionRevision",
            CreatedOn = now, LastUpdatedOn = now, OptimisticVersion = 1, ConcurrencyVersion = Guid.NewGuid(),
        };
        await Assert.ThrowsAnyAsync<Exception>(() => _db.SqlSugar.Insertable(duplicate).ExecuteCommandAsync());
    }

    [Fact]
    public async Task Sql_store_initial_commit_allows_only_one_concurrent_writer()
    {
        var runner = new SchemaMigrationRunner(_db, SystemDataSchemaMigrations.All.Select(x => new SchemaMigrationStep(x.Id, x.Description, x.Apply)), NullLogger<SchemaMigrationRunner>.Instance);
        await runner.ApplyPendingAsync();
        var tenant = $"tenant-concurrent-initial-{Guid.NewGuid():N}";
        using var firstDb = new SqlSugarDbContext(Options.Create(new SqlSugarOptions { ConnectionString = $"Data Source={_dbPath}", DbType = DbType.Sqlite }));
        using var secondDb = new SqlSugarDbContext(Options.Create(new SqlSugarOptions { ConnectionString = $"Data Source={_dbPath}", DbType = DbType.Sqlite }));

        var attempts = await Task.WhenAll(
            TryCommitAsync(new SqlControlPlaneStore(firstDb), tenant),
            TryCommitAsync(new SqlControlPlaneStore(secondDb), tenant));

        Assert.Single(attempts, x => x.Succeeded);
        Assert.Single(attempts, x => x.Error is not null);
    }

    private static async Task<(bool Succeeded, Exception? Error)> TryCommitAsync(SqlControlPlaneStore store, string tenant)
    {
        try
        {
            await store.CommitAsync(ControlPlaneSnapshot.Empty(tenant), 0, ProbeCommit(tenant), CancellationToken.None);
            return (true, null);
        }
        catch (Exception exception)
        {
            return (false, exception);
        }
    }

    private static ControlPlaneCommit ProbeCommit(string tenant) =>
        new(
            [new ControlPlaneEvent(Guid.NewGuid(), "SystemData.PersistenceProbe.v1", "v1", tenant, "{}", DateTimeOffset.UtcNow)],
            []);

    public void Dispose()
    {
        _db.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }
}
