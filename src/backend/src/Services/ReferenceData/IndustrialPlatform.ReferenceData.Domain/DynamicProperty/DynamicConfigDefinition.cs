using System.Text.RegularExpressions;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.ReferenceData.Domain.DynamicProperty;

public sealed record DynamicFieldSettings(string Name, ReferenceDataType DataType, bool Required, bool Enabled, int Sort,
    ReferenceScalar? DefaultValue, int? MinLength, int? MaxLength, decimal? MinValue, decimal? MaxValue, int? Scale,
    string? Pattern, string? DictionaryNId, string? ReferenceTarget, string? Description);
public sealed record DynamicFieldInput(string NId, DynamicFieldSettings Settings);
public sealed record DynamicValueInput(string FieldNId, ReferenceScalar Value);
public sealed record DynamicRecordSettings(string? Name, string? Category, int Sort, bool Enabled);
public sealed record DynamicEntityState(Guid Id, bool IsFrozen, bool IsLocked, bool IsDeleted, DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn, long OptimisticVersion, Guid ConcurrencyVersion)
{
    public static DynamicEntityState From(Entity entity) => new(entity.Id, entity.IsFrozen, entity.IsLocked, entity.IsDeleted,
        entity.CreatedOn, entity.LastUpdatedOn, entity.OptimisticVersion, entity.ConcurrencyVersion);
}
public sealed record DynamicDefinitionState(DynamicEntityState Entity, string NId, string Name, string? Description,
    ReferenceScopeType ScopeType, string? TenantNId, int Revision, PublicationStatus Status, DateTimeOffset? PublishedOn, string? PublishedBy);

