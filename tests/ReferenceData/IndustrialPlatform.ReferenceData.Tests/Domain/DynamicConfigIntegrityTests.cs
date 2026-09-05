using System.Text.Json;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.DynamicProperty;
using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.ReferenceData.Tests.Domain;

public sealed class DynamicConfigIntegrityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_a_protected_field_preserves_the_entire_aggregate(bool locked)
    {
        var definition = Create();
        var first = definition.Fields[0];
        var removed = definition.Fields[1];
        Protect(removed, locked);
        var before = Snapshot(definition);

        Assert.Throws<BusinessException>(() => definition.Update("Changed", "Changed", [
            new(first.NId, first.Settings with { Name = "Changed first" }),
        ]));

        Assert.Equal(before, Snapshot(definition));
        Assert.Collection(definition.Fields, field => Assert.Same(first, field), field => Assert.Same(removed, field));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_a_protected_value_preserves_earlier_values_and_root_versions(bool locked)
    {
        var definition = Create();
        var row = AddValues(definition);
        var first = row.Values[0];
        var removed = row.Values[1];
        Protect(removed, locked);
        var before = Snapshot(definition);

        Assert.Throws<BusinessException>(() => definition.UpdateRecord(row.Id, row.NId,
            row.Settings with { Name = "Changed row" }, [new("Alpha", Scalar("99"))]));

        Assert.Equal(before, Snapshot(definition));
        Assert.Same(row, definition.Record(row.Id));
        Assert.Collection(row.Values, value => Assert.Same(first, value), value => Assert.Same(removed, value));
    }

    [Theory]
    [InlineData("published")]
    [InlineData("clone")]
    [InlineData("restored")]
    public void Value_collections_cannot_be_mutated_outside_the_aggregate(string source)
    {
        var original = Create();
        var originalRow = AddValues(original);
        original.Publish("ADMIN");
        var definition = source switch
        {
            "published" => original,
            "clone" => original.Clone(2),
            "restored" => Restore(original, records: [RestoreRecord(originalRow)]),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
        var row = Assert.Single(definition.Records);
        var values = Assert.IsAssignableFrom<IList<DynamicConfigFieldValue>>(row.Values);
        var before = Snapshot(definition);

        Assert.Throws<NotSupportedException>(() => values[0] = values[1]);
        Assert.Throws<NotSupportedException>(() => values.Add(values[0]));
        Assert.Throws<NotSupportedException>(values.Clear);

        Assert.Equal(before, Snapshot(definition));
        Assert.Collection(row.Values,
            value => Assert.Equal("1", value.Value.CanonicalValue),
            value => Assert.Equal("2", value.Value.CanonicalValue));
    }

    [Fact]
    public void Restoring_a_record_copies_the_supplied_value_collection()
    {
        var definition = Create();
        var row = AddValues(definition);
        var supplied = row.Values.ToList();
        var restored = RestoreRecord(row, values: supplied);
        definition = Restore(definition, records: [restored]);
        var before = Snapshot(definition);

        supplied[0] = supplied[1];
        supplied.Clear();

        Assert.Equal(before, Snapshot(definition));
    }

    [Theory]
    [InlineData(PublicationStatus.Draft, false)]
    [InlineData(PublicationStatus.Published, false)]
    [InlineData(PublicationStatus.Published, true)]
    public void Exhausted_root_versions_reject_disable_and_supersede_without_changing_state(PublicationStatus status, bool supersede)
    {
        var definition = Create();
        AddValues(definition);
        if (status == PublicationStatus.Published) definition.Publish("ADMIN");
        definition = Restore(definition, rootVersion: long.MaxValue);
        var before = Snapshot(definition);

        var error = Assert.Throws<ReferenceDataException>(() =>
        {
            if (supersede) definition.Supersede();
            else definition.Disable();
        });

        AssertConflict(error);
        Assert.Equal(before, Snapshot(definition));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Exhausted_later_field_usage_does_not_mark_earlier_fields_or_add_values(bool updateExistingRecord)
    {
        var definition = Create();
        if (updateExistingRecord) definition.AddRecord("FIRST", new(null, null, 0, true), []);
        var first = definition.Fields[0];
        var exhausted = RestoreField(definition.Fields[1], long.MaxValue);
        definition = Restore(definition, fields: [first, exhausted]);
        var before = Snapshot(definition);

        var error = Assert.Throws<ReferenceDataException>(() =>
        {
            if (updateExistingRecord)
            {
                var row = Assert.Single(definition.Records);
                definition.UpdateRecord(row.Id, row.NId, row.Settings, [new("Alpha", Scalar("1")), new("Zulu", Scalar("2"))]);
            }
            else AddValues(definition);
        });

        AssertConflict(error);
        Assert.Equal(before, Snapshot(definition));
        Assert.False(first.HasHadValue);
        Assert.False(exhausted.HasHadValue);
        Assert.Collection(definition.Fields, field => Assert.Same(first, field), field => Assert.Same(exhausted, field));
    }

    [Fact]
    public void Exhausted_later_field_publication_does_not_publish_any_part_of_the_aggregate()
    {
        var definition = Create();
        definition.AddRecord("FIRST", new(null, null, 0, true), []);
        var first = definition.Fields[0];
        var exhausted = RestoreField(definition.Fields[1], long.MaxValue);
        definition = Restore(definition, fields: [first, exhausted]);
        var before = Snapshot(definition);

        var error = Assert.Throws<ReferenceDataException>(() => definition.Publish("ADMIN"));

        AssertConflict(error);
        Assert.Equal(before, Snapshot(definition));
        Assert.False(first.WasPublished);
        Assert.False(exhausted.WasPublished);
        Assert.Null(definition.PublishedOn);
        Assert.Null(definition.PublishedBy);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("record")]
    [InlineData("value")]
    public void Exhausted_child_versions_reject_replacement_without_mutating_earlier_children(string exhaustedChild)
    {
        var definition = Create();
        var row = AddValues(definition);
        if (exhaustedChild == "field")
            definition = Restore(definition, fields: [definition.Fields[0], RestoreField(definition.Fields[1], long.MaxValue)]);
        else if (exhaustedChild == "record")
            definition = Restore(definition, records: [RestoreRecord(row, version: long.MaxValue)]);
        else
        {
            var value = row.Values[1];
            var exhausted = DynamicConfigFieldValue.Restore(DynamicEntityState.From(value) with { OptimisticVersion = long.MaxValue },
                value.DynamicConfigDefinitionId, value.DynamicConfigRecordId, value.DynamicConfigFieldDefinitionId, value.FieldNId, value.Value);
            definition = Restore(definition, records: [RestoreRecord(row, values: [row.Values[0], exhausted])]);
        }
        var before = Snapshot(definition);

        var error = Assert.Throws<ReferenceDataException>(() =>
        {
            if (exhaustedChild == "field")
                definition.Update("Changed", null, definition.Fields.Select(field =>
                    new DynamicFieldInput(field.NId, field.Settings with { Name = "Changed " + field.NId })).ToArray());
            else
            {
                var current = Assert.Single(definition.Records);
                definition.UpdateRecord(current.Id, current.NId, current.Settings with { Name = "Changed" },
                    [new("Alpha", Scalar("99")), new("Zulu", Scalar("99"))]);
            }
        });

        AssertConflict(error);
        Assert.Equal(before, Snapshot(definition));
    }

    [Theory]
    [InlineData(ReferenceDataType.String, "\"a\\u0000b\"")]
    [InlineData(ReferenceDataType.Json, "{\"x\":\"\\u0000\"}")]
    [InlineData(ReferenceDataType.Json, "{\"\\u0000\":1}")]
    public void PostgreSql_incompatible_nul_is_rejected_in_scalar_strings_and_json(ReferenceDataType type, string json)
    {
        using var document = JsonDocument.Parse(json);

        var error = Assert.Throws<ReferenceDataException>(() => ReferenceScalar.Parse(type, document.RootElement));

        Assert.Equal("REF-VALIDATION-FAILED", error.ErrorCode);
        Assert.Equal("value", error.Field);
    }

    private static DynamicConfigDefinition Create()
    {
        var definition = new DynamicConfigDefinition("Matrix", "Matrix", null, ReferenceScopeType.Tenant, "TENANT", null);
        definition.Update("Matrix", null, [Field("Alpha"), Field("Zulu")]);
        return definition;
    }

    private static DynamicFieldInput Field(string nId) => new(nId,
        new(nId, ReferenceDataType.Decimal, false, true, 0, null, null, null, null, null, null, null, null, null, null));

    private static DynamicConfigRecord AddValues(DynamicConfigDefinition definition) =>
        definition.AddRecord("FIRST", new(null, null, 0, true), [new("Alpha", Scalar("1")), new("Zulu", Scalar("2"))]);

    private static ReferenceScalar Scalar(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ReferenceScalar.Parse(ReferenceDataType.Decimal, document.RootElement);
    }

    private static void Protect(Entity entity, bool locked)
    {
        if (locked) entity.Lock();
        else entity.Freeze();
    }

    private static void AssertConflict(ReferenceDataException error)
    {
        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
        Assert.Equal(409, error.Status);
    }

    private static DynamicDefinitionState State(DynamicConfigDefinition definition) => new(DynamicEntityState.From(definition),
        definition.NId, definition.Name, definition.Description, definition.ScopeType, definition.TenantNId, definition.Revision,
        definition.Status, definition.PublishedOn, definition.PublishedBy);

    private static DynamicConfigDefinition Restore(DynamicConfigDefinition definition, long? rootVersion = null,
        IReadOnlyList<DynamicConfigFieldDefinition>? fields = null, IReadOnlyList<DynamicConfigRecord>? records = null)
    {
        var state = State(definition);
        if (rootVersion is long version) state = state with { Entity = state.Entity with { OptimisticVersion = version } };
        return DynamicConfigDefinition.Restore(state, fields ?? definition.Fields, records ?? definition.Records);
    }

    private static DynamicConfigFieldDefinition RestoreField(DynamicConfigFieldDefinition field, long version) =>
        DynamicConfigFieldDefinition.Restore(DynamicEntityState.From(field) with { OptimisticVersion = version },
            field.DynamicConfigDefinitionId, field.NId, field.Settings, field.HasHadValue, field.WasPublished);

    private static DynamicConfigRecord RestoreRecord(DynamicConfigRecord record, long? version = null,
        IReadOnlyList<DynamicConfigFieldValue>? values = null)
    {
        var state = DynamicEntityState.From(record);
        if (version is long number) state = state with { OptimisticVersion = number };
        return DynamicConfigRecord.Restore(state, record.DynamicConfigDefinitionId, record.NId, record.Settings, values ?? record.Values);
    }

    private static string Snapshot(DynamicConfigDefinition definition) => JsonSerializer.Serialize(new
    {
        Definition = State(definition),
        Fields = definition.Fields.Select(field => new
        {
            Entity = DynamicEntityState.From(field), field.DynamicConfigDefinitionId, field.NId, field.Settings,
            field.HasHadValue, field.WasPublished,
        }),
        Records = definition.Records.Select(record => new
        {
            Entity = DynamicEntityState.From(record), record.DynamicConfigDefinitionId, record.NId, record.Settings,
            Values = record.Values.Select(value => new
            {
                Entity = DynamicEntityState.From(value), value.DynamicConfigDefinitionId, value.DynamicConfigRecordId,
                value.DynamicConfigFieldDefinitionId, value.FieldNId, value.Value.DataType, value.Value.CanonicalValue,
            }),
        }),
    });
}
