using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Metadata;

namespace IndustrialPlatform.ReferenceData.Tests.Domain;

public sealed class MetadataSchemaDomainTests
{
    [Fact]
    public void Attribute_types_accept_only_their_own_constraints_and_safe_patterns()
    {
        var schema = Schema();
        schema.Update("Equipment", null,
        [
            Attribute("Serial", ReferenceDataType.String) with
            {
                Settings = Settings(ReferenceDataType.String) with { MinLength = 1, MaxLength = 40, Pattern = "^[A-Z0-9-]+$" },
            },
            Attribute("Power", ReferenceDataType.Decimal) with
            {
                Settings = Settings(ReferenceDataType.Decimal) with { Precision = 18, Scale = 6, MinValue = 0m },
            },
            Attribute("Cycles", ReferenceDataType.Integer) with
            {
                Settings = Settings(ReferenceDataType.Integer) with { MinValue = 0m, MaxValue = 1000m },
            },
            Attribute("Enabled", ReferenceDataType.Boolean),
            Attribute("BuiltOn", ReferenceDataType.Date) with
            {
                Settings = Settings(ReferenceDataType.Date) with { DefaultValue = "2026-09-05" },
            },
            Attribute("ObservedOn", ReferenceDataType.DateTime) with
            {
                Settings = Settings(ReferenceDataType.DateTime) with { DefaultValue = "2026-09-05T08:00:00+08:00" },
            },
            Attribute("Mode", ReferenceDataType.Enum) with
            {
                Settings = Settings(ReferenceDataType.Enum) with { DictionaryNId = "EquipmentMode" },
            },
            Attribute("Material", ReferenceDataType.Reference) with
            {
                Settings = Settings(ReferenceDataType.Reference) with { ReferenceTarget = "Material" },
            },
        ]);

        Assert.Equal(8, schema.Attributes.Count);
        Assert.Contains(schema.Attributes, attribute => attribute.NId == "SERIAL" && attribute.Settings.Pattern is not null);
        AssertInvalid(() => Schema().Update("Bad", null,
            [Attribute("Bad", ReferenceDataType.Boolean) with { Settings = Settings(ReferenceDataType.Boolean) with { MaxLength = 10 } }]));
        AssertInvalid(() => Schema().Update("Bad", null,
            [Attribute("Bad", ReferenceDataType.Decimal) with { Settings = Settings(ReferenceDataType.Decimal) with { Precision = 4, Scale = 5 } }]));
        AssertInvalid(() => Schema().Update("Bad", null,
            [Attribute("Bad", ReferenceDataType.String) with { Settings = Settings(ReferenceDataType.String) with { Pattern = "(?=unsafe)" } }]));
        AssertInvalid(() => Schema().Update("Bad", null,
            [Attribute("Bad", ReferenceDataType.Enum)]));
        AssertInvalid(() => Schema().Update("Bad", null,
            [Attribute("Bad", ReferenceDataType.Reference)]));
        AssertInvalid(() => Schema().Update("Bad", null,
            [Attribute("Same", ReferenceDataType.String), Attribute("same", ReferenceDataType.String)]));
    }

    [Fact]
    public void Decimal_unit_reference_is_an_all_or_none_fixed_source_tuple()
    {
        var schema = Schema();
        schema.Update("Equipment", null,
        [
            Attribute("Weight", ReferenceDataType.Decimal) with
            {
                Settings = Settings(ReferenceDataType.Decimal) with
                {
                    UnitDimensionNId = "Mass", DefaultUnitNId = "kg", UnitRevision = 3,
                    UnitSourceScope = ReferenceScopeType.Tenant, UnitSourceTenantNId = "TENANT-A",
                },
            },
        ]);
        Assert.Equal(3, schema.Attributes[0].Settings.UnitRevision);

        AssertInvalid(() => Schema().Update("Bad", null,
            [Attribute("Weight", ReferenceDataType.Decimal) with { Settings = Settings(ReferenceDataType.Decimal) with { UnitDimensionNId = "Mass" } }]));
        AssertInvalid(() => Schema().Update("Bad", null,
            [Attribute("Label", ReferenceDataType.String) with
            {
                Settings = Settings(ReferenceDataType.String) with
                {
                    UnitDimensionNId = "Mass", UnitRevision = 1, UnitSourceScope = ReferenceScopeType.Platform,
                },
            }]));
        AssertInvalid(() => Schema().Update("Bad", null,
            [Attribute("Weight", ReferenceDataType.Decimal) with
            {
                Settings = Settings(ReferenceDataType.Decimal) with
                {
                    UnitDimensionNId = "Mass", UnitRevision = 1, UnitSourceScope = ReferenceScopeType.Platform,
                    UnitSourceTenantNId = "TENANT-A",
                },
            }]));
        AssertInvalid(() => Schema().Update("Bad", null,
            [Attribute("Weight", ReferenceDataType.Decimal) with
            {
                Settings = Settings(ReferenceDataType.Decimal) with { MinValue = 0.1234567890123m },
            }]));
    }