public sealed class DynamicConfigDefinition : AggregateRoot
{
    private List<DynamicConfigFieldDefinition> fields = [];
    private readonly List<DynamicConfigRecord> records = [];
    public string NId { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public ReferenceScopeType ScopeType { get; }
    public string? TenantNId { get; }
    public int Revision { get; private set; } = 1;
    public PublicationStatus Status { get; private set; } = PublicationStatus.Draft;
    public DateTimeOffset? PublishedOn { get; private set; }
    public string? PublishedBy { get; private set; }
    public IReadOnlyList<DynamicConfigFieldDefinition> Fields => fields.AsReadOnly();
    public IReadOnlyList<DynamicConfigRecord> Records => records.AsReadOnly();

    public DynamicConfigDefinition(string nId, string name, string? description, ReferenceScopeType scopeType, string? tenantNId, string? scopeId)
    {
        ReferenceValidation.Scope(scopeType, tenantNId, scopeId);
        NId = ReferenceValidation.NId(nId); Name = ReferenceValidation.Name(name); Description = ReferenceValidation.Description(description);
        ScopeType = scopeType; TenantNId = tenantNId;
    }

    public void CheckVersion(long version, Guid token)
    {
        if (OptimisticVersion != version || ConcurrencyVersion != token) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    public void Update(string name, string? description, IReadOnlyList<DynamicFieldInput> inputs)
    {
        EnsureDraft();
        name = ReferenceValidation.Name(name); description = ReferenceValidation.Description(description);
        if (inputs is null || inputs.Count > 100) throw Limit("fields");
        var next = new List<DynamicConfigFieldDefinition>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            if (input is null) throw Invalid($"fields[{index}]");
            try
            {
                var nId = ReferenceValidation.NId(input.NId, item: true);
                var old = fields.Find(item => item.NId == nId);
                next.Add(old is null ? new(Id, nId, input.Settings) : old.WithSettings(input.Settings));
            }
            catch (ReferenceDataException error) { throw new ReferenceDataException(error.ErrorCode, error.Status, $"fields[{index}].{error.Field ?? "nId"}"); }
        }
        if (next.Select(item => item.NId).Distinct(StringComparer.Ordinal).Count() != next.Count) throw Invalid("fields");
        if (fields.Any(old => (old.HasHadValue || old.WasPublished) && next.All(item => item.NId != old.NId))) throw Invalid("fields");
        foreach (var removed in fields.Where(old => next.All(item => item.Id != old.Id))) removed.CheckRemoval();
        foreach (var record in records) ValidateRecord(next, record.Settings, record.Values);
        next = next.OrderBy(item => item.Settings.Sort).ThenBy(item => item.NId, StringComparer.Ordinal).ToList();
        if (Name == name && Description == description && fields.SequenceEqual(next)) return;
        Name = name; Description = description; fields = next; Touch();
    }

    public DynamicConfigRecord AddRecord(string nId, DynamicRecordSettings settings, IReadOnlyList<DynamicValueInput> inputs)
    {
        EnsureDraft();
        if (records.Count >= 10000) throw Limit("records");
        nId = ReferenceValidation.NId(nId, item: true);
        if (records.Any(item => item.NId == nId)) throw Invalid("nId");
        var record = new DynamicConfigRecord(Id, nId, settings);
        var values = BuildValues(record, inputs, true);
        ValidateRecord(fields, record.Settings, values); CheckValueLimit(values.Count);
        var usedFields = fields.Where(field => values.Any(value => value.DynamicConfigFieldDefinitionId == field.Id)).ToArray();
        foreach (var field in usedFields) field.CheckUsageChange();
        record.SetInitialValues(values);
        foreach (var field in usedFields) field.MarkUsed();
        records.Add(record); Touch(); return record;
    }

    public DynamicConfigRecord UpdateRecord(Guid recordId, string nId, DynamicRecordSettings settings, IReadOnlyList<DynamicValueInput> inputs)
    {
        EnsureDraft();
        var old = Record(recordId);
        if (old.NId != ReferenceValidation.NId(nId, item: true)) throw Invalid("nId");
        var values = BuildValues(old, inputs, false);
        foreach (var removed in old.Values.Where(value => values.All(item => item.Id != value.Id))) removed.CheckRemoval();
        var next = old.WithValues(settings, values);
        ValidateRecord(fields, next.Settings, values); CheckValueLimit(values.Count - old.Values.Count);
        if (ReferenceEquals(old, next)) return old;
        var usedFields = fields.Where(field => values.Any(value => value.DynamicConfigFieldDefinitionId == field.Id)).ToArray();
        foreach (var field in usedFields) field.CheckUsageChange();
        foreach (var field in usedFields) field.MarkUsed();
        records[records.IndexOf(old)] = next; Touch(); return next;
    }

    public DynamicConfigRecord DisableRecord(Guid recordId)
    {
        var old = Record(recordId);
        return UpdateRecord(recordId, old.NId, old.Settings with { Enabled = false }, old.Values.Select(value => new DynamicValueInput(value.FieldNId, value.Value)).ToArray());
    }

    public DynamicConfigRecord Record(Guid id) => records.Find(item => item.Id == id && !item.IsDeleted) ?? throw new ReferenceDataException("REF-DYNAMIC-CONFIG-NOT-FOUND", 404);

    public void CheckPublication()
    {
        EnsureDraft();
        if (!fields.Any(item => item.Settings.Enabled && !item.IsDeleted) || !records.Any(item => item.Settings.Enabled && !item.IsDeleted)) throw Invalid("records");
        CheckValueLimit(0);
        foreach (var record in records) ValidateRecord(fields, record.Settings, record.Values);
        foreach (var field in fields) field.CheckPublicationChange();
    }

    public void Publish(string userNId)
    {
        CheckPublication();
        foreach (var field in fields) field.MarkPublished();
        Status = PublicationStatus.Published; PublishedBy = userNId; PublishedOn = DateTimeOffset.UtcNow; Touch();
    }
    public void Supersede()
    {
        EnsureCanModify();
        CheckCapacity(this);
        if (Status != PublicationStatus.Published) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        Status = PublicationStatus.Superseded; Touch();
    }
    public void Disable()
    {
        EnsureCanModify();
        CheckCapacity(this);
        if (Status is not (PublicationStatus.Draft or PublicationStatus.Published)) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        Status = PublicationStatus.Disabled; Touch();
    }

    public DynamicConfigDefinition Clone(int revision)
    {
        EnsureCanModify();
        if (PublishedOn is null || revision <= Revision) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        var clone = new DynamicConfigDefinition(NId, Name, Description, ScopeType, TenantNId, null) { Revision = revision };
        clone.fields = fields.Select(field => field.Clone(clone.Id)).ToList();
        foreach (var record in records)
        {
            var next = new DynamicConfigRecord(clone.Id, record.NId, record.Settings);
            next.SetInitialValues(record.Values.Select(value => new DynamicConfigFieldValue(clone.Id, next.Id,
                clone.fields.Single(field => field.NId == value.FieldNId).Id, value.FieldNId, value.Value)).ToArray());
            clone.records.Add(next);
        }
        return clone;
    }

    private List<DynamicConfigFieldValue> BuildValues(DynamicConfigRecord record, IReadOnlyList<DynamicValueInput> inputs, bool expandDefaults)
    {
        if (inputs is null || inputs.Count > 100) throw Limit("values");
        var values = new Dictionary<string, ReferenceScalar>(StringComparer.Ordinal);
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            if (input is null || input.Value is null) throw Invalid($"values[{index}]");
            var nId = ReferenceValidation.NId(input.FieldNId, item: true);
            if (!values.TryAdd(nId, input.Value) || fields.All(field => field.NId != nId)) throw Invalid($"values[{index}].fieldNId");
        }
        if (expandDefaults)
            foreach (var field in fields.Where(item => item.Settings.Enabled && item.Settings.DefaultValue is not null)) values.TryAdd(field.NId, field.Settings.DefaultValue!);
        return values.OrderBy(item => item.Key, StringComparer.Ordinal).Select(pair =>
        {
            var field = fields.Single(item => item.NId == pair.Key); field.ValidateValue(pair.Value);
            var old = record.Values.FirstOrDefault(value => value.DynamicConfigFieldDefinitionId == field.Id);
            return old?.WithValue(pair.Value) ?? new DynamicConfigFieldValue(Id, record.Id, field.Id, field.NId, pair.Value);
        }).ToList();
    }

