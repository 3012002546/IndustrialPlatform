using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.ReferenceData.Domain.Parameter;

public sealed record ConfigurationKeySettings(string Name, string? Description, ReferenceDataType DataType,
    ConfigurationValueMode ValueMode, ConfigurationScalar? Value, ConfigurationScalar? DefaultValue, bool IsMandatory,
    bool IsReadOnly, string? DictionaryNId, string? ReferenceTarget, ConfigurationStatus Status, int Sort);
public sealed record ConfigurationValueSettings(string? Name, ConfigurationScalar Value, int Sort, bool IsDefault, bool Enabled);
public sealed record ConfigurationEntityState(Guid Id, bool IsFrozen, bool IsLocked, bool IsDeleted,
    DateTimeOffset CreatedOn, DateTimeOffset LastUpdatedOn, long OptimisticVersion, Guid ConcurrencyVersion);
public sealed record ConfigurationAppDomainState(ConfigurationEntityState Entity, string NId, string Name, string? Description,
    ReferenceScopeType ScopeType, string? TenantNId, ConfigurationStatus Status, long Revision);

public sealed class ConfigurationAppDomain : AggregateRoot
{
    private readonly List<ConfigurationKey> keys = [];
    public string NId { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public ReferenceScopeType ScopeType { get; }
    public string? TenantNId { get; }
    public ConfigurationStatus Status { get; private set; } = ConfigurationStatus.Active;
    public long Revision { get; private set; } = 1;
    public IReadOnlyList<ConfigurationKey> Keys => keys.AsReadOnly();

    public ConfigurationAppDomain(string nId, string name, string? description, ReferenceScopeType scopeType, string? tenantNId, string? scopeId)
    {
        ReferenceValidation.Scope(scopeType, tenantNId, scopeId);
        NId = ParameterValidation.NId(nId); Name = ReferenceValidation.Name(name); Description = ReferenceValidation.Description(description);
        ScopeType = scopeType; TenantNId = tenantNId;
    }

    public void CheckVersion(long version, Guid token)
    {
        if (OptimisticVersion != version || ConcurrencyVersion != token) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    public void Update(string nId, string name, string? description)
    {
        EnsureCanModify();
        if (NId != ParameterValidation.NId(nId)) throw new ReferenceDataException("REF-CONFIG-NID-IMMUTABLE", 409, "nId");
        name = ReferenceValidation.Name(name); description = ReferenceValidation.Description(description);
        if (Name == name && Description == description) return;
        Name = name; Description = description; Changed();
    }

    public void SetStatus(ConfigurationStatus status)
    {
        EnsureCanModify(); ValidateStatus(status);
        if (Status == status) return;
        if (status == ConfigurationStatus.Active)
            foreach (var key in keys.Where(item => item.Settings.Status == ConfigurationStatus.Active)) key.CheckMandatory();
        Status = status; Changed();
    }

    public ConfigurationKey AddKey(string nId, ConfigurationKeySettings settings, IReadOnlyList<(string NId, ConfigurationValueSettings Settings)>? initialValues = null)
    {
        EnsureCanModify();
        if (keys.Count >= 500) throw new ReferenceDataException("REF-CONFIG-KEY-LIMIT", 409);
        nId = ParameterValidation.NId(nId);
        if (keys.Any(item => item.NId == nId)) throw new ReferenceDataException("REF-CONFIG-DUPLICATE-NID", 409, "nId");
        var key = new ConfigurationKey(Id, nId, settings, initialValues ?? []);
        keys.Add(key); Changed(); return key;
    }

    public void UpdateKey(Guid keyId, string nId, ConfigurationKeySettings settings)
    {
        EnsureCanModify();
        if (Key(keyId).Update(nId, settings)) Changed();
    }

    public void SetKeyStatus(Guid keyId, ConfigurationStatus status)
    {
        EnsureCanModify(); ValidateStatus(status);
        var key = Key(keyId);
        if (key.Update(key.NId, key.Settings with { Status = status })) Changed();
    }

    public ConfigurationKeyMultiValue AddValue(Guid keyId, string nId, ConfigurationValueSettings settings)
    {
        EnsureCanModify();
        var value = Key(keyId).AddValue(nId, settings); Changed(); return value;
    }

    public void UpdateValue(Guid keyId, Guid valueId, string nId, ConfigurationValueSettings settings)
    {
        EnsureCanModify();
        if (Key(keyId).UpdateValue(valueId, nId, settings)) Changed();
    }

    public void SetValueEnabled(Guid keyId, Guid valueId, bool enabled)
    {
        EnsureCanModify();
        var key = Key(keyId); var value = key.Value(valueId);
        if (key.UpdateValue(valueId, value.NId, value.Settings with { Enabled = enabled })) Changed();
    }

    public ConfigurationKey Key(Guid keyId) => keys.Find(item => item.Id == keyId && !item.IsDeleted)
        ?? throw new ReferenceDataException("REF-CONFIG-KEY-NOT-FOUND", 404);

    private new void EnsureCanModify()
    {
        base.EnsureCanModify();
        if (Revision == long.MaxValue || OptimisticVersion == long.MaxValue) throw new ReferenceDataException("REF-CONFIG-VERSION-EXHAUSTED", 409);
    }
    private void Changed() { Revision++; Touch(); }
    internal static void ValidateStatus(ConfigurationStatus status)
    {
        if (!Enum.IsDefined(status)) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "status");
    }

    public static ConfigurationAppDomain Restore(ConfigurationAppDomainState state, IReadOnlyList<ConfigurationKey> children)
    {
        var stamp = state.Entity;
        var domain = new ConfigurationAppDomain(state.NId, state.Name, state.Description, state.ScopeType, state.TenantNId, null)
        {
            Id = stamp.Id, IsFrozen = stamp.IsFrozen, IsLocked = stamp.IsLocked, IsDeleted = stamp.IsDeleted,
            CreatedOn = stamp.CreatedOn, LastUpdatedOn = stamp.LastUpdatedOn, OptimisticVersion = stamp.OptimisticVersion,
            ConcurrencyVersion = stamp.ConcurrencyVersion, Status = state.Status, Revision = state.Revision,
        };
        domain.keys.AddRange(children); return domain;
    }
}

public sealed class ConfigurationKey : Entity
{
    private readonly List<ConfigurationKeyMultiValue> values = [];
    public Guid ConfigurationAppDomainId { get; }
    public string NId { get; }
    public ConfigurationKeySettings Settings { get; private set; }
    public IReadOnlyList<ConfigurationKeyMultiValue> MultiValues => values.AsReadOnly();
    public bool HasActualValue => Settings.Value is not null || Settings.DefaultValue is not null || values.Count > 0;
    public bool HasHadValue { get; private set; }

