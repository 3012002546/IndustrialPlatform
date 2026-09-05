using System.Text.Json;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Application.DynamicProperty;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.DynamicProperty;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;
using IndustrialPlatform.ReferenceData.Infrastructure.DynamicProperty;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests.Infrastructure;

public sealed class DynamicConfigurationPersistenceTests : IAsyncLifetime, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pf03-dynamic-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext _context;
    private readonly DynamicConfigurationService _service;
    private static readonly ReferenceDataActor Tenant = new("TENANT-A", "USER-A", false, "dynamic-persistence-test");
    private static readonly ReferenceDataActor Platform = Tenant with { CanManagePlatform = true };
    private static readonly JsonSerializerOptions SerializationOptions = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, JsonElement> EmptyValues = new(StringComparer.Ordinal);
    private static CancellationToken Ct => CancellationToken.None;

    public DynamicConfigurationPersistenceTests()
    {
        _context = OpenContext();
        _service = Service(_context);
    }

    public async Task InitializeAsync()
    {
        var initializer = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(_context));
        var context = new ServiceInitializationContext("Test", Tenant.TenantNId, "OP-DYNAMIC", "referencedata", "referencedata",
            new ResolvedDatabaseTarget("Test", DatabaseTopologyMode.PerService, "referencedata", DatabaseProvider.Sqlite,
                "referencedata_db", _path, false), ReferenceDataServiceInitializer.CurrentVersion,
            ServiceInitializationPolicy.Standard, Tenant.TraceId);
        var inspection = await initializer.InspectAsync(context, Ct);
        Assert.True((await initializer.ApplyAsync(context, await initializer.PlanAsync(context, inspection, Ct), Ct)).Ready);
    }

    [Fact]
    public async Task Nine_types_use_their_real_columns_and_round_trip_exact_decimal_integer_and_utc_values()
    {
        var dictionaries = new DictionaryService(new DictionaryRepository(_context));
        var dictionary = await dictionaries.CreateAsync(Tenant,
            new("Tenant", null, "Choices", "Choices", null, [new("READY", "Ready", null, 0, true)]), Ct);
        await dictionaries.PublishAsync(Tenant, dictionary.Id, new(dictionary.OptimisticVersion, dictionary.ConcurrencyVersion, "Test choices"), Ct);
        var definition = await CreateAsync("TypedValues", [
            Field("TextValue", "String"), Field("IntegerValue", "Integer"), Field("DecimalValue", "Decimal") with { Scale = 10 },
            Field("BooleanValue", "Boolean"), Field("DateValue", "Date"), Field("DateTimeValue", "DateTime"),
            Field("EnumValue", "Enum") with { DictionaryNId = "Choices" }, Field("JsonValue", "Json"),
            Field("ReferenceValue", "Reference") with { ReferenceTarget = "Material" },
        ]);
        const string precise = "999999999999999999.9999999999";
        var values = new Dictionary<string, JsonElement>
        {
            ["TextValue"] = Json("\"\""), ["IntegerValue"] = Json("9223372036854775807"),
            ["DecimalValue"] = Json(precise), ["BooleanValue"] = Json("false"), ["DateValue"] = Json("\"2026-09-05\""),
            ["DateTimeValue"] = Json("\"2026-09-05T08:09:10.1234567+08:00\""), ["EnumValue"] = Json("\"ready\""),
            ["JsonValue"] = Json("{\"token\":\"business-field\",\"a\":1.00}"), ["ReferenceValue"] = Json("\"MAT-001\""),
        };
        var added = await AddAsync(definition, "FIRST", values);
        definition = await _service.PublishAsync(Tenant, definition.Id, Version(added.Definition), Ct);

        var columns = await ReadValueColumnsAsync(definition.Id);
        Assert.Equal(9, columns.Count);
        AssertColumn(columns["TEXTVALUE"], 0, string.Empty);
        AssertColumn(columns["INTEGERVALUE"], 1, long.MaxValue);
        AssertColumn(columns["DECIMALVALUE"], 2, precise);
        AssertColumn(columns["BOOLEANVALUE"], 3, 0L);
        AssertColumn(columns["DATEVALUE"], 4, "2026-09-05");
        AssertColumn(columns["DATETIMEVALUE"], 5);
        AssertColumn(columns["ENUMVALUE"], 0, "READY");
        AssertColumn(columns["JSONVALUE"], 6, "{\"a\":1,\"token\":\"business-field\"}");
        AssertColumn(columns["REFERENCEVALUE"], 7, "MAT-001");

        using var freshContext = OpenContext();
        var fresh = Service(freshContext);
        var row = await fresh.GetRecordAsync(Tenant, definition.NId, "first", definition.Revision, "Tenant", Tenant.TenantNId, null, Ct);
        Assert.Equal(precise, row.ValuesJson["DECIMALVALUE"]);
        Assert.Equal(999999999999999999.9999999999m, row.Values["DECIMALVALUE"].GetDecimal());
        Assert.Equal(long.MaxValue, row.Values["INTEGERVALUE"].GetInt64());
        Assert.False(row.Values["BOOLEANVALUE"].GetBoolean());
        Assert.Equal(string.Empty, row.Values["TEXTVALUE"].GetString());
        Assert.Equal("READY", row.Values["ENUMVALUE"].GetString());
        var expectedUtc = new DateTimeOffset(2026, 9, 5, 0, 9, 10, TimeSpan.Zero).AddTicks(1234560);
        Assert.Equal(expectedUtc, row.Values["DATETIMEVALUE"].GetDateTimeOffset());
        Assert.Equal(TimeSpan.Zero, row.Values["DATETIMEVALUE"].GetDateTimeOffset().Offset);
        var reloaded = await fresh.GetAsync(Tenant, definition.Id, Ct);
        Assert.Equal(definition.LastUpdatedOn, reloaded.LastUpdatedOn);
        Assert.Equal(definition.PublishedOn, reloaded.PublishedOn);
        Assert.Equal(TimeSpan.Zero, reloaded.LastUpdatedOn.Offset);
        Assert.Equal(TimeSpan.Zero, reloaded.PublishedOn!.Value.Offset);
    }

    [Fact]
    public async Task Defaults_are_copied_only_on_record_creation_and_clearing_does_not_reapply_them()
    {
        var definition = await CreateAsync("Defaults", [
            Field("Score") with { DefaultValue = Json("0") }, Field("Review", "Boolean") with { DefaultValue = Json("false") },
        ]);
        var first = await AddAsync(definition, "FIRST", EmptyValues);
        Assert.Equal("0", first.Record.ValuesJson["SCORE"]);
        Assert.Equal("false", first.Record.ValuesJson["REVIEW"]);
        definition = await _service.UpdateAsync(Tenant, definition.Id, Edit(first.Definition) with
        {
            Fields = [Field("Score") with { DefaultValue = Json("9") }, Field("Review", "Boolean") with { DefaultValue = Json("true") }],
        }, Ct);
        var second = await AddAsync(definition, "SECOND", EmptyValues);
        Assert.Equal("9", second.Record.ValuesJson["SCORE"]);
        Assert.Equal("true", second.Record.ValuesJson["REVIEW"]);
        var firstReloaded = Assert.Single((await _service.SearchRecordsAsync(Tenant, definition.Id, new(NId: "FIRST"), Ct)).Items);
        Assert.Equal("0", firstReloaded.ValuesJson["SCORE"]);
        Assert.Equal("false", firstReloaded.ValuesJson["REVIEW"]);
        var cleared = await _service.UpdateRecordAsync(Tenant, definition.Id, first.Record.Id,
            RecordRequest(second.Definition, first.Record, EmptyValues), Ct);
        Assert.Empty(cleared.Record.Values);
        definition = await _service.GetAsync(Tenant, definition.Id, Ct);
        Assert.Equal(2L, definition.ValueCount);
        Assert.All(definition.Fields, field => Assert.True(field.HasHadValue));
        definition = await _service.PublishAsync(Tenant, definition.Id, Version(definition), Ct);

        using var freshContext = OpenContext();
        var fresh = Service(freshContext);
        Assert.Empty((await fresh.GetRecordAsync(Tenant, definition.NId, "FIRST", definition.Revision, "Tenant", Tenant.TenantNId, null, Ct)).Values);
        Assert.Equal("9", (await fresh.GetRecordAsync(Tenant, definition.NId, "SECOND", definition.Revision, "Tenant", Tenant.TenantNId, null, Ct)).ValuesJson["SCORE"]);
        var clone = await fresh.CloneAsync(Tenant, definition.Id, Version(definition), Ct);
        Assert.Empty(Assert.Single((await fresh.SearchRecordsAsync(Tenant, clone.Id, new(NId: "FIRST"), Ct)).Items).Values);
        Assert.All((await fresh.GetAsync(Tenant, clone.Id, Ct)).Fields, field => Assert.True(field.HasHadValue));
    }

    [Theory]
    [InlineData("1000000000000000000")]
    [InlineData("0.00000000001")]
    [InlineData("0.123456789012345678901234567891")]
    public async Task Unrepresentable_decimals_are_rejected_without_rounding_or_persisting_partial_changes(string invalid)
    {
        var definition = await CreateAsync("DecimalBoundary");
        var added = await AddAsync(definition, "FIRST", Values("Score", "1.25"));

        var error = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.UpdateRecordAsync(Tenant, definition.Id,
            added.Record.Id, RecordRequest(added.Definition, added.Record, Values("Score", invalid)), Ct));

        Assert.Equal("REF-DYNAMIC-CONFIG-FIELD-INVALID", error.ErrorCode);
        Assert.Equal(422, error.Status);
        AssertUnchanged(added.Definition, await _service.GetAsync(Tenant, definition.Id, Ct));
        AssertColumn((await ReadValueColumnsAsync(definition.Id))["SCORE"], 2, "1.25");
    }

    [Fact]
    public async Task Field_and_record_value_writes_share_both_root_concurrency_tokens()
    {
        var initial = await CreateAsync("Versions");
        var changed = await _service.UpdateAsync(Tenant, initial.Id, Edit(initial) with
        {
            Fields = [Field("Score") with { Name = "Changed score" }],
        }, Ct);
        Assert.Equal(initial.OptimisticVersion + 1, changed.OptimisticVersion);
        Assert.NotEqual(initial.ConcurrencyVersion, changed.ConcurrencyVersion);
        Assert.Equal(initial.Revision, changed.Revision);
        await AssertConflictAsync(() => _service.UpdateAsync(Tenant, initial.Id,
            Edit(changed) with { Name = "Stale version", ExpectedOptimisticVersion = initial.OptimisticVersion }, Ct));
        await AssertConflictAsync(() => _service.UpdateAsync(Tenant, initial.Id,
            Edit(changed) with { Name = "Stale token", ExpectedConcurrencyVersion = initial.ConcurrencyVersion }, Ct));
        await AssertConflictAsync(() => _service.AddRecordAsync(Tenant, initial.Id,
            new("STALE", null, null, 0, true, Values("Score", "1"), initial.OptimisticVersion, initial.ConcurrencyVersion), Ct));
        var added = await AddAsync(changed, "FIRST", Values("Score", "1"));
        var request = RecordRequest(added.Definition, added.Record, Values("Score", "2"));
        var updated = await _service.UpdateRecordAsync(Tenant, initial.Id, added.Record.Id, request, Ct);
        Assert.Equal(added.Definition.OptimisticVersion + 1, updated.OptimisticVersion);
        Assert.NotEqual(added.Definition.ConcurrencyVersion, updated.ConcurrencyVersion);
        var current = await _service.GetAsync(Tenant, initial.Id, Ct);
        var currentRequest = RecordRequest(current, updated.Record, Values("Score", "99"));
        await AssertConflictAsync(() => _service.UpdateRecordAsync(Tenant, initial.Id, added.Record.Id,
            currentRequest with { ExpectedOptimisticVersion = added.Definition.OptimisticVersion }, Ct));
        await AssertConflictAsync(() => _service.UpdateRecordAsync(Tenant, initial.Id, added.Record.Id,
            currentRequest with { ExpectedConcurrencyVersion = added.Definition.ConcurrencyVersion }, Ct));
        await AssertConflictAsync(() => _service.UpdateAsync(Tenant, initial.Id, Edit(added.Definition) with { Name = "Stale field editor" }, Ct));
        AssertUnchanged(current, await _service.GetAsync(Tenant, initial.Id, Ct));
        var stored = Assert.Single((await _service.SearchRecordsAsync(Tenant, initial.Id, new(), Ct)).Items);
        Assert.Equal("2", stored.ValuesJson["SCORE"]);
        Assert.Equal(1L, current.ValueCount);
    }

    [Fact]
    public async Task Repository_compare_and_swap_rejects_the_second_loaded_writer_and_preserves_the_winner()
    {
        var detail = await CreateAsync("RepositoryCas");
        var repository = new DynamicConfigurationRepository(_context);
        var winner = await repository.GetAsync(Tenant.TenantNId, detail.Id, true, Ct);
        var loser = await repository.GetAsync(Tenant.TenantNId, detail.Id, true, Ct);
        Assert.NotNull(winner);
        Assert.NotNull(loser);
        winner.Update("Winner", null, [new("Score", winner.Fields[0].Settings with { Name = "Winner score" })]);
        loser.Update("Loser", null, [new("Score", loser.Fields[0].Settings with { Name = "Loser score" })]);
        await repository.SaveAsync([new(winner, detail.OptimisticVersion, detail.ConcurrencyVersion, SaveFields: true)], Ct);

        await AssertConflictAsync(() => repository.SaveAsync([new(loser, detail.OptimisticVersion, detail.ConcurrencyVersion, SaveFields: true)], Ct));

        var stored = await _service.GetAsync(Tenant, detail.Id, Ct);
        Assert.Equal("Winner", stored.Name);
        Assert.Equal("Winner score", Assert.Single(stored.Fields).Name);
        Assert.Equal(winner.OptimisticVersion, stored.OptimisticVersion);
        Assert.Equal(winner.ConcurrencyVersion, stored.ConcurrencyVersion);
    }

    [Fact]
    public async Task Clone_rebuilds_every_id_and_revision_link_and_old_published_values_remain_stable()
    {
        var definition = await CreateAsync("Cloning", [Field("Score"), Field("Other")]);
        var first = await AddAsync(definition, "FIRST", new Dictionary<string, JsonElement> { ["Score"] = Json("1"), ["Other"] = Json("2") });
        var second = await AddAsync(first.Definition, "SECOND", new Dictionary<string, JsonElement> { ["Score"] = Json("3"), ["Other"] = Json("4") });
        definition = await _service.PublishAsync(Tenant, definition.Id, Version(second.Definition), Ct);
        var clone = await _service.CloneAsync(Tenant, definition.Id, Version(definition), Ct);
        Assert.Equal(definition.Revision + 1, clone.Revision);
        Assert.Equal("Draft", clone.Status);
        var repository = new DynamicConfigurationRepository(_context);
        var old = await repository.GetAsync(Tenant.TenantNId, definition.Id, true, Ct);
        var next = await repository.GetAsync(Tenant.TenantNId, clone.Id, true, Ct);
        Assert.NotNull(old);
        Assert.NotNull(next);
        Assert.NotEqual(old.Id, next.Id);
        Assert.NotEqual(old.ConcurrencyVersion, next.ConcurrencyVersion);
        Assert.Equal(old.Fields.Count, next.Fields.Count);
        Assert.Equal(old.Records.Count, next.Records.Count);
        foreach (var field in next.Fields)
        {
            var source = old.Fields.Single(item => item.NId == field.NId);
            Assert.NotEqual(source.Id, field.Id);
            Assert.NotEqual(source.ConcurrencyVersion, field.ConcurrencyVersion);
            Assert.Equal(next.Id, field.DynamicConfigDefinitionId);
            Assert.Equal(source.Settings, field.Settings);
            Assert.True(field.WasPublished);
            Assert.True(field.HasHadValue);
        }
        foreach (var record in next.Records)
        {
            var source = old.Records.Single(item => item.NId == record.NId);
            Assert.NotEqual(source.Id, record.Id);
            Assert.NotEqual(source.ConcurrencyVersion, record.ConcurrencyVersion);
            Assert.Equal(next.Id, record.DynamicConfigDefinitionId);
            Assert.Equal(source.Values.Count, record.Values.Count);
            foreach (var value in record.Values)
            {
                var sourceValue = source.Values.Single(item => item.FieldNId == value.FieldNId);
                Assert.NotEqual(sourceValue.Id, value.Id);
                Assert.NotEqual(sourceValue.ConcurrencyVersion, value.ConcurrencyVersion);
                Assert.Equal(next.Id, value.DynamicConfigDefinitionId);
                Assert.Equal(record.Id, value.DynamicConfigRecordId);
                Assert.Equal(next.Fields.Single(field => field.NId == value.FieldNId).Id, value.DynamicConfigFieldDefinitionId);
                Assert.Equal(sourceValue.Value, value.Value);
            }
        }
        var clonedRow = Assert.Single((await _service.SearchRecordsAsync(Tenant, clone.Id, new(NId: "FIRST"), Ct)).Items);
        var changed = await _service.UpdateRecordAsync(Tenant, clone.Id, clonedRow.Id,
            RecordRequest(clone, clonedRow, new Dictionary<string, JsonElement> { ["Score"] = Json("99"), ["Other"] = Json("2") }), Ct);
        clone = await _service.PublishAsync(Tenant, clone.Id, new(changed.OptimisticVersion, changed.ConcurrencyVersion, "Publish clone"), Ct);
        Assert.Equal("Superseded", (await _service.GetAsync(Tenant, definition.Id, Ct)).Status);
        Assert.Equal("1", (await RuntimeRecordAsync(definition, "FIRST")).ValuesJson["SCORE"]);
        Assert.Equal("99", (await RuntimeRecordAsync(clone, "FIRST")).ValuesJson["SCORE"]);
        await AssertForeignKeysValidAsync();
    }

    [Theory]
    [InlineData("field_id", "field")]
    [InlineData("record_id", "record")]
    public async Task Composite_foreign_keys_reject_existing_children_from_another_revision(string column, string table)
    {
        var definition = await CreateAsync("RevisionFk");
        var added = await AddAsync(definition, "FIRST", Values("Score", "1"));
        definition = await _service.PublishAsync(Tenant, definition.Id, Version(added.Definition), Ct);
        var clone = await _service.CloneAsync(Tenant, definition.Id, Version(definition), Ct);
        await using var connection = OpenSqlite();
        await using var command = connection.CreateCommand();
        // Both substituted identifiers come exclusively from the two InlineData cases above.
        command.CommandText = $"""
            UPDATE reference_data_dynamic_property_value
            SET {column}=(SELECT id FROM reference_data_dynamic_property_{table} WHERE lower(definition_id)=lower($old))
            WHERE lower(definition_id)=lower($clone)
            """;
        command.Parameters.AddWithValue("$old", definition.Id.ToString());
        command.Parameters.AddWithValue("$clone", clone.Id.ToString());

        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync(Ct));

        Assert.Equal(787, error.SqliteExtendedErrorCode);
        await AssertForeignKeysValidAsync();
        var stored = await new DynamicConfigurationRepository(_context).GetAsync(Tenant.TenantNId, clone.Id, true, Ct);
        Assert.NotNull(stored);
        var row = Assert.Single(stored.Records);
        var value = Assert.Single(row.Values);
        Assert.Equal(stored.Id, value.DynamicConfigDefinitionId);
        Assert.Equal(row.Id, value.DynamicConfigRecordId);
        Assert.Equal(Assert.Single(stored.Fields).Id, value.DynamicConfigFieldDefinitionId);
    }

    [Theory]
    [InlineData("string_value='extra'")]
    [InlineData("decimal_value=NULL")]
    [InlineData("value_type='String'")]
    public async Task Value_check_rejects_multiple_empty_or_mismatched_typed_columns(string invalidAssignment)
    {
        var definition = await CreateAsync("TypeCheck");
        await AddAsync(definition, "FIRST", Values("Score", "1.25"));
        await using var connection = OpenSqlite();
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE reference_data_dynamic_property_value SET {invalidAssignment} WHERE lower(definition_id)=lower($id)";
        command.Parameters.AddWithValue("$id", definition.Id.ToString());

        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync(Ct));

        Assert.Equal(275, error.SqliteExtendedErrorCode);
        AssertColumn((await ReadValueColumnsAsync(definition.Id))["SCORE"], 2, "1.25");
    }

    [Fact]
    public async Task Publication_field_write_failure_rolls_back_both_revisions_and_their_versions()
    {
        var definition = await CreateAsync("AtomicPublish");
        var added = await AddAsync(definition, "FIRST", Values("Score", "1"));
        definition = await _service.PublishAsync(Tenant, definition.Id, Version(added.Definition), Ct);
        var clone = await _service.CloneAsync(Tenant, definition.Id, Version(definition), Ct);
        clone = await _service.UpdateAsync(Tenant, clone.Id, Edit(clone) with { Fields = [Field("Score"), Field("Added")] }, Ct);
        var beforeOld = await _service.GetAsync(Tenant, definition.Id, Ct);
        var beforeNew = await _service.GetAsync(Tenant, clone.Id, Ct);
        var beforeRows = await _service.SearchRecordsAsync(Tenant, clone.Id, new(), Ct);
        // This trigger is created only in the fixture's unique temporary SQLite database.
        await ExecuteSqlAsync("""
            CREATE TRIGGER pf03_dynamic_publish_failure BEFORE UPDATE ON reference_data_dynamic_property_field
            WHEN NEW.was_published=1
            BEGIN SELECT RAISE(ABORT, 'pf03-dynamic-publication-write-failure'); END;
            """);
        try
        {
            var error = await Assert.ThrowsAsync<ReferenceDataException>(() => _service.PublishAsync(Tenant, clone.Id, Version(clone), Ct));
            Assert.Equal(503, error.Status);
        }
        finally
        {
            await ExecuteSqlAsync("DROP TRIGGER pf03_dynamic_publish_failure");
        }
        using var freshContext = OpenContext();
        var fresh = Service(freshContext);
        AssertUnchanged(beforeOld, await fresh.GetAsync(Tenant, definition.Id, Ct));
        AssertUnchanged(beforeNew, await fresh.GetAsync(Tenant, clone.Id, Ct));
        var afterRows = await fresh.SearchRecordsAsync(Tenant, clone.Id, new(), Ct);
        Assert.Equal(beforeRows.Total, afterRows.Total);
        Assert.Equal(JsonSerializer.Serialize(beforeRows.Items), JsonSerializer.Serialize(afterRows.Items));
        Assert.False((await fresh.GetAsync(Tenant, clone.Id, Ct)).Fields.Single(field => field.NId == "ADDED").WasPublished);
        Assert.Equal(definition.Id, (await fresh.GetSchemaAsync(Tenant, definition.NId, null, Ct)).DefinitionId);
        var published = await fresh.PublishAsync(Tenant, clone.Id, Version(beforeNew), Ct);
        Assert.Equal("Published", published.Status);
        Assert.Equal("Superseded", (await fresh.GetAsync(Tenant, definition.Id, Ct)).Status);
    }

    [Fact]
    public async Task Tenant_whole_set_override_and_explicit_sources_disambiguate_equal_revisions_and_keep_disabled_history()
    {
        var platform = await CreateAsync("Shared", [Field("Score"), Field("PlatformOnly", "String")], Platform, "Platform");
        var firstPlatform = await AddAsync(platform, "SHARED", new Dictionary<string, JsonElement>
        {
            ["Score"] = Json("11"), ["PlatformOnly"] = Json("\"platform\""),
        }, actor: Platform);
        var platformOnly = await AddAsync(firstPlatform.Definition, "PLATFORM_ONLY", Values("Score", "12"), actor: Platform);
        platform = await _service.PublishAsync(Platform, platform.Id, Version(platformOnly.Definition), Ct);
        var tenant = await CreateAsync("Shared", [Field("Score"), Field("TenantOnly", "Boolean")]);
        var tenantRow = await AddAsync(tenant, "SHARED", new Dictionary<string, JsonElement>
        {
            ["Score"] = Json("22"), ["TenantOnly"] = Json("false"),
        });
        tenant = await _service.PublishAsync(Tenant, tenant.Id, Version(tenantRow.Definition), Ct);
        Assert.Equal(platform.Revision, tenant.Revision);
        var schema = await _service.GetSchemaAsync(Tenant, "shared", null, Ct);
        Assert.Equal(tenant.Id, schema.DefinitionId);
        Assert.Equal("Tenant", schema.SourceScope);
        Assert.Equal(Tenant.TenantNId, schema.SourceTenantNId);
        Assert.DoesNotContain(schema.Fields, field => field.NId == "PLATFORMONLY");
        var tenantRecords = await _service.GetRecordsAsync(Tenant, "shared", schema.Revision, schema.SourceScope, schema.SourceTenantNId, null, new(), Ct);
        Assert.Equal("SHARED", Assert.Single(tenantRecords.Items).NId);
        Assert.Equal("22", (await RuntimeRecordAsync(tenant, "SHARED")).ValuesJson["SCORE"]);
        Assert.Equal("11", (await RuntimeRecordAsync(platform, "SHARED")).ValuesJson["SCORE"]);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReferenceDataException>(() => RuntimeRecordAsync(tenant, "PLATFORM_ONLY"))).Status);
        tenant = await _service.DisableAsync(Tenant, tenant.Id, Version(tenant), Ct);
        Assert.Equal(platform.Id, (await _service.GetSchemaAsync(Tenant, "Shared", null, Ct)).DefinitionId);
        Assert.Equal("22", (await RuntimeRecordAsync(tenant, "SHARED")).ValuesJson["SCORE"]);
        platform = await _service.DisableAsync(Platform, platform.Id, Version(platform), Ct);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReferenceDataException>(() => _service.GetSchemaAsync(Tenant, "Shared", null, Ct))).Status);
        Assert.Equal("11", (await RuntimeRecordAsync(platform, "SHARED")).ValuesJson["SCORE"]);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReferenceDataException>(() => _service.GetRecordAsync(
            Tenant with { TenantNId = "TENANT-B" }, "Shared", "SHARED", tenant.Revision, "Tenant", Tenant.TenantNId, null, Ct))).Status);
        var neverPublished = await CreateAsync("NeverPublished");
        neverPublished = await _service.DisableAsync(Tenant, neverPublished.Id, Version(neverPublished), Ct);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReferenceDataException>(() => RuntimeRecordAsync(neverPublished, "FIRST"))).Status);
    }

    [Fact]
    public async Task Details_omit_records_and_admin_and_runtime_queries_page_and_filter_on_the_server()
    {
        var definition = await CreateAsync("Listing");
        definition = (await AddAsync(definition, "A1", Values("Score", "1"), "Alpha one", "RAW", 10)).Definition;
        definition = (await AddAsync(definition, "A2", Values("Score", "2"), "Alpha two", "RAW", 10)).Definition;
        definition = (await AddAsync(definition, "B1", Values("Score", "3"), "Beta one", "PACK", 0)).Definition;
        definition = (await AddAsync(definition, "HIDDEN", Values("Score", "4"), "Alpha hidden", "RAW", 0, false)).Definition;
        definition = (await AddAsync(definition, "A3", Values("Score", "5"), "Alpha three", "RAW", 20)).Definition;
        definition = (await AddAsync(definition, "OTHER", Values("Score", "6"), "Other", "RAW", 30)).Definition;
        var detail = await _service.GetAsync(Tenant, definition.Id, Ct);
        Assert.Equal(6, detail.RecordCount);
        Assert.Equal(6L, detail.ValueCount);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(detail, SerializationOptions));
        Assert.False(json.RootElement.TryGetProperty("records", out _));
        Assert.Single(json.RootElement.GetProperty("fields").EnumerateArray());
        var first = await _service.SearchRecordsAsync(Tenant, definition.Id, new(1, 2, "alpha", Category: "RAW"), Ct);
        var second = await _service.SearchRecordsAsync(Tenant, definition.Id, new(2, 2, "alpha", Category: "RAW"), Ct);
        Assert.Equal(4L, first.Total);
        Assert.Equal(4L, second.Total);
        Assert.Collection(first.Items, row => Assert.Equal("HIDDEN", row.NId), row => Assert.Equal("A1", row.NId));
        Assert.Collection(second.Items, row => Assert.Equal("A2", row.NId), row => Assert.Equal("A3", row.NId));
        var exact = await _service.SearchRecordsAsync(Tenant, definition.Id, new(NId: "a2", Category: "RAW"), Ct);
        Assert.Equal(1L, exact.Total);
        Assert.Equal("A2", Assert.Single(exact.Items).NId);
        var summaries = await _service.SearchAsync(Tenant, new(1, 1, "listing", ScopeType: "Tenant"), Ct);
        Assert.Equal(1L, summaries.Total);
        Assert.Equal(6, Assert.Single(summaries.Items).RecordCount);
        definition = await _service.PublishAsync(Tenant, definition.Id, Version(definition), Ct);
        var runtimeFirst = await _service.GetRecordsAsync(Tenant, definition.NId, definition.Revision, "Tenant", Tenant.TenantNId, null,
            new(1, 2, "alpha", Category: "RAW"), Ct);
        var runtimeSecond = await _service.GetRecordsAsync(Tenant, definition.NId, definition.Revision, "Tenant", Tenant.TenantNId, null,
            new(2, 2, "alpha", Category: "RAW"), Ct);
        Assert.Equal(3L, runtimeFirst.Total);
        Assert.Equal(3L, runtimeSecond.Total);
        Assert.Collection(runtimeFirst.Items, row => Assert.Equal("A1", row.NId), row => Assert.Equal("A2", row.NId));
        Assert.Equal("A3", Assert.Single(runtimeSecond.Items).NId);
        Assert.Equal(400, (await Assert.ThrowsAsync<ReferenceDataException>(() => _service.SearchRecordsAsync(Tenant, definition.Id, new(PageSize: 101), Ct))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ReferenceDataException>(() => _service.GetRecordsAsync(Tenant, definition.NId, definition.Revision,
            "Tenant", Tenant.TenantNId, null, new(PageIndex: 0), Ct))).Status);
    }

    private SqlSugarDbContext OpenContext() => new(Options.Create(new SqlSugarOptions
    {
        DbType = DbType.Sqlite, ConnectionString = $"Data Source={_path};Pooling=False;Foreign Keys=True",
    }));
    private SqliteConnection OpenSqlite()
    {
        var connection = new SqliteConnection($"Data Source={_path};Pooling=False;Foreign Keys=True");
        connection.Open();
        return connection;
    }
    private static DynamicConfigurationService Service(SqlSugarDbContext context) => new(new DynamicConfigurationRepository(context),
        new DictionaryService(new DictionaryRepository(context)));
    private Task<DynamicConfigurationDetailDto> CreateAsync(string nId, IReadOnlyList<DynamicFieldRequest>? fields = null,
        ReferenceDataActor? actor = null, string scope = "Tenant") =>
        _service.CreateAsync(actor ?? Tenant, new(scope, null, nId, nId, null, fields ?? [Field("Score")]), Ct);
    private async Task<(DynamicConfigurationDetailDto Definition, DynamicRecordDto Record)> AddAsync(DynamicConfigurationDetailDto definition,
        string nId, IReadOnlyDictionary<string, JsonElement> values, string? name = null, string? category = null, int sort = 0,
        bool enabled = true, ReferenceDataActor? actor = null)
    {
        var user = actor ?? Tenant;
        var mutation = await _service.AddRecordAsync(user, definition.Id,
            new(nId, name, category, sort, enabled, values, definition.OptimisticVersion, definition.ConcurrencyVersion), Ct);
        var current = await _service.GetAsync(user, definition.Id, Ct);
        Assert.Equal(mutation.OptimisticVersion, current.OptimisticVersion);
        Assert.Equal(mutation.ConcurrencyVersion, current.ConcurrencyVersion);
        return (current, mutation.Record);
    }
    private Task<DynamicRecordDto> RuntimeRecordAsync(DynamicConfigurationDetailDto definition, string nId) =>
        _service.GetRecordAsync(Tenant, definition.NId, nId, definition.Revision, definition.ScopeType, definition.TenantNId, null, Ct);
    private static DynamicFieldRequest Field(string nId, string type = "Decimal") => new(nId, nId, type, false, true, 0, null);
    private static DynamicFieldRequest EditField(DynamicFieldDto field) => new(field.NId, field.Name, field.DataType, field.Required, field.Enabled,
        field.Sort, field.DefaultValue, field.MinLength, field.MaxLength, field.MinValue, field.MaxValue, field.Scale, field.Pattern,
        field.DictionaryNId, field.ReferenceTarget, field.Description);
    private static UpdateDynamicConfigurationRequest Edit(DynamicConfigurationDetailDto definition) =>
        new(definition.Name, definition.Description, definition.Fields.Select(EditField).ToArray(), definition.OptimisticVersion, definition.ConcurrencyVersion);
    private static DynamicRecordRequest RecordRequest(DynamicConfigurationDetailDto definition, DynamicRecordDto row,
        IReadOnlyDictionary<string, JsonElement> values) => new(row.NId, row.Name, row.Category, row.Sort, row.Enabled, values,
            definition.OptimisticVersion, definition.ConcurrencyVersion);
    private static PublishOrDisableRequest Version(DynamicConfigurationDetailDto definition) =>
        new(definition.OptimisticVersion, definition.ConcurrencyVersion, "Dynamic persistence test");
    private static Dictionary<string, JsonElement> Values(string nId, string json) => new(StringComparer.Ordinal) { [nId] = Json(json) };
    private static JsonElement Json(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static void AssertUnchanged(DynamicConfigurationDetailDto before, DynamicConfigurationDetailDto after) =>
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
    private static async Task AssertConflictAsync(Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<ReferenceDataException>(action);
        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
        Assert.Equal(409, error.Status);
    }

    private async Task<Dictionary<string, object?[]>> ReadValueColumnsAsync(Guid definitionId)
    {
        await using var connection = OpenSqlite();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT f.n_id, v.string_value, v.integer_value, v.decimal_value, v.boolean_value,
                v.date_value, v.date_time_value, v.json_value, v.reference_value
            FROM reference_data_dynamic_property_value v JOIN reference_data_dynamic_property_field f ON f.id=v.field_id
            WHERE lower(v.definition_id)=lower($id)
            """;
        command.Parameters.AddWithValue("$id", definitionId.ToString());
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var result = new Dictionary<string, object?[]>(StringComparer.Ordinal);
        while (await reader.ReadAsync(Ct))
            result.Add(reader.GetString(0), Enumerable.Range(1, 8).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
        return result;
    }
    private static void AssertColumn(object?[] values, int selected, object? expected = null)
    {
        Assert.Equal(8, values.Length);
        Assert.NotNull(values[selected]);
        if (expected is not null) Assert.Equal(expected, values[selected]);
        for (var index = 0; index < values.Length; index++)
            if (index != selected) Assert.Null(values[index]);
    }
    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = OpenSqlite();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
    }
    private async Task AssertForeignKeysValidAsync()
    {
        await using var connection = OpenSqlite();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.False(await reader.ReadAsync(Ct));
    }

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