    private static void ValidateRecord(IReadOnlyList<DynamicConfigFieldDefinition> schema, DynamicRecordSettings settings, IReadOnlyList<DynamicConfigFieldValue> values)
    {
        foreach (var value in values)
        {
            var field = schema.FirstOrDefault(item => item.Id == value.DynamicConfigFieldDefinitionId) ?? throw Invalid("fields");
            field.ValidateValue(value.Value);
        }
        if (settings.Enabled && schema.Any(field => field.Settings.Enabled && field.Settings.Required && !values.Any(value => value.DynamicConfigFieldDefinitionId == field.Id)))
            throw Invalid("values");
    }
    private void CheckValueLimit(int added) { if (records.Sum(record => (long)record.Values.Count) + added > 200000) throw Limit("values"); }
    private void EnsureDraft()
    {
        EnsureCanModify();
        if (Status != PublicationStatus.Draft) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        CheckCapacity(this);
    }
    internal static void CheckCapacity(Entity entity)
    {
        if (entity.OptimisticVersion == long.MaxValue) throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }
    internal static ReferenceDataException Invalid(string field) => new("REF-DYNAMIC-CONFIG-FIELD-INVALID", 422, field);
    private static ReferenceDataException Limit(string field) => new("REF-DYNAMIC-CONFIG-LIMIT-EXCEEDED", 422, field);

    public static DynamicConfigDefinition Restore(DynamicDefinitionState state, IReadOnlyList<DynamicConfigFieldDefinition> restoredFields, IReadOnlyList<DynamicConfigRecord> restoredRecords)
    {
        var stamp = state.Entity;
        var definition = new DynamicConfigDefinition(state.NId, state.Name, state.Description, state.ScopeType, state.TenantNId, null)
        {
            Id = stamp.Id, IsFrozen = stamp.IsFrozen, IsLocked = stamp.IsLocked, IsDeleted = stamp.IsDeleted, CreatedOn = stamp.CreatedOn,
            LastUpdatedOn = stamp.LastUpdatedOn, OptimisticVersion = stamp.OptimisticVersion, ConcurrencyVersion = stamp.ConcurrencyVersion,
            Revision = state.Revision, Status = state.Status, PublishedOn = state.PublishedOn, PublishedBy = state.PublishedBy,
            fields = restoredFields.OrderBy(field => field.Settings.Sort).ThenBy(field => field.NId, StringComparer.Ordinal).ToList(),
        };
        definition.records.AddRange(restoredRecords); return definition;
    }
}