    private ConfigurationKey(Guid domainId, string nId, ConfigurationKeySettings settings)
    {
        ConfigurationAppDomainId = domainId; NId = ParameterValidation.NId(nId); Settings = settings;
    }

    internal ConfigurationKey(Guid domainId, string nId, ConfigurationKeySettings settings, IReadOnlyList<(string NId, ConfigurationValueSettings Settings)> initialValues)
        : this(domainId, nId, settings)
    {
        var children = initialValues.Select(item => new ConfigurationKeyMultiValue(Id, item.NId, item.Settings)).ToArray();
        Settings = Validate(settings, children); values.AddRange(children); HasHadValue = HasActualValue;
    }

    internal bool Update(string nId, ConfigurationKeySettings settings)
    {
        EnsureWritable();
        if (NId != ParameterValidation.NId(nId)) throw new ReferenceDataException("REF-CONFIG-NID-IMMUTABLE", 409, "nId");
        if (HasHadValue && (settings.DataType != Settings.DataType || settings.ValueMode != Settings.ValueMode))
            throw new ReferenceDataException("REF-CONFIG-TYPE-CHANGE-NOT-ALLOWED", 409);
        settings = Validate(settings, values);
        if (Settings == settings) return false;
        Settings = settings; HasHadValue |= HasActualValue; Touch(); return true;
    }

    internal ConfigurationKeyMultiValue AddValue(string nId, ConfigurationValueSettings settings)
    {
        EnsureWritable();
        var value = new ConfigurationKeyMultiValue(Id, nId, settings);
        Validate(Settings, [.. values, value]);
        values.Add(value); HasHadValue = true; Touch(); return value;
    }

    internal bool UpdateValue(Guid id, string nId, ConfigurationValueSettings settings)
    {
        EnsureWritable();
        var value = Value(id);
        var candidate = new ConfigurationKeyMultiValue(Id, nId, settings);
        Validate(Settings, values.Select(item => item.Id == id ? candidate : item).ToArray());
        if (!value.Update(nId, candidate.Settings)) return false;
        Touch(); return true;
    }

    public ConfigurationKeyMultiValue Value(Guid id) => values.Find(item => item.Id == id && !item.IsDeleted)
        ?? throw new ReferenceDataException("REF-CONFIG-KEY-NOT-FOUND", 404);
    public IReadOnlyList<ConfigurationKeyMultiValue> EnabledValues() => values.Where(item => item.Settings.Enabled && !item.IsDeleted)
        .OrderBy(item => item.Settings.Sort).ThenBy(item => item.NId, StringComparer.Ordinal).ToArray();
    public ConfigurationScalar? ResolveSingle() { CheckMandatory(); return Settings.Value ?? Settings.DefaultValue; }
    public void CheckMandatory() => ValidateMandatory(Settings, values);

    private void EnsureWritable()
    {
        EnsureCanModify();
        if (Settings.IsReadOnly) throw new ReferenceDataException("REF-CONFIG-READ-ONLY", 409);
    }

