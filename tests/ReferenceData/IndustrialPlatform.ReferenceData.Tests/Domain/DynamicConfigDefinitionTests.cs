using System.Text.Json;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.DynamicProperty;

namespace IndustrialPlatform.ReferenceData.Tests.Domain;

public sealed class DynamicConfigDefinitionTests
{
    [Fact]
    public void Defaults_are_copied_only_when_a_record_is_created_and_false_zero_and_empty_are_values()
    {
        var definition = Create();
        definition.Update("Matrix", null, [Field("Weight", ReferenceDataType.Decimal, true, "0"), Field("Review", ReferenceDataType.Boolean, true, "false"), Field("Label", ReferenceDataType.String, true, "\"\"")]);
        var row = definition.AddRecord("FIRST", new(null, null, 0, true), []);
        Assert.Equal("0", Value(row, "WEIGHT").CanonicalValue);
        Assert.Equal("false", Value(row, "REVIEW").CanonicalValue);
        Assert.Equal("\"\"", Value(row, "LABEL").CanonicalValue);
        definition.Update("Matrix", null, [Field("Weight", ReferenceDataType.Decimal, true, "5"), Field("Review", ReferenceDataType.Boolean, true, "true"), Field("Label", ReferenceDataType.String, true, "\"new\"")]);
        Assert.Equal("0", Value(row, "WEIGHT").CanonicalValue);
        Assert.Throws<ReferenceDataException>(() => definition.UpdateRecord(row.Id, row.NId, row.Settings, []));
        Assert.Equal("0", Value(row, "WEIGHT").CanonicalValue);
    }

    [Fact]
    public void Cloning_published_data_creates_new_ids_and_same_revision_links_and_content_stays_immutable()
    {
        var original = Create(); original.Update("Matrix", null, [Field("Weight", ReferenceDataType.Decimal, true)]);
        original.AddRecord("FIRST", new(null, "RAW", 0, true), [new("Weight", Scalar(ReferenceDataType.Decimal, "12.50"))]);
        original.Publish("ADMIN");
        Assert.Throws<ReferenceDataException>(() => original.Update("Changed", null, []));
        var clone = original.Clone(2);
        Assert.Equal(PublicationStatus.Draft, clone.Status);
        Assert.NotEqual(original.Id, clone.Id);
        Assert.NotEqual(original.Fields[0].Id, clone.Fields[0].Id);
        Assert.NotEqual(original.Records[0].Id, clone.Records[0].Id);
        var value = Assert.Single(clone.Records[0].Values);
        Assert.Equal(clone.Id, value.DynamicConfigDefinitionId);
        Assert.Equal(clone.Fields[0].Id, value.DynamicConfigFieldDefinitionId);
        Assert.Equal(clone.Records[0].Id, value.DynamicConfigRecordId);
        Assert.Equal("12.5", value.Value.CanonicalValue);
        Assert.Throws<ReferenceDataException>(() => clone.Update("Matrix", null, []));
    }

    [Fact]
    public void Used_field_types_cannot_change_after_clearing_values_and_failed_schema_changes_are_atomic()
    {
        var definition = Create(); definition.Update("Matrix", null, [Field("Weight", ReferenceDataType.Decimal)]);
        var row = definition.AddRecord("FIRST", new(null, null, 0, true), [new("Weight", Scalar(ReferenceDataType.Decimal, "1"))]);
        definition.UpdateRecord(row.Id, row.NId, row.Settings, []);
        var token = definition.ConcurrencyVersion;
        Assert.Throws<ReferenceDataException>(() => definition.Update("Changed", null, [Field("Weight", ReferenceDataType.String)]));
        Assert.Equal(token, definition.ConcurrencyVersion);
        Assert.Equal("Matrix", definition.Name);
        Assert.Throws<ReferenceDataException>(() => definition.Update("Matrix", null, []));
    }

    [Fact]
    public void Field_constraints_reject_unsafe_patterns_lossy_scale_and_wrong_type_constraints()
    {
        var definition = Create();
        var text = Field("Name", ReferenceDataType.String);
        Assert.Throws<ReferenceDataException>(() => definition.Update("Matrix", null, [text with { Settings = text.Settings with { Pattern = "(a+)\\1" } }]));
        Assert.Throws<ReferenceDataException>(() => definition.Update("Matrix", null, [text with { Settings = text.Settings with { MinValue = 1 } }]));
        var number = Field("Weight", ReferenceDataType.Decimal);
        definition.Update("Matrix", null, [number with { Settings = number.Settings with { Scale = 2, MinValue = 0, MaxValue = 10 } }]);
        Assert.Throws<ReferenceDataException>(() => definition.AddRecord("FIRST", new(null, null, 0, true), [new("Weight", Scalar(ReferenceDataType.Decimal, "1.001"))]));
        Assert.Throws<ReferenceDataException>(() => definition.AddRecord("FIRST", new(null, null, 0, true), [new("Weight", Scalar(ReferenceDataType.Decimal, "11"))]));
        Assert.Empty(definition.Records);
    }

    [Fact]
    public void Publish_requires_enabled_fields_and_rows_and_every_write_uses_root_versions()
    {
        var definition = Create();
        Assert.Throws<ReferenceDataException>(() => definition.Publish("ADMIN"));
        definition.Update("Matrix", null, [Field("Weight", ReferenceDataType.Decimal)]);
        Assert.Throws<ReferenceDataException>(() => definition.Publish("ADMIN"));
        var token = definition.ConcurrencyVersion;
        definition.AddRecord("FIRST", new(null, null, 0, true), []);
        Assert.Throws<ReferenceDataException>(() => definition.CheckVersion(definition.OptimisticVersion, token));
        definition.Publish("ADMIN");
        Assert.Equal(PublicationStatus.Published, definition.Status);
    }

    private static DynamicConfigDefinition Create() => new("Matrix", "Matrix", null, ReferenceScopeType.Tenant, "TENANT", null);
    private static DynamicFieldInput Field(string nId, ReferenceDataType type, bool required = false, string? defaultJson = null) => new(nId,
        new(nId, type, required, true, 0, defaultJson is null ? null : Scalar(type, defaultJson), null, null, null, null, null, null, null, null, null));
    private static ReferenceScalar Scalar(ReferenceDataType type, string json) { using var document = JsonDocument.Parse(json); return ReferenceScalar.Parse(type, document.RootElement); }
    private static ReferenceScalar Value(DynamicConfigRecord row, string field) => row.Values.Single(value => value.FieldNId == field).Value;
}