public sealed class DynamicConfigFieldDefinition : Entity
{
    private readonly Regex? regex;
    public Guid DynamicConfigDefinitionId { get; }
    public string NId { get; }
    public DynamicFieldSettings Settings { get; }
    public bool HasHadValue { get; private set; }
    public bool WasPublished { get; private set; }
    internal DynamicConfigFieldDefinition(Guid definitionId, string nId, DynamicFieldSettings settings)
    {
        DynamicConfigDefinitionId = definitionId; NId = ReferenceValidation.NId(nId, item: true); Settings = Validate(settings);
        if (Settings.Pattern is not null)
            try { regex = new Regex(Settings.Pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException) { throw DynamicConfigDefinition.Invalid("pattern"); }
        if (Settings.DefaultValue is not null) { ValidateValue(Settings.DefaultValue); HasHadValue = true; }
    }
    private static DynamicFieldSettings Validate(DynamicFieldSettings settings)
    {
        if (settings is null || !Enum.IsDefined(settings.DataType) || settings.Sort < 0) throw DynamicConfigDefinition.Invalid("fields");
        settings = settings with { Name = ReferenceValidation.Name(settings.Name), Description = ReferenceValidation.Description(settings.Description) };
        if (settings.MinLength is < 0 || settings.MaxLength is < 0 || settings.MinLength > settings.MaxLength || settings.MinValue > settings.MaxValue || settings.Scale is < 0 or > 10 || settings.Pattern?.Length > 256)
            throw DynamicConfigDefinition.Invalid("fields");
        if (settings.DataType != ReferenceDataType.String && (settings.MinLength is not null || settings.MaxLength is not null || settings.Pattern is not null)) throw DynamicConfigDefinition.Invalid("fields");
        if (settings.DataType is not (ReferenceDataType.Integer or ReferenceDataType.Decimal) && (settings.MinValue is not null || settings.MaxValue is not null)) throw DynamicConfigDefinition.Invalid("fields");
        if (settings.DataType != ReferenceDataType.Decimal && settings.Scale is not null) throw DynamicConfigDefinition.Invalid("scale");
        if (settings.DataType == ReferenceDataType.Enum) settings = settings with { DictionaryNId = ReferenceValidation.NId(settings.DictionaryNId!) };
        else if (settings.DictionaryNId is not null) throw DynamicConfigDefinition.Invalid("dictionaryNId");
        if (settings.DataType == ReferenceDataType.Reference)
        {
            if (string.IsNullOrWhiteSpace(settings.ReferenceTarget) || settings.ReferenceTarget.Length > 128) throw DynamicConfigDefinition.Invalid("referenceTarget");
        }
        else if (settings.ReferenceTarget is not null) throw DynamicConfigDefinition.Invalid("referenceTarget");
        return settings;
    }
    public void ValidateValue(ReferenceScalar value)
    {
        if (value.DataType != Settings.DataType) throw DynamicConfigDefinition.Invalid("value");
        var json = value.JsonValue;
        if (Settings.DataType == ReferenceDataType.String)
        {
            var text = json.GetString()!;
            if (text.Length < Settings.MinLength || text.Length > Settings.MaxLength) throw DynamicConfigDefinition.Invalid("value");
            try { if (regex is not null && !regex.IsMatch(text)) throw DynamicConfigDefinition.Invalid("value"); }
            catch (RegexMatchTimeoutException) { throw DynamicConfigDefinition.Invalid("pattern"); }
        }
        if (Settings.DataType is ReferenceDataType.Integer or ReferenceDataType.Decimal)
        {
            var number = json.GetDecimal();
            if (number < Settings.MinValue || number > Settings.MaxValue) throw DynamicConfigDefinition.Invalid("value");
            if (Settings.Scale is int scale && decimal.Round(number, scale) != number) throw DynamicConfigDefinition.Invalid("scale");
        }
    }
    internal DynamicConfigFieldDefinition WithSettings(DynamicFieldSettings settings)
    {
        var next = new DynamicConfigFieldDefinition(DynamicConfigDefinitionId, NId, settings);
        if (Settings == next.Settings) return this;
        EnsureCanModify();
        DynamicConfigDefinition.CheckCapacity(this);
        if ((HasHadValue || WasPublished) && Settings.DataType != next.Settings.DataType) throw DynamicConfigDefinition.Invalid("dataType");
        next = Restore(DynamicEntityState.From(this), DynamicConfigDefinitionId, NId, next.Settings, HasHadValue || next.HasHadValue, WasPublished);
        next.Touch(); return next;
    }
    internal void CheckRemoval() => EnsureCanModify();
    internal void CheckUsageChange() { if (!HasHadValue) { EnsureCanModify(); DynamicConfigDefinition.CheckCapacity(this); } }
    internal void MarkUsed() { if (!HasHadValue) { CheckUsageChange(); HasHadValue = true; Touch(); } }
    internal void CheckPublicationChange() { if (!WasPublished) { EnsureCanModify(); DynamicConfigDefinition.CheckCapacity(this); } }
    internal void MarkPublished() { if (!WasPublished) { CheckPublicationChange(); WasPublished = true; Touch(); } }
    internal DynamicConfigFieldDefinition Clone(Guid definitionId) => new(definitionId, NId, Settings) { HasHadValue = HasHadValue, WasPublished = WasPublished };
    public static DynamicConfigFieldDefinition Restore(DynamicEntityState stamp, Guid definitionId, string nId, DynamicFieldSettings settings, bool hasHadValue, bool wasPublished) => new(definitionId, nId, settings)
    {
        Id = stamp.Id, IsFrozen = stamp.IsFrozen, IsLocked = stamp.IsLocked, IsDeleted = stamp.IsDeleted, CreatedOn = stamp.CreatedOn,
        LastUpdatedOn = stamp.LastUpdatedOn, OptimisticVersion = stamp.OptimisticVersion, ConcurrencyVersion = stamp.ConcurrencyVersion, HasHadValue = hasHadValue, WasPublished = wasPublished,
    };
}

public sealed class DynamicConfigRecord : Entity
{
    public Guid DynamicConfigDefinitionId { get; }
    public string NId { get; }
    public DynamicRecordSettings Settings { get; }
    public IReadOnlyList<DynamicConfigFieldValue> Values { get; private set; } = [];
    internal DynamicConfigRecord(Guid definitionId, string nId, DynamicRecordSettings settings)
    {
        if (settings.Name?.Length > 200 || settings.Category?.Length > 100 || settings.Sort < 0) throw DynamicConfigDefinition.Invalid("records");
        DynamicConfigDefinitionId = definitionId; NId = ReferenceValidation.NId(nId, item: true);
        Settings = settings with { Name = settings.Name?.Trim(), Category = settings.Category?.Trim() };
    }
    internal void SetInitialValues(IReadOnlyList<DynamicConfigFieldValue> values) => Values = Array.AsReadOnly(values.ToArray());
    internal DynamicConfigRecord WithValues(DynamicRecordSettings settings, IReadOnlyList<DynamicConfigFieldValue> values)
    {
        EnsureCanModify();
        var next = Restore(DynamicEntityState.From(this), DynamicConfigDefinitionId, NId, settings, values);
        if (Settings == next.Settings && Values.SequenceEqual(next.Values)) return this;
        DynamicConfigDefinition.CheckCapacity(this);
        next.Touch(); return next;
    }
    public static DynamicConfigRecord Restore(DynamicEntityState stamp, Guid definitionId, string nId, DynamicRecordSettings settings, IReadOnlyList<DynamicConfigFieldValue> values) => new(definitionId, nId, settings)
    {
        Id = stamp.Id, IsFrozen = stamp.IsFrozen, IsLocked = stamp.IsLocked, IsDeleted = stamp.IsDeleted, CreatedOn = stamp.CreatedOn,
        LastUpdatedOn = stamp.LastUpdatedOn, OptimisticVersion = stamp.OptimisticVersion, ConcurrencyVersion = stamp.ConcurrencyVersion,
        Values = Array.AsReadOnly(values.OrderBy(value => value.FieldNId, StringComparer.Ordinal).ToArray()),
    };
}

public sealed class DynamicConfigFieldValue : Entity
{
    public Guid DynamicConfigDefinitionId { get; }
    public Guid DynamicConfigRecordId { get; }
    public Guid DynamicConfigFieldDefinitionId { get; }
    public string FieldNId { get; }
    public ReferenceScalar Value { get; }
    internal DynamicConfigFieldValue(Guid definitionId, Guid recordId, Guid fieldId, string fieldNId, ReferenceScalar value)
    {
        DynamicConfigDefinitionId = definitionId; DynamicConfigRecordId = recordId; DynamicConfigFieldDefinitionId = fieldId; FieldNId = fieldNId; Value = value;
    }
    internal DynamicConfigFieldValue WithValue(ReferenceScalar value)
    {
        if (value == Value) return this;
        EnsureCanModify();
        DynamicConfigDefinition.CheckCapacity(this);
        var next = Restore(DynamicEntityState.From(this), DynamicConfigDefinitionId, DynamicConfigRecordId, DynamicConfigFieldDefinitionId, FieldNId, value);
        next.Touch(); return next;
    }
    internal void CheckRemoval() => EnsureCanModify();
    public static DynamicConfigFieldValue Restore(DynamicEntityState stamp, Guid definitionId, Guid recordId, Guid fieldId, string fieldNId, ReferenceScalar value) => new(definitionId, recordId, fieldId, fieldNId, value)
    {
        Id = stamp.Id, IsFrozen = stamp.IsFrozen, IsLocked = stamp.IsLocked, IsDeleted = stamp.IsDeleted, CreatedOn = stamp.CreatedOn,
        LastUpdatedOn = stamp.LastUpdatedOn, OptimisticVersion = stamp.OptimisticVersion, ConcurrencyVersion = stamp.ConcurrencyVersion,
    };
}