    private static ConfigurationKeySettings Validate(ConfigurationKeySettings settings, IReadOnlyList<ConfigurationKeyMultiValue> children)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ConfigurationAppDomain.ValidateStatus(settings.Status);
        if (!Enum.IsDefined(settings.DataType) || !Enum.IsDefined(settings.ValueMode) || settings.Sort < 0) throw new ReferenceDataException("REF-VALIDATION-FAILED");
        settings = settings with { Name = ReferenceValidation.Name(settings.Name), Description = ReferenceValidation.Description(settings.Description) };
        if (settings.DataType == ReferenceDataType.Enum)
            settings = settings with { DictionaryNId = ReferenceValidation.NId(settings.DictionaryNId!) };
        else if (settings.DictionaryNId is not null) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "dictionaryNId");
        if (settings.DataType == ReferenceDataType.Reference)
        {
            if (string.IsNullOrWhiteSpace(settings.ReferenceTarget) || settings.ReferenceTarget.Length > 128) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "referenceTarget");
        }
        else if (settings.ReferenceTarget is not null) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "referenceTarget");
        if (settings.Value?.DataType != null && settings.Value.DataType != settings.DataType
            || settings.DefaultValue?.DataType != null && settings.DefaultValue.DataType != settings.DataType
            || children.Any(item => item.Settings.Value.DataType != settings.DataType)) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "value");
        if (settings.ValueMode == ConfigurationValueMode.Single ? children.Count > 0 : settings.Value is not null || settings.DefaultValue is not null)
            throw new ReferenceDataException("REF-CONFIG-VALUE-MODE-CONFLICT", 422);
        if (children.Count > 1000) throw new ReferenceDataException("REF-CONFIG-MULTI-VALUE-LIMIT", 422);
        if (children.Select(item => item.NId).Distinct(StringComparer.Ordinal).Count() != children.Count) throw new ReferenceDataException("REF-CONFIG-DUPLICATE-NID", 409, "nId");
        var enabled = children.Where(item => item.Settings.Enabled).ToArray();
        if (enabled.Select(item => item.Settings.Value.CanonicalValueHash).Distinct(StringComparer.Ordinal).Count() != enabled.Length)
            throw new ReferenceDataException("REF-CONFIG-DUPLICATE-VALUE", 409, "value");
        ValidateMandatory(settings, children);
        return settings;
    }

    private static void ValidateMandatory(ConfigurationKeySettings settings, IReadOnlyList<ConfigurationKeyMultiValue> children)
    {
        if (settings.IsMandatory && (settings.ValueMode == ConfigurationValueMode.Single
                ? settings.Value is null && settings.DefaultValue is null : !children.Any(item => item.Settings.Enabled && !item.IsDeleted)))
            throw new ReferenceDataException("REF-CONFIG-MANDATORY-VALUE-MISSING", 422, "value");
    }

    public static ConfigurationKey Restore(ConfigurationEntityState stamp, Guid domainId, string nId, ConfigurationKeySettings settings, IReadOnlyList<ConfigurationKeyMultiValue> children, bool hasHadValue)
    {
        // Restore lifecycle and retained child identities without treating hydration as a mutation.
        var key = new ConfigurationKey(domainId, nId, settings)
        {
            Id = stamp.Id, IsFrozen = stamp.IsFrozen, IsLocked = stamp.IsLocked, IsDeleted = stamp.IsDeleted,
            CreatedOn = stamp.CreatedOn, LastUpdatedOn = stamp.LastUpdatedOn, OptimisticVersion = stamp.OptimisticVersion,
            ConcurrencyVersion = stamp.ConcurrencyVersion, HasHadValue = hasHadValue,
        };
        key.values.AddRange(children); return key;
    }
}

public sealed class ConfigurationKeyMultiValue : Entity
{
    public Guid ConfigurationKeyId { get; }
    public string NId { get; }
    public ConfigurationValueSettings Settings { get; private set; }
    internal ConfigurationKeyMultiValue(Guid keyId, string nId, ConfigurationValueSettings settings)
    {
        ConfigurationKeyId = keyId; NId = ParameterValidation.NId(nId); Settings = Validate(settings);
    }
    internal bool Update(string nId, ConfigurationValueSettings settings)
    {
        EnsureCanModify();
        if (NId != ParameterValidation.NId(nId)) throw new ReferenceDataException("REF-CONFIG-NID-IMMUTABLE", 409, "nId");
        settings = Validate(settings);
        if (Settings == settings) return false;
        Settings = settings; Touch(); return true;
    }
    private static ConfigurationValueSettings Validate(ConfigurationValueSettings settings)
    {
        if (settings.Value is null || settings.Sort < 0 || settings.Name?.Length > 200) throw new ReferenceDataException("REF-VALIDATION-FAILED");
        if (settings.IsDefault && !settings.Enabled) throw new ReferenceDataException("REF-CONFIG-DEFAULT-MUST-BE-ENABLED", 409, "isDefault");
        return settings with { Name = settings.Name?.Trim() };
    }
    public static ConfigurationKeyMultiValue Restore(ConfigurationEntityState stamp, Guid keyId, string nId, ConfigurationValueSettings settings) => new(keyId, nId, settings)
    {
        Id = stamp.Id, IsFrozen = stamp.IsFrozen, IsLocked = stamp.IsLocked, IsDeleted = stamp.IsDeleted,
        CreatedOn = stamp.CreatedOn, LastUpdatedOn = stamp.LastUpdatedOn, OptimisticVersion = stamp.OptimisticVersion, ConcurrencyVersion = stamp.ConcurrencyVersion,
    };
}
