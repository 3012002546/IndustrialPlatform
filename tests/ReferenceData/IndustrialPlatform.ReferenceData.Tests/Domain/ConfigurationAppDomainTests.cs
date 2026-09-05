using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.Parameter;

namespace IndustrialPlatform.ReferenceData.Tests.Domain;

public sealed class ConfigurationAppDomainTests
{
    [Fact]
    public void Real_changes_advance_root_and_child_once_while_no_op_preserves_versions()
    {
        var domain = Create();
        var key = domain.AddKey("Gate", Settings());
        var rootVersion = domain.OptimisticVersion;
        var token = domain.ConcurrencyVersion;
        domain.UpdateKey(key.Id, "Gate", Settings());
        Assert.Equal(token, domain.ConcurrencyVersion);
        domain.UpdateKey(key.Id, "Gate", Settings() with { Value = Scalar("false") });
        Assert.Equal(rootVersion + 1, domain.OptimisticVersion);
        Assert.Equal(3, domain.Revision);
        Assert.Equal(1, key.OptimisticVersion);
        Assert.Equal("false", key.ResolveSingle()!.CanonicalValue);
    }

    [Fact]
    public void Existing_read_only_state_cannot_be_cleared_or_disabled()
    {
        var domain = Create();
        var key = domain.AddKey("Gate", Settings() with { IsReadOnly = true });
        var token = domain.ConcurrencyVersion;
        Assert.Equal("REF-CONFIG-READ-ONLY", Assert.Throws<ReferenceDataException>(() => domain.UpdateKey(key.Id, "Gate", Settings())).ErrorCode);
        Assert.Equal("REF-CONFIG-READ-ONLY", Assert.Throws<ReferenceDataException>(() => domain.SetKeyStatus(key.Id, ConfigurationStatus.Disabled)).ErrorCode);
        Assert.Equal(token, domain.ConcurrencyVersion);
    }

    [Fact]
    public void Empty_optional_value_and_false_are_distinct_from_default_and_mandatory_is_enforced()
    {
        var domain = Create();
        var key = domain.AddKey("Gate", Settings() with { Value = null, DefaultValue = Scalar("true") });
        Assert.Equal("true", key.ResolveSingle()!.CanonicalValue);
        domain.UpdateKey(key.Id, "Gate", Settings() with { Value = Scalar("false"), DefaultValue = Scalar("true") });
        Assert.Equal("false", key.ResolveSingle()!.CanonicalValue);
        domain.UpdateKey(key.Id, "Gate", Settings() with { Value = null });
        Assert.Null(key.ResolveSingle());
        var token = domain.ConcurrencyVersion;
        Assert.Equal("REF-CONFIG-MANDATORY-VALUE-MISSING", Assert.Throws<ReferenceDataException>(() => domain.UpdateKey(key.Id, "Gate", Settings() with { Value = null, IsMandatory = true })).ErrorCode);
        Assert.Equal(token, domain.ConcurrencyVersion);
    }

    [Fact]
    public void Multi_values_keep_identity_and_validate_enabled_uniqueness_and_defaults_atomically()
    {
        var domain = Create();
        var key = domain.AddKey("Sources", Settings() with { DataType = ReferenceDataType.Decimal, ValueMode = ConfigurationValueMode.Multi, Value = null });
        var one = domain.AddValue(key.Id, "first", Value("1.0", 2, true));
        var second = domain.AddValue(key.Id, "second", Value("2", 1, true));
        Assert.Equal(new[] { second.Id, one.Id }, key.EnabledValues().Select(item => item.Id));
        var token = domain.ConcurrencyVersion;
        Assert.Equal("REF-CONFIG-DUPLICATE-VALUE", Assert.Throws<ReferenceDataException>(() => domain.AddValue(key.Id, "third", Value("1.00", 0))).ErrorCode);
        Assert.Equal("REF-CONFIG-DEFAULT-MUST-BE-ENABLED", Assert.Throws<ReferenceDataException>(() => domain.SetValueEnabled(key.Id, one.Id, false)).ErrorCode);
        Assert.Equal(token, domain.ConcurrencyVersion);
        domain.UpdateValue(key.Id, one.Id, "first", Value("3", 0));
        Assert.Equal(one.Id, key.MultiValues.Single(item => item.NId == "FIRST").Id);
        Assert.Equal("3", one.Settings.Value.CanonicalValue);
    }

