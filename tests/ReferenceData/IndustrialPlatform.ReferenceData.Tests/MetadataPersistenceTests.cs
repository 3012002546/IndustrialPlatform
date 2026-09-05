using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Application.Metadata;
using IndustrialPlatform.ReferenceData.Application.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.Dictionary;
using IndustrialPlatform.ReferenceData.Contracts.Metadata;
using IndustrialPlatform.ReferenceData.Contracts.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.ReferenceData.Infrastructure.Metadata;
using IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class MetadataPersistenceTests : IAsyncLifetime, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pf03-metadata-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext _context;
    private readonly DictionaryService _dictionaries;
    private readonly UnitDimensionService _units;
    private readonly MetadataSchemaService _service;
    private static readonly ReferenceDataActor Tenant = new("TENANT-A", "USER-A", false, "metadata-test");
    private static readonly ReferenceDataActor Platform = Tenant with { CanManagePlatform = true };
    private static CancellationToken Ct => CancellationToken.None;

    public MetadataPersistenceTests()
    {
        _context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={_path};Pooling=False;Foreign Keys=True",
        }));
        _dictionaries = new DictionaryService(new DictionaryRepository(_context));
        _units = new UnitDimensionService(new UnitDimensionRepository(_context));
        _service = new MetadataSchemaService(new MetadataSchemaRepository(_context), _dictionaries, _units);
    }

    public async Task InitializeAsync()
    {
        var initializer = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(_context));
        var context = new ServiceInitializationContext("Test", Tenant.TenantNId, "OP-METADATA", "referencedata", "referencedata",
            new ResolvedDatabaseTarget("Test", DatabaseTopologyMode.PerService, "referencedata", DatabaseProvider.Sqlite,
                "referencedata_db", _path, false), ReferenceDataServiceInitializer.CurrentVersion,
            ServiceInitializationPolicy.Standard, Tenant.TraceId);
        var inspection = await initializer.InspectAsync(context, Ct);
        Assert.True((await initializer.ApplyAsync(context, await initializer.PlanAsync(context, inspection, Ct), Ct)).Ready);
        await EnsureMetadataMigrationAsync(_context);
    }

    [Fact]
    public async Task Effective_resolution_and_explicit_historical_revision_never_mix_sources()
    {
        var platform = await CreateAsync(Platform, "Platform", "Equipment", [Attribute("Serial", "String")]);
        platform = await PublishAsync(Platform, platform);
        var tenant = await CreateAsync(Tenant, "Tenant", "Equipment", [Attribute("Voltage", "Decimal") with { Precision = 12, Scale = 3 }]);
        tenant = await PublishAsync(Tenant, tenant);

        var effective = await _service.GetEffectiveAsync(Tenant, "Equipment", null, Ct);
        Assert.Equal("Tenant", effective.SourceScope);
        Assert.Equal(["VOLTAGE"], effective.Attributes.Select(attribute => attribute.NId).ToArray());
        Assert.Equal("Platform", (await _service.GetRevisionAsync(Tenant, "Equipment", 1, "Platform", null, Ct)).SourceScope);
        Assert.Equal("Tenant", (await _service.GetRevisionAsync(Tenant, "Equipment", 1, "Tenant", "TENANT-A", Ct)).SourceScope);

        var clone = await _service.CloneAsync(Tenant, tenant.Id, Version(tenant), Ct);
        clone = await _service.UpdateAsync(Tenant, clone.Id, new("Equipment v2", null,
        [
            Attribute("Voltage", "Decimal") with { Precision = 8, Scale = 2 },
            Attribute("Mandatory", "Boolean") with { Required = true },
        ], clone.OptimisticVersion, clone.ConcurrencyVersion), Ct);
        var check = await _service.CheckPublicationAsync(Tenant, clone.Id, Ct);
        Assert.Contains("VOLTAGE", check.TightenedAttributes);
        Assert.Contains(check.IncompatibleChanges, change => change.AttributeNId == "VOLTAGE" && change.Code == "PrecisionTightened");
        Assert.Contains(check.IncompatibleChanges, change => change.AttributeNId == "MANDATORY" && change.Code == "RequiredAttributeAdded");
        clone = await PublishAsync(Tenant, clone);

        var old = await _service.GetRevisionAsync(Tenant, "Equipment", 1, "Tenant", "TENANT-A", Ct);
        Assert.Equal(["VOLTAGE"], old.Attributes.Select(attribute => attribute.NId).ToArray());
        Assert.Equal(2, (await _service.GetEffectiveAsync(Tenant, "Equipment", null, Ct)).Revision);
    }

    [Fact]
    public async Task Publication_validates_one_published_dictionary_snapshot_and_fixed_unit_revision()
    {
        var dictionary = await _dictionaries.CreateAsync(Tenant,
            new("Tenant", null, "EquipmentMode", "Equipment mode", null, [new("AUTO", "Automatic", null, 0, true)]), Ct);
        await _dictionaries.PublishAsync(Tenant, dictionary.Id,
            new(dictionary.OptimisticVersion, dictionary.ConcurrencyVersion, "Metadata test"), Ct);
        var dimension = await _units.CreateAsync(Tenant, new("Tenant", null, "VoltageDimension", "Voltage", null,
            "Ratio", "v", [new("v", "Volt", "V", "1", "0", 3, "ToEven", true, 0)]), Ct);
        dimension = await _units.PublishAsync(Tenant, dimension.Id, Version(dimension), Ct);

        var valid = await CreateAsync(Tenant, "Tenant", "EquipmentTyped",
        [
            Attribute("Mode", "Enum") with { DictionaryNId = "EquipmentMode" },
            Attribute("Voltage", "Decimal") with
            {
                Precision = 12, Scale = 3, UnitDimensionNId = "VoltageDimension", DefaultUnitNId = "v",
                UnitRevision = dimension.Revision, UnitSourceScope = "Tenant", UnitSourceTenantNId = "TENANT-A",
            },
        ]);
        Assert.Empty((await _service.CheckPublicationAsync(Tenant, valid.Id, Ct)).Errors);
        valid = await PublishAsync(Tenant, valid);
        Assert.Equal(dimension.Revision, valid.Attributes.Single(attribute => attribute.NId == "VOLTAGE").UnitRevision);

        var invalid = await CreateAsync(Tenant, "Tenant", "InvalidTyped",
        [
            Attribute("Mode", "Enum") with { DictionaryNId = "MissingDictionary" },
            Attribute("Weight", "Decimal") with
            {
                UnitDimensionNId = "Mass", DefaultUnitNId = "missing", UnitRevision = 999,
                UnitSourceScope = "Platform",
            },
        ]);
        var check = await _service.CheckPublicationAsync(Tenant, invalid.Id, Ct);
        Assert.Contains(check.Errors, error => error.Code == "REF-METADATA-DICTIONARY-INVALID");
        Assert.Contains(check.Errors, error => error.Code == "REF-METADATA-UNIT-INVALID");
        var error = await Assert.ThrowsAsync<ReferenceDataException>(() => PublishAsync(Tenant, invalid));
        Assert.True(error.ErrorCode is "REF-METADATA-DICTIONARY-INVALID" or "REF-METADATA-UNIT-INVALID");
        Assert.Equal("Draft", (await _service.GetAsync(Tenant, invalid.Id, Ct)).Status);
    }

    [Fact]
    public async Task Clone_uses_new_ids_source_CAS_is_atomic_child_fk_is_enforced_and_sqlite_times_are_utc()
    {
        var schema = await CreateAsync(Tenant, "Tenant", "Asset", [Attribute("Serial", "String")]);
        schema = await PublishAsync(Tenant, schema);
        var clone = await _service.CloneAsync(Tenant, schema.Id, Version(schema), Ct);
        Assert.NotEqual(schema.Id, clone.Id);
        Assert.Equal(schema.Revision, clone.SourceRevision);
        Assert.All(clone.Attributes, attribute => Assert.DoesNotContain(schema.Attributes, old => old.Id == attribute.Id));
        var conflict = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.CloneAsync(Tenant, schema.Id,
            new(schema.OptimisticVersion - 1, schema.ConcurrencyVersion, "stale"), Ct));
        Assert.Equal("REF-CONCURRENCY-CONFLICT", conflict.ErrorCode);

        var loaded = await _service.GetAsync(Tenant, schema.Id, Ct);
        Assert.Equal(TimeSpan.Zero, loaded.LastUpdatedOn.Offset);
        Assert.Equal(TimeSpan.Zero, loaded.PublishedOn!.Value.Offset);
        var fk = await Assert.ThrowsAnyAsync<Exception>(() => _context.SqlSugar.Ado.ExecuteCommandAsync($"""
            INSERT INTO reference_data_metadata_attribute_definition
                (id,entity_schema_id,n_id,name,data_type,required,is_array,enabled,sort,was_published,
                 is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version)
            VALUES ('{Guid.NewGuid()}','{Guid.NewGuid()}','BAD','Bad','String',0,0,1,0,0,0,0,0,'Bad',
                    '2026-09-05T00:00:00Z','2026-09-05T00:00:00Z',0,'{Guid.NewGuid()}')
            """));
        Assert.Contains("FOREIGN KEY", fk.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Current_runtime_omits_disabled_attributes_fixed_revision_keeps_them_and_second_clone_conflicts()
    {
        var schema = await CreateAsync(Tenant, "Tenant", "RuntimeShape", [Attribute("Legacy", "String")]);
        schema = await PublishAsync(Tenant, schema);
        var clone = await _service.CloneAsync(Tenant, schema.Id, Version(schema), Ct);
        var second = await Assert.ThrowsAsync<ReferenceDataException>(() =>
            _service.CloneAsync(Tenant, schema.Id, Version(schema), Ct));
        Assert.Equal("REF-CONCURRENCY-CONFLICT", second.ErrorCode);

        clone = await _service.UpdateAsync(Tenant, clone.Id, new("RuntimeShape", null,
        [
            Attribute("Legacy", "String") with { Enabled = false },
            Attribute("Current", "Boolean"),
        ], clone.OptimisticVersion, clone.ConcurrencyVersion), Ct);
        clone = await PublishAsync(Tenant, clone);

        var current = await _service.GetEffectiveAsync(Tenant, "RuntimeShape", null, Ct);
        Assert.Equal(["CURRENT"], current.Attributes.Select(attribute => attribute.NId).ToArray());
        var fixedRevision = await _service.GetRevisionAsync(Tenant, "RuntimeShape", clone.Revision,
            "Tenant", "TENANT-A", Ct);
        Assert.Equal(["CURRENT", "LEGACY"], fixedRevision.Attributes.Select(attribute => attribute.NId).Order().ToArray());
    }

    [Fact]
    public async Task Sqlite_constraints_reject_invalid_decimal_text_and_impossible_publication_state()
    {
        var schema = await CreateAsync(Tenant, "Tenant", "ConstraintProbe", [Attribute("Good", "String")]);
        var junk = await Assert.ThrowsAnyAsync<Exception>(() => _context.SqlSugar.Ado.ExecuteCommandAsync($"""
            INSERT INTO reference_data_metadata_attribute_definition
                (id,entity_schema_id,n_id,name,data_type,required,is_array,enabled,sort,min_value,was_published,
                 is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version)
            VALUES ('{Guid.NewGuid()}','{schema.Id}','BAD','Bad','Decimal',0,0,1,0,'junk',0,0,0,0,'Bad',
                    '2026-09-05T00:00:00Z','2026-09-05T00:00:00Z',0,'{Guid.NewGuid()}')
            """));
        Assert.Contains("CHECK", junk.ToString(), StringComparison.OrdinalIgnoreCase);

        var tooPrecise = await Assert.ThrowsAnyAsync<Exception>(() => _context.SqlSugar.Ado.ExecuteCommandAsync($"""
            INSERT INTO reference_data_metadata_attribute_definition
                (id,entity_schema_id,n_id,name,data_type,required,is_array,enabled,sort,min_value,was_published,
                 is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version)
            VALUES ('{Guid.NewGuid()}','{schema.Id}','BAD','Bad','Decimal',0,0,1,0,'1.1234567890123',0,0,0,0,'Bad',
                    '2026-09-05T00:00:00Z','2026-09-05T00:00:00Z',0,'{Guid.NewGuid()}')
            """));
        Assert.Contains("CHECK", tooPrecise.ToString(), StringComparison.OrdinalIgnoreCase);

        var state = await Assert.ThrowsAnyAsync<Exception>(() => _context.SqlSugar.Ado.ExecuteCommandAsync($"""
            UPDATE reference_data_metadata_entity_schema SET status='Published',published_on=NULL WHERE id='{schema.Id}'
            """));
        Assert.Contains("CHECK", state.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_is_006_provider_compatible_and_uses_normal_child_foreign_key()
    {
        Assert.Equal("reference-data-2.7-006", MetadataMigration.Version);
        var postgres = MetadataMigration.Sql(true);
        Assert.Contains("reference_data.metadata_entity_schema", postgres, StringComparison.Ordinal);
        Assert.Contains("numeric(28,12)", postgres, StringComparison.Ordinal);
        Assert.Contains("REFERENCES reference_data.metadata_entity_schema(id)", postgres, StringComparison.Ordinal);
        Assert.Contains("metadata_draft_uq", postgres, StringComparison.Ordinal);
        Assert.Contains("metadata_current_uq", postgres, StringComparison.Ordinal);
        Assert.DoesNotContain("entity_schema_id,is_deleted", postgres, StringComparison.Ordinal);
    }

    private Task<MetadataSchemaDetailDto> CreateAsync(ReferenceDataActor actor, string scope, string nId,
        IReadOnlyList<MetadataAttributeRequest> attributes) => _service.CreateAsync(actor,
            new(scope, nId, nId, null, attributes), Ct);
    private Task<MetadataSchemaDetailDto> PublishAsync(ReferenceDataActor actor, MetadataSchemaDetailDto schema) =>
        _service.PublishAsync(actor, schema.Id, Version(schema), Ct);
    private static MetadataAttributeRequest Attribute(string nId, string type) => new(
        nId, nId, type, false, false, true, 0, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null);
    private static PublishOrDisableRequest Version(MetadataSchemaDetailDto schema) =>
        new(schema.OptimisticVersion, schema.ConcurrencyVersion, "Metadata test state change");
    private static PublishOrDisableRequest Version(UnitDimensionDetailDto dimension) =>
        new(dimension.OptimisticVersion, dimension.ConcurrencyVersion, "Metadata test state change");
    private static async Task EnsureMetadataMigrationAsync(SqlSugarDbContext context)
    {
        await context.SqlSugar.Ado.ExecuteCommandAsync("PRAGMA foreign_keys=ON");
        if (await context.SqlSugar.Ado.GetIntAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='reference_data_metadata_entity_schema'") == 0)
            await context.SqlSugar.Ado.ExecuteCommandAsync(MetadataMigration.Sql(false));
    }

    public void Dispose()
    {
        _context.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }
    public Task DisposeAsync() { Dispose(); return Task.CompletedTask; }
}
