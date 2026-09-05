using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Infrastructure.Persistence;
using IndustrialPlatform.SharedKernel.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class UnitOfMeasurePersistenceTests : IAsyncLifetime, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pf03-uom-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext _context;
    private readonly UnitDimensionRepository _repository;
    private readonly UnitDimensionService _service;
    private static readonly ReferenceDataActor Tenant = new("TENANT-A", "USER-A", false, "uom-test");
    private static readonly ReferenceDataActor Platform = Tenant with { CanManagePlatform = true };
    private static CancellationToken Ct => CancellationToken.None;

    public UnitOfMeasurePersistenceTests()
    {
        _context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={_path};Pooling=False",
        }));
        _repository = new UnitDimensionRepository(_context);
        _service = new UnitDimensionService(_repository);
    }

    public async Task InitializeAsync()
    {
        await _context.SqlSugar.Ado.ExecuteCommandAsync("PRAGMA foreign_keys=ON;"
            + UnitOfMeasureMigration.Sql(false) + ReferenceDataSharedMigration.Sql(false)
            + ReferenceDataCacheGenerationMigration.Sql(false));
    }

    [Fact]
    public async Task Available_keeps_platform_and_tenant_sources_side_by_side_and_fixed_revisions_never_fallback()
    {
        var platform = await CreateAsync(Platform, "Platform", "CustomMass", Unit("kg", "1"), Unit("g", "0.001"));
        platform = await _service.PublishAsync(Platform, platform.Id, Version(platform), Ct);
        var tenant = await CreateAsync(Tenant, "Tenant", "CustomMass", Unit("kg", "1"), Unit("g", "0.002"));
        tenant = await _service.PublishAsync(Tenant, tenant.Id, Version(tenant), Ct);

        var available = await _service.ListAvailableAsync(Tenant, new(1, 100, "CustomMass"), Ct);
        Assert.Equal(2, available.Total);
        Assert.Contains(available.Items, item => item.SourceScope == "Platform" && item.SourceTenantNId is null);
        Assert.Contains(available.Items, item => item.SourceScope == "Tenant" && item.SourceTenantNId == Tenant.TenantNId);
        Assert.Equal(platform.Revision, (await _service.GetCurrentAsync(Tenant, "CustomMass", "Platform", null, Ct)).Revision);
        Assert.Equal(tenant.Revision, (await _service.GetCurrentAsync(Tenant, "CustomMass", "Tenant", Tenant.TenantNId, Ct)).Revision);

        var clone = await _service.CloneAsync(Tenant, tenant.Id, Version(tenant), Ct);
        clone = await _service.UpdateAsync(Tenant, clone.Id, new("Custom mass v2", null, "Ratio", "kg",
            [Unit("kg", "1"), Unit("g", "0.001")], clone.OptimisticVersion, clone.ConcurrencyVersion), Ct);
        clone = await _service.PublishAsync(Tenant, clone.Id, Version(clone), Ct);
        Assert.Equal(1, (await _service.GetRevisionAsync(Tenant, "CustomMass", 1, "Tenant", Tenant.TenantNId, Ct)).Revision);
        Assert.Equal(clone.Revision, (await _service.GetCurrentAsync(Tenant, "CustomMass", "Tenant", Tenant.TenantNId, Ct)).Revision);

        var oldSnapshot = await _service.ConvertAsync(Tenant,
            new("Tenant", Tenant.TenantNId, "CustomMass", 1, "kg", "g", "1"), Ct);
        Assert.Equal("500.000", oldSnapshot.ResultValue);

        clone = await _service.DisableAsync(Tenant, clone.Id, Version(clone), Ct);
        Assert.Equal("Disabled", (await _service.GetRevisionAsync(Tenant, "CustomMass", 2, "Tenant", Tenant.TenantNId, Ct)).Status);
        var missing = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            _service.GetCurrentAsync(Tenant, "CustomMass", "Tenant", Tenant.TenantNId, Ct));
        Assert.Equal("REF-UNIT-DIMENSION-NOT-FOUND", missing.ErrorCode);
        Assert.Equal(platform.Revision, (await _service.GetCurrentAsync(Tenant, "CustomMass", "Platform", null, Ct)).Revision);
    }

    [Fact]
    public async Task Decimal_strings_round_trip_conversion_and_root_versions_make_publication_atomic()
    {
        var dimension = await CreateAsync(Tenant, "Tenant", "Precise", Unit("base", "1.000000000000"),
            Unit("small", "0.000000000001", places: 12));
        var stale = dimension;
        dimension = await _service.PublishAsync(Tenant, dimension.Id, Version(dimension), Ct);
        Assert.NotEqual(stale.ConcurrencyVersion, dimension.ConcurrencyVersion);
        await Assert.ThrowsAsync<ReferenceDataException>(() => _service.DisableAsync(Tenant, dimension.Id, Version(stale), Ct));

        var converted = await _service.ConvertAsync(Tenant,
            new("Tenant", Tenant.TenantNId, "Precise", dimension.Revision, "base", "small", "0.000000000001"), Ct);
        Assert.Equal("1.000000000000", converted.ResultValue);
        Assert.Equal("0.000000000001", converted.ConversionSnapshot.TargetFactorToBase);
        Assert.False(converted.WasRounded);

        var loaded = await _service.GetAsync(Tenant, dimension.Id, Ct);
        Assert.Equal(dimension.LastUpdatedOn, loaded.LastUpdatedOn);
        Assert.Equal(dimension.PublishedOn, loaded.PublishedOn);
        Assert.Equal(TimeSpan.Zero, loaded.LastUpdatedOn.Offset);
        Assert.Equal(TimeSpan.Zero, loaded.PublishedOn!.Value.Offset);
    }

    [Fact]
    public async Task System_seed_is_published_and_cannot_be_changed_by_normal_application_commands()
    {
        var mass = await _service.GetCurrentAsync(Tenant, "Mass", "Platform", null, Ct);
        Assert.True(mass.IsSystemDefined);
        Assert.Equal(["G", "KG"], mass.Units.Select(unit => unit.NId).Order().ToArray());
        var converted = await _service.ConvertAsync(Tenant,
            new("Platform", null, "Mass", mass.Revision, "kg", "g", "1"), Ct);
        Assert.Equal("1000.000", converted.ResultValue);
        Assert.Equal("1000.000", (await _service.ConvertAsync(Tenant,
            new("Platform", null, "Volume", 1, "L", "mL", "1"), Ct)).ResultValue);
        Assert.Equal("32.00", (await _service.ConvertAsync(Tenant,
            new("Platform", null, "Absolute_Temperature", 1, "degC", "degF", "0"), Ct)).ResultValue);
        Assert.Equal("REF-UNIT-CONVERSION-INVALID", (await Assert.ThrowsAsync<ReferenceDataException>(() =>
            _service.ConvertAsync(Tenant, new("Platform", null, "Mass", 1, "kg", "L", "1"), Ct))).ErrorCode);

        var summaries = await _service.SearchAsync(Platform, new(1, 100, "Mass", "Published", "Platform"), Ct);
        var detail = await _service.GetAsync(Platform, summaries.Items.Single(item => item.IsSystemDefined && item.NId == "MASS").Id, Ct);
        var update = new UpdateUnitDimensionRequest("Changed", detail.Description, detail.ConversionKind, detail.BaseUnitNId,
            detail.Units.Select(unit => new UnitDefinitionRequest(unit.NId, unit.Name, unit.Symbol, unit.FactorToBase,
                unit.OffsetToBase, unit.DecimalPlaces, unit.RoundingMode, unit.Enabled, unit.Sort)).ToArray(),
            detail.OptimisticVersion, detail.ConcurrencyVersion);
        foreach (var action in new Func<Task>[]
        {
            () => _service.UpdateAsync(Platform, detail.Id, update, Ct),
            () => _service.CloneAsync(Platform, detail.Id, Version(detail), Ct),
            () => _service.PublishAsync(Platform, detail.Id, Version(detail), Ct),
            () => _service.DisableAsync(Platform, detail.Id, Version(detail), Ct),
        })
        {
            var error = await Assert.ThrowsAsync<ReferenceDataException>(action);
            Assert.Equal("REF-UNIT-SYSTEM-DEFINED", error.ErrorCode);
        }
    }

    [Fact]
    public async Task Repository_rejects_system_mutation_even_when_base_entity_methods_are_used()
    {
        var system = await _repository.GetCurrentAsync(Tenant.TenantNId, "MASS", "Platform", Ct);
        Assert.NotNull(system);
        var expectedVersion = system.OptimisticVersion;
        var expectedToken = system.ConcurrencyVersion;
        ((Entity)system).MarkDeleted();

        var error = await Assert.ThrowsAsync<ReferenceDataException>(() => _repository.SaveAsync(
            [new(system, expectedVersion, expectedToken)], Ct));

        Assert.Equal("REF-UNIT-SYSTEM-DEFINED", error.ErrorCode);
        Assert.False((await _repository.GetCurrentAsync(Tenant.TenantNId, "MASS", "Platform", Ct))!.IsDeleted);
    }

    [Fact]
    public async Task A_second_clone_of_the_same_published_source_returns_a_concurrency_conflict()
    {
        var published = await CreateAsync(Tenant, "Tenant", "CloneRace", Unit("base", "1"));
        published = await _service.PublishAsync(Tenant, published.Id, Version(published), Ct);
        var sourceVersion = Version(published);
        await _service.CloneAsync(Tenant, published.Id, sourceVersion, Ct);

        var error = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            _service.CloneAsync(Tenant, published.Id, sourceVersion, Ct));

        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public async Task Unit_rows_have_an_enforced_ordinary_parent_foreign_key_and_clone_uses_new_ids()
    {
        var definition = await CreateAsync(Tenant, "Tenant", "LengthX", Unit("m", "1"), Unit("mm", "0.001"));
        definition = await _service.PublishAsync(Tenant, definition.Id, Version(definition), Ct);
        var clone = await _service.CloneAsync(Tenant, definition.Id, Version(definition), Ct);
        Assert.NotEqual(definition.Id, clone.Id);
        Assert.Equal(definition.Revision, clone.SourceRevision);
        Assert.All(clone.Units, unit => Assert.DoesNotContain(definition.Units, old => old.Id == unit.Id));

        var error = await Assert.ThrowsAnyAsync<Exception>(() => _context.SqlSugar.Ado.ExecuteCommandAsync($"""
            INSERT INTO reference_data_unit_of_measure_unit
                (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
                 is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version)
            VALUES ('{Guid.NewGuid()}','{Guid.NewGuid()}','BAD','Bad','bad','1','0',0,'ToEven',1,0,0,0,0,'Bad','2026-09-05T00:00:00Z','2026-09-05T00:00:00Z',0,'{Guid.NewGuid()}')
            """));
        Assert.Contains("FOREIGN KEY", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_is_2_7_005_and_uses_postgres_numeric_constraints_and_normal_foreign_key()
    {
        Assert.Equal("reference-data-2.7-005", UnitOfMeasureMigration.Version);
        var sql = UnitOfMeasureMigration.Sql(true);
        Assert.Contains("numeric(28,12)", sql, StringComparison.Ordinal);
        Assert.Contains("factor_to_base numeric(28,12) NOT NULL CHECK(factor_to_base>0)", sql, StringComparison.Ordinal);
        Assert.Contains("REFERENCES reference_data.unit_of_measure_dimension(id)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("unit_dimension_id,is_deleted", sql, StringComparison.Ordinal);
        Assert.Contains('\n', sql);
    }

    [Fact]
    public async Task Sqlite_rejects_malformed_or_out_of_scale_numbers_and_invalid_publication_state()
    {
        var definition = await CreateAsync(Tenant, "Tenant", "SqliteChecks",
            Unit("base", "1"), Unit("other", "0.001"));
        var unitId = definition.Units.Single(unit => unit.NId == "OTHER").Id;
        foreach (var assignment in new[]
        {
            "factor_to_base='1junk'",
            "factor_to_base='0.1234567890123'",
            "offset_to_base='1junk'",
            "offset_to_base='0.1234567890123'",
        })
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => _context.SqlSugar.Ado.ExecuteCommandAsync(
                $"UPDATE reference_data_unit_of_measure_unit SET {assignment} WHERE id='{unitId}'"));
            Assert.Contains("CHECK", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        var lifecycle = await Assert.ThrowsAnyAsync<Exception>(() => _context.SqlSugar.Ado.ExecuteCommandAsync(
            $"UPDATE reference_data_unit_of_measure_dimension SET published_on='2026-09-05T00:00:00Z' WHERE id='{definition.Id}'"));
        Assert.Contains("CHECK", lifecycle.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<UnitDimensionDetailDto> CreateAsync(ReferenceDataActor actor, string scope, string nId,
        params UnitDefinitionRequest[] units) => await _service.CreateAsync(actor,
            new(scope, null, nId, nId, null, "Ratio", units[0].NId, units), Ct);
    private static UnitDefinitionRequest Unit(string nId, string factor, string offset = "0", int places = 3,
        string rounding = "ToEven", bool enabled = true, int sort = 0) =>
        new(nId, nId, nId, factor, offset, places, rounding, enabled, sort);
    private static PublishOrDisableRequest Version(UnitDimensionDetailDto dimension) =>
        new(dimension.OptimisticVersion, dimension.ConcurrencyVersion, "Unit test state change");
    public void Dispose()
    {
        _context.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }
    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }
}