    [Fact]
    public void Stable_paths_and_used_types_cannot_change_and_factory_is_rejected()
    {
        var domain = Create();
        var key = domain.AddKey("Gate", Settings());
        Assert.Throws<ReferenceDataException>(() => domain.UpdateKey(key.Id, "Renamed", Settings()));
        Assert.Equal("REF-CONFIG-TYPE-CHANGE-NOT-ALLOWED", Assert.Throws<ReferenceDataException>(() => domain.UpdateKey(key.Id, "Gate", Settings() with { ValueMode = ConfigurationValueMode.Multi, Value = null })).ErrorCode);
        domain.UpdateKey(key.Id, "Gate", Settings() with { Value = null });
        Assert.True(key.HasHadValue);
        Assert.Equal("REF-CONFIG-TYPE-CHANGE-NOT-ALLOWED", Assert.Throws<ReferenceDataException>(() => domain.UpdateKey(key.Id, "Gate", Settings() with { DataType = ReferenceDataType.String, Value = null })).ErrorCode);
        Assert.Throws<ReferenceDataException>(() => domain.AddKey("with.dot", Settings()));
        Assert.Equal("REF-CONFIG-SENSITIVE-REJECTED", Assert.Throws<ReferenceDataException>(() => domain.AddKey("ClientSecret", Settings())).ErrorCode);
        Assert.Equal("REF-SCOPE-FACTORY-NOT-READY", Assert.Throws<ReferenceDataException>(() => new ConfigurationAppDomain("Trace", "Trace", null, ReferenceScopeType.Factory, "TENANT", "F")).ErrorCode);
    }

    [Fact]
    public void Disabled_mandatory_key_cannot_bypass_required_value_and_exhausted_root_is_unchanged()
    {
        var domain = Create();
        Assert.Equal(422, Assert.Throws<ReferenceDataException>(() => domain.AddKey("Gate", Settings() with { Status = ConfigurationStatus.Disabled, IsMandatory = true, Value = null })).Status);
        var stamp = new ConfigurationEntityState(domain.Id, false, false, false, domain.CreatedOn, domain.LastUpdatedOn, domain.OptimisticVersion, domain.ConcurrencyVersion);
        var exhausted = ConfigurationAppDomain.Restore(new(stamp, domain.NId, domain.Name, null, domain.ScopeType, domain.TenantNId, domain.Status, long.MaxValue), []);
        Assert.Throws<ReferenceDataException>(() => exhausted.Update(domain.NId, "Changed", null));
        Assert.Equal(domain.Name, exhausted.Name);
        Assert.Equal(domain.ConcurrencyVersion, exhausted.ConcurrencyVersion);
    }

    private static ConfigurationAppDomain Create() => new("Trace", "Trace", null, ReferenceScopeType.Tenant, "TENANT", null);
    private static ConfigurationScalar Scalar(string json) => ConfigurationScalarTests.Parse(ReferenceDataType.Boolean, json);
    private static ConfigurationKeySettings Settings() => new("Gate", null, ReferenceDataType.Boolean, ConfigurationValueMode.Single, Scalar("true"), null, false, false, null, null, ConfigurationStatus.Active, 0);
    private static ConfigurationValueSettings Value(string value, int sort, bool isDefault = false) => new(null, ConfigurationScalarTests.Parse(ReferenceDataType.Decimal, value), sort, isDefault, true);
}
