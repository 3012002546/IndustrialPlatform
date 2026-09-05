using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure;

namespace IndustrialPlatform.ReferenceData.Tests.Domain;

public sealed class UnitOfMeasureDomainTests
{
    [Fact]
    public void Ratio_dimension_requires_one_enabled_identity_base_positive_factors_and_zero_offsets()
    {
        var dimension = Create();
        dimension.Update("Mass", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m), Unit("g", 0.001m)]);
        Assert.Equal("KG", dimension.BaseUnitNId);
        Assert.Equal(["G", "KG"], dimension.Units.Select(unit => unit.NId).Order().ToArray());

        AssertError(() => Create().Update("Mass", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 0m)]));
        AssertError(() => Create().Update("Mass", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m, 1m)]));
        AssertError(() => Create().Update("Mass", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 2m)]));
        AssertError(() => Create().Update("Mass", null, UnitConversionKind.Ratio, "kg", [Unit("g", 0.001m)]));
        AssertError(() => Create().Update("Mass", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m, enabled: false)]));
        AssertError(() => Create().Update("Mass", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m), Unit("KG", 1m)]));
        AssertError(() => Create().Update("Mass", null, UnitConversionKind.Ratio, "kg",
            Enumerable.Range(0, 201).Select(index => Unit($"U{index}", index == 0 ? 1m : index)).ToArray()));
    }

    [Fact]
    public void Conversion_uses_one_base_path_and_only_rounds_at_the_target()
    {
        var temperature = new UnitDimension("Temperature", "Temperature", null, ReferenceScopeType.Tenant, "TENANT-A", null);
        temperature.Update("Temperature", null, UnitConversionKind.AbsoluteTemperature, "K",
        [
            Unit("K", 1m, 0m, 2),
            Unit("degC", 1m, 273.15m, 2),
            Unit("degF", 0.555555555556m, 255.372222222222m, 2),
        ]);
        var freezing = temperature.Convert("degC", "degF", 0m);
        Assert.Equal(32.00m, freezing.ResultValue);
        Assert.True(freezing.WasRounded);
        Assert.Equal(0.555555555556m, freezing.Target.FactorToBase);
        Assert.Equal(255.372222222222m, freezing.Target.OffsetToBase);

        var even = Create();
        even.Update("Count", null, UnitConversionKind.Ratio, "piece",
            [Unit("piece", 1m, decimalPlaces: 0, rounding: UnitRoundingMode.ToEven)]);
        Assert.Equal(2m, even.Convert("piece", "piece", 2.5m).ResultValue);
        Assert.Equal(-2m, even.Convert("piece", "piece", -2.5m).ResultValue);

        var away = Create();
        away.Update("Count", null, UnitConversionKind.Ratio, "piece",
            [Unit("piece", 1m, decimalPlaces: 0, rounding: UnitRoundingMode.AwayFromZero)]);
        Assert.Equal(3m, away.Convert("piece", "piece", 2.5m).ResultValue);
        Assert.Equal(-3m, away.Convert("piece", "piece", -2.5m).ResultValue);
    }

    [Fact]
    public void Conversion_rejects_disabled_or_unknown_units_and_decimal_overflow()
    {
        var dimension = Create();
        dimension.Update("Mass", null, UnitConversionKind.Ratio, "kg",
            [Unit("kg", 1m), Unit("g", 0.001m), Unit("old", 2m, enabled: false)]);
        AssertError(() => dimension.Convert("kg", "other", 1m));
        AssertError(() => dimension.Convert("old", "kg", 1m));

        var overflow = Create();
        overflow.Update("Large", null, UnitConversionKind.Ratio, "base", [Unit("base", 1m), Unit("double", 2m)]);
        var error = Assert.Throws<ReferenceDataException>(() => overflow.Convert("double", "base", decimal.MaxValue));
        Assert.Equal("REF-UNIT-NUMERIC-OVERFLOW", error.ErrorCode);
    }

    [Fact]
    public void Published_content_is_immutable_and_clone_has_new_root_and_unit_ids()
    {
        var original = Create();
        original.Update("Mass", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m), Unit("g", 0.001m)]);
        original.Publish("ADMIN");
        original.Unfreeze();
        Assert.Equal("REF-INVALID-STATE", Assert.Throws<ReferenceDataException>(() =>
            original.Update("Changed", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m)])).ErrorCode);

        var clone = original.Clone(2);
        Assert.Equal(PublicationStatus.Draft, clone.Status);
        Assert.Equal(1, clone.SourceRevision);
        Assert.NotEqual(original.Id, clone.Id);
        Assert.All(clone.Units, unit => Assert.DoesNotContain(original.Units, old => old.Id == unit.Id));
        clone.Update("Mass v2", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m), Unit("g", 0.001m)]);
    }

    [Fact]
    public void Draft_updates_preserve_matching_unit_identity_and_advance_only_changed_children()
    {
        var dimension = Create();
        dimension.Update("Mass", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m), Unit("g", 0.001m)]);
        var before = dimension.Units.ToDictionary(unit => unit.NId);

        dimension.Update("Mass", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m), Unit("g", 0.002m)]);

        var after = dimension.Units.ToDictionary(unit => unit.NId);
        Assert.Equal(before["KG"].Id, after["KG"].Id);
        Assert.Equal(before["KG"].OptimisticVersion, after["KG"].OptimisticVersion);
        Assert.Equal(before["G"].Id, after["G"].Id);
        Assert.Equal(before["G"].OptimisticVersion + 1, after["G"].OptimisticVersion);
    }

    [Fact]
    public void System_defined_dimensions_reject_every_normal_lifecycle_write()
    {
        var system = UnitDimension.CreateSystemDefined("Mass", "Mass", null, UnitConversionKind.Ratio, "kg",
            [Unit("kg", 1m), Unit("g", 0.001m)], 1, new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero));
        foreach (var action in new Action[]
        {
            () => system.Update("Changed", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m)]),
            () => system.Clone(2),
            () => system.Publish("ADMIN"),
            system.Disable,
        })
        {
            var error = Assert.Throws<ReferenceDataException>(action);
            Assert.Equal("REF-UNIT-SYSTEM-DEFINED", error.ErrorCode);
            Assert.Equal(409, error.Status);
        }
    }

    [Fact]
    public void Exposed_unit_snapshots_cannot_mutate_the_aggregate_or_bypass_its_version()
    {
        var dimension = Create();
        dimension.Update("Mass", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m), Unit("g", 0.001m)]);
        var version = dimension.OptimisticVersion;

        dimension.Units.Single(unit => unit.NId == "G").MarkDeleted();

        Assert.False(dimension.Units.Single(unit => unit.NId == "G").IsDeleted);
        Assert.Equal(version, dimension.OptimisticVersion);
    }

    [Fact]
    public void Version_exhaustion_rejects_changes_before_mutating_root_or_units()
    {
        var original = Create();
        original.Update("Mass", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m), Unit("g", 0.001m)]);
        var exhausted = UnitDimension.Restore(new(
            original.Id, original.NId, original.Name, original.Description, original.ScopeType, original.TenantNId,
            original.Revision, original.Status, original.SourceRevision, original.PublishedOn, original.PublishedBy,
            original.IsSystemDefined, original.ConversionKind, original.BaseUnitNId, original.IsFrozen,
            original.IsLocked, original.IsDeleted, original.CreatedOn, original.LastUpdatedOn, long.MaxValue,
            original.ConcurrencyVersion), original.Units);
        var unitIds = exhausted.Units.Select(unit => unit.Id).ToArray();

        var error = Assert.Throws<ReferenceDataException>(() => exhausted.Update(
            "Changed", null, UnitConversionKind.Ratio, "kg", [Unit("kg", 1m), Unit("g", 0.002m)]));

        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
        Assert.Equal("Mass", exhausted.Name);
        Assert.Equal(long.MaxValue, exhausted.OptimisticVersion);
        Assert.Equal(unitIds, exhausted.Units.Select(unit => unit.Id).ToArray());
    }

    private static UnitDimension Create() => new("Mass", "Mass", null, ReferenceScopeType.Tenant, "TENANT-A", null);
    private static UnitDefinition Unit(string nId, decimal factor, decimal offset = 0m, int decimalPlaces = 3,
        UnitRoundingMode rounding = UnitRoundingMode.ToEven, bool enabled = true) =>
        new(nId, nId, nId, factor, offset, decimalPlaces, rounding, enabled, 0);
    private static void AssertError(Action action) => Assert.Equal("REF-UNIT-CONVERSION-INVALID",
        Assert.Throws<ReferenceDataException>(action).ErrorCode);
}