    [Fact]
    public void Enum_defaults_are_normalized_for_scalar_and_array_values()
    {
        var schema = Schema();
        schema.Update("Equipment", null,
        [
            Attribute("Mode", ReferenceDataType.Enum) with
            {
                Settings = Settings(ReferenceDataType.Enum) with
                {
                    DictionaryNId = "EquipmentMode", DefaultValue = "auto",
                },
            },
            Attribute("Modes", ReferenceDataType.Enum) with
            {
                Settings = Settings(ReferenceDataType.Enum) with
                {
                    DictionaryNId = "EquipmentMode", IsArray = true, DefaultValue = "[\"auto\",\"manual\"]",
                },
            },
        ]);

        Assert.Equal("AUTO", schema.Attributes.Single(attribute => attribute.NId == "MODE").Settings.DefaultValue);
        Assert.Equal("[\"AUTO\",\"MANUAL\"]",
            schema.Attributes.Single(attribute => attribute.NId == "MODES").Settings.DefaultValue);
    }

    [Fact]
    public void Published_schema_is_immutable_and_clone_keeps_history_with_new_ids()
    {
        var original = Schema();
        original.Update("Equipment", null, [Attribute("Serial", ReferenceDataType.String)]);
        original.Publish("ADMIN");
        original.Unfreeze();
        Assert.Equal("REF-INVALID-STATE", Assert.Throws<ReferenceDataException>(() =>
            original.Update("Changed", null, [Attribute("Serial", ReferenceDataType.String)])).ErrorCode);

        var clone = original.Clone(2);
        Assert.Equal(PublicationStatus.Draft, clone.Status);
        Assert.Equal(1, clone.SourceRevision);
        Assert.NotEqual(original.Id, clone.Id);
        Assert.All(clone.Attributes, attribute => Assert.DoesNotContain(original.Attributes, old => old.Id == attribute.Id));
        AssertInvalid(() => clone.Update("Equipment", null, []));

        clone.Update("Equipment v2", null,
        [
            Attribute("Serial", ReferenceDataType.String) with { Settings = Settings(ReferenceDataType.String) with { Enabled = false } },
            Attribute("Voltage", ReferenceDataType.Decimal),
        ]);
    }

    [Fact]
    public void Child_snapshots_cannot_mutate_the_aggregate_and_same_revision_updates_preserve_identity()
    {
        var schema = Schema();
        schema.Update("Equipment", null, [Attribute("Serial", ReferenceDataType.String)]);
        var before = schema.Attributes.Single();
        var childVersion = before.OptimisticVersion;
        before.MarkDeleted();
        Assert.False(schema.Attributes.Single().IsDeleted);

        var rootVersion = schema.OptimisticVersion;
        schema.Update("Equipment", null,
        [
            Attribute("Serial", ReferenceDataType.String) with
            {
                Settings = Settings(ReferenceDataType.String) with { Name = "Serial number" },
            },
        ]);
        var after = schema.Attributes.Single();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(childVersion + 1, after.OptimisticVersion);
        Assert.Equal(rootVersion + 1, schema.OptimisticVersion);
    }

    [Fact]
    public void Exhausted_root_version_rejects_changes_without_partial_mutation()
    {
        var schema = Schema();
        schema.Update("Equipment", null, [Attribute("Serial", ReferenceDataType.String)]);
        var restored = EntitySchema.Restore(new(
            MetadataEntityState.From(schema) with { OptimisticVersion = long.MaxValue },
            schema.NId, schema.Name, schema.Description, schema.ScopeType, schema.TenantNId, schema.Revision,
            schema.Status, schema.SourceRevision, schema.PublishedOn, schema.PublishedBy), schema.Attributes);

        var error = Assert.Throws<ReferenceDataException>(() => restored.Update("Changed", null,
            [Attribute("Serial", ReferenceDataType.String)]));
        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
        Assert.Equal("Equipment", restored.Name);
        Assert.Equal(long.MaxValue, restored.OptimisticVersion);
    }

    private static EntitySchema Schema() => new("Equipment", "Equipment", null, ReferenceScopeType.Tenant, "TENANT-A", null);
    private static MetadataAttributeInput Attribute(string nId, ReferenceDataType type) => new(nId, Settings(type));
    private static MetadataAttributeSettings Settings(ReferenceDataType type) => new(
        "Attribute", type, Required: false, IsArray: false, Enabled: true, Sort: 0, DefaultValue: null,
        MinLength: null, MaxLength: null, MinValue: null, MaxValue: null, Pattern: null,
        DictionaryNId: null, ReferenceTarget: null, Precision: null, Scale: null,
        UnitDimensionNId: null, DefaultUnitNId: null, UnitRevision: null, UnitSourceScope: null,
        UnitSourceTenantNId: null, Description: null);
    private static void AssertInvalid(Action action) =>
        Assert.Equal("REF-VALIDATION-FAILED", Assert.Throws<ReferenceDataException>(action).ErrorCode);
}
