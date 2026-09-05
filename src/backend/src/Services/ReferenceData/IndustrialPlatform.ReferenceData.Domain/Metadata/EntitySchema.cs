using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.ReferenceData.Domain.Metadata;

public sealed record MetadataAttributeSettings(
    string Name,
    ReferenceDataType DataType,
    bool Required,
    bool IsArray,
    bool Enabled,
    int Sort,
    string? DefaultValue,
    int? MinLength,
    int? MaxLength,
    decimal? MinValue,
    decimal? MaxValue,
    string? Pattern,
    string? DictionaryNId,
    string? ReferenceTarget,
    int? Precision,
    int? Scale,
    string? UnitDimensionNId,
    string? DefaultUnitNId,
    int? UnitRevision,
    ReferenceScopeType? UnitSourceScope,
    string? UnitSourceTenantNId,
    string? Description);

public sealed record MetadataAttributeInput(string NId, MetadataAttributeSettings Settings);

public sealed record MetadataEntityState(
    Guid Id,
    bool IsFrozen,
    bool IsLocked,
    bool IsDeleted,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    long OptimisticVersion,
    Guid ConcurrencyVersion)
{
    public static MetadataEntityState From(Entity entity) => new(entity.Id, entity.IsFrozen, entity.IsLocked,
        entity.IsDeleted, entity.CreatedOn, entity.LastUpdatedOn, entity.OptimisticVersion, entity.ConcurrencyVersion);
}

public sealed record EntitySchemaState(
    MetadataEntityState Entity,
    string NId,
    string Name,
    string? Description,
    ReferenceScopeType ScopeType,
    string? TenantNId,
    int Revision,
    PublicationStatus Status,
    int? SourceRevision,
    DateTimeOffset? PublishedOn,
    string? PublishedBy);

public sealed class EntitySchema : AggregateRoot
{
    private List<AttributeDefinition> attributes = [];

    public string NId { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public ReferenceScopeType ScopeType { get; }
    public string? TenantNId { get; }
    public int Revision { get; private set; } = 1;
    public PublicationStatus Status { get; private set; } = PublicationStatus.Draft;
    public int? SourceRevision { get; private set; }
    public DateTimeOffset? PublishedOn { get; private set; }
    public string? PublishedBy { get; private set; }
    public IReadOnlyList<AttributeDefinition> Attributes => attributes.Select(Snapshot).ToArray();

    public EntitySchema(string nId, string name, string? description, ReferenceScopeType scopeType,
        string? tenantNId, string? scopeId)
    {
        ReferenceValidation.Scope(scopeType, tenantNId, scopeId);
        NId = ReferenceValidation.NId(nId);
        Name = ReferenceValidation.Name(name);
        Description = ReferenceValidation.Description(description);
        ScopeType = scopeType;
        TenantNId = tenantNId;
    }

    public void Update(string name, string? description, IReadOnlyList<MetadataAttributeInput> inputs)
    {
        EnsureDraft();
        if (inputs is null || inputs.Count > 200) Invalid("attributes");
        var next = new List<AttributeDefinition>(inputs.Count);
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            if (input is null) Invalid($"attributes[{index}]");
            try
            {
                var nId = ReferenceValidation.NId(input!.NId, item: true);
                var current = attributes.Find(attribute => attribute.NId == nId);
                var attribute = current is null
                    ? new AttributeDefinition(Id, nId, input.Settings)
                    : current.WithSettings(input.Settings);
                attribute.ValidateUnitSource(ScopeType, TenantNId);
                next.Add(attribute);
            }
            catch (ReferenceDataException error)
            {
                throw new ReferenceDataException(error.ErrorCode, error.Status,
                    $"attributes[{index}].{error.Field ?? "nId"}");
            }
        }
        if (next.Select(attribute => attribute.NId).Distinct(StringComparer.Ordinal).Count() != next.Count)
            Invalid("attributes");
        if (attributes.Any(old => old.WasPublished && next.All(attribute => attribute.NId != old.NId)))
            Invalid("attributes");

        var nextName = ReferenceValidation.Name(name);
        var nextDescription = ReferenceValidation.Description(description);
        next = next.OrderBy(attribute => attribute.Settings.Sort)
            .ThenBy(attribute => attribute.NId, StringComparer.Ordinal).ToList();
        if (Name == nextName && Description == nextDescription && attributes.SequenceEqual(next)) return;
        EnsureCapacity();
        Name = nextName;
        Description = nextDescription;
        attributes = next;
        Touch();
    }

    public void CheckVersion(long expectedOptimisticVersion, Guid expectedConcurrencyVersion)
    {
        if (OptimisticVersion != expectedOptimisticVersion || ConcurrencyVersion != expectedConcurrencyVersion)
            throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    public void CheckPublication()
    {
        EnsureDraft();
        if (!attributes.Any(attribute => attribute.Settings.Enabled && !attribute.IsDeleted)) Invalid("attributes");
    }

    public void Publish(string userNId)
    {
        CheckPublication();
        if (string.IsNullOrWhiteSpace(userNId)) Invalid("userNId");
        EnsureCapacity();
        foreach (var attribute in attributes) attribute.EnsureCanMarkPublished();
        foreach (var attribute in attributes) attribute.MarkPublished();
        Status = PublicationStatus.Published;
        PublishedOn = DateTimeOffset.UtcNow;
        PublishedBy = userNId;
        Touch();
    }

    public void Supersede()
    {
        EnsureMutable();
        EnsureCapacity();
        if (Status != PublicationStatus.Published) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        Status = PublicationStatus.Superseded;
        Touch();
    }

    public void Disable()
    {
        EnsureMutable();
        EnsureCapacity();
        if (Status is not (PublicationStatus.Draft or PublicationStatus.Published))
            throw new ReferenceDataException("REF-INVALID-STATE", 409);
        Status = PublicationStatus.Disabled;
        Touch();
    }

    public EntitySchema Clone(int revision)
    {
        EnsureMutable();
        if (PublishedOn is null || revision <= Revision) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        var clone = new EntitySchema(NId, Name, Description, ScopeType, TenantNId, null)
        {
            Revision = revision,
            SourceRevision = Revision,
        };
        clone.attributes = attributes.Select(attribute => attribute.Clone(clone.Id)).ToList();
        return clone;
    }

    public new void Freeze() { if (!IsFrozen) EnsureCapacity(); base.Freeze(); }
    public new void Unfreeze() { if (IsFrozen) EnsureCapacity(); base.Unfreeze(); }
    public new void Lock() { if (!IsLocked) EnsureCapacity(); base.Lock(); }
    public new void Unlock() { if (IsLocked) EnsureCapacity(); base.Unlock(); }
    public new void MarkDeleted() { if (!IsDeleted) EnsureCapacity(); base.MarkDeleted(); }
    public new void Restore() { if (IsDeleted) EnsureCapacity(); base.Restore(); }

    private void EnsureDraft()
    {
        EnsureMutable();
        if (Status != PublicationStatus.Draft) throw new ReferenceDataException("REF-INVALID-STATE", 409);
    }

    private void EnsureMutable()
    {
        if (IsDeleted || IsFrozen || IsLocked) throw new ReferenceDataException("REF-INVALID-STATE", 409);
    }

    private void EnsureCapacity()
    {
        if (OptimisticVersion == long.MaxValue)
            throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    private static AttributeDefinition Snapshot(AttributeDefinition attribute) => AttributeDefinition.Restore(
        MetadataEntityState.From(attribute), attribute.EntitySchemaId, attribute.NId, attribute.Settings,
        attribute.WasPublished);

    [DoesNotReturn]
    internal static void Invalid(string field) =>
        throw new ReferenceDataException("REF-VALIDATION-FAILED", 400, field);

    public static EntitySchema Restore(EntitySchemaState state, IReadOnlyList<AttributeDefinition> restoredAttributes)
    {
        var stamp = state.Entity;
        return new EntitySchema(state.NId, state.Name, state.Description, state.ScopeType, state.TenantNId, null)
        {
            Id = stamp.Id,
            IsFrozen = stamp.IsFrozen,
            IsLocked = stamp.IsLocked,
            IsDeleted = stamp.IsDeleted,
            CreatedOn = stamp.CreatedOn,
            LastUpdatedOn = stamp.LastUpdatedOn,
            OptimisticVersion = stamp.OptimisticVersion,
            ConcurrencyVersion = stamp.ConcurrencyVersion,
            Revision = state.Revision,
            Status = state.Status,
            SourceRevision = state.SourceRevision,
            PublishedOn = state.PublishedOn,
            PublishedBy = state.PublishedBy,
            attributes = restoredAttributes.Select(Snapshot).OrderBy(attribute => attribute.Settings.Sort)
                .ThenBy(attribute => attribute.NId, StringComparer.Ordinal).ToList(),
        };
    }
}

public sealed class AttributeDefinition : Entity
{
    private const decimal NumericMaximum = 9999999999999999.999999999999m;
    private readonly Regex? regex;

    public Guid EntitySchemaId { get; }
    public string NId { get; }
    public MetadataAttributeSettings Settings { get; }
    public bool WasPublished { get; private set; }

    internal AttributeDefinition(Guid entitySchemaId, string nId, MetadataAttributeSettings settings)
    {
        EntitySchemaId = entitySchemaId;
        NId = ReferenceValidation.NId(nId, item: true);
        Settings = Validate(settings);
        if (Settings.Pattern is not null)
        {
            try
            {
                regex = new Regex(Settings.Pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(50));
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException)
            {
                EntitySchema.Invalid("pattern");
            }
        }
        ValidateDefault();
    }

    internal AttributeDefinition WithSettings(MetadataAttributeSettings settings)
    {
        var candidate = new AttributeDefinition(EntitySchemaId, NId, settings);
        if (candidate.Settings == Settings) return this;
        EnsureMutable();
        EnsureCapacity();
        candidate = Restore(MetadataEntityState.From(this), EntitySchemaId, NId, candidate.Settings, WasPublished);
        candidate.Touch();
        return candidate;
    }

    internal void ValidateUnitSource(ReferenceScopeType schemaScope, string? schemaTenantNId)
    {
        if (Settings.UnitSourceScope == ReferenceScopeType.Tenant
            && (schemaScope != ReferenceScopeType.Tenant
                || !string.Equals(Settings.UnitSourceTenantNId, schemaTenantNId, StringComparison.Ordinal)))
            EntitySchema.Invalid("unitSourceTenantNId");
        if (schemaScope == ReferenceScopeType.Platform && Settings.UnitSourceScope == ReferenceScopeType.Tenant)
            EntitySchema.Invalid("unitSourceScope");
    }

    internal void MarkPublished()
    {
        if (WasPublished) return;
        WasPublished = true;
        Touch();
    }

    internal void EnsureCanMarkPublished()
    {
        if (WasPublished) return;
        EnsureMutable();
        EnsureCapacity();
    }

    internal AttributeDefinition Clone(Guid entitySchemaId) =>
        new(entitySchemaId, NId, Settings) { WasPublished = WasPublished };

    public static AttributeDefinition Restore(MetadataEntityState stamp, Guid entitySchemaId, string nId,
        MetadataAttributeSettings settings, bool wasPublished) => new(entitySchemaId, nId, settings)
    {
        Id = stamp.Id,
        IsFrozen = stamp.IsFrozen,
        IsLocked = stamp.IsLocked,
        IsDeleted = stamp.IsDeleted,
        CreatedOn = stamp.CreatedOn,
        LastUpdatedOn = stamp.LastUpdatedOn,
        OptimisticVersion = stamp.OptimisticVersion,
        ConcurrencyVersion = stamp.ConcurrencyVersion,
        WasPublished = wasPublished,
    };

    private void EnsureMutable()
    {
        if (IsDeleted || IsFrozen || IsLocked) throw new ReferenceDataException("REF-INVALID-STATE", 409);
    }

    private void EnsureCapacity()
    {
        if (OptimisticVersion == long.MaxValue)
            throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    private static MetadataAttributeSettings Validate(MetadataAttributeSettings settings)
    {
        if (settings is null || !Enum.IsDefined(settings.DataType) || settings.DataType == ReferenceDataType.Json
            || settings.Sort < 0 || settings.Pattern?.Length > 256)
            EntitySchema.Invalid("attributes");
        settings = settings with
        {
            Name = ReferenceValidation.Name(settings.Name),
            Description = ReferenceValidation.Description(settings.Description),
        };
        if (settings.MinLength is < 0 || settings.MaxLength is < 0 || settings.MinLength > settings.MaxLength
            || settings.MinValue > settings.MaxValue
            || settings.MinValue is not null && decimal.Abs(settings.MinValue.Value) > NumericMaximum
            || settings.MaxValue is not null && decimal.Abs(settings.MaxValue.Value) > NumericMaximum
            || settings.MinValue is decimal minimumValue && ExceedsStorageShape(minimumValue)
            || settings.MaxValue is decimal maximumValue && ExceedsStorageShape(maximumValue))
            EntitySchema.Invalid("attributes");

        var stringConstraints = settings.MinLength is not null || settings.MaxLength is not null || settings.Pattern is not null;
        var numericConstraints = settings.MinValue is not null || settings.MaxValue is not null;
        var precisionConstraints = settings.Precision is not null || settings.Scale is not null;
        var unitConstraints = settings.UnitDimensionNId is not null || settings.DefaultUnitNId is not null
            || settings.UnitRevision is not null || settings.UnitSourceScope is not null
            || settings.UnitSourceTenantNId is not null;

        if (settings.DataType != ReferenceDataType.String && stringConstraints) EntitySchema.Invalid("attributes");
        if (settings.DataType is not (ReferenceDataType.Integer or ReferenceDataType.Decimal) && numericConstraints)
            EntitySchema.Invalid("attributes");
        if (settings.DataType != ReferenceDataType.Decimal && (precisionConstraints || unitConstraints))
            EntitySchema.Invalid("attributes");
        if (settings.DataType == ReferenceDataType.Integer
            && (settings.MinValue is decimal minimum && minimum != decimal.Truncate(minimum)
                || settings.MaxValue is decimal maximum && maximum != decimal.Truncate(maximum)))
            EntitySchema.Invalid("attributes");

        if (settings.DataType == ReferenceDataType.Decimal)
        {
            if (settings.Precision is not null && settings.Precision is < 1 or > 28
                || settings.Scale is not null && settings.Scale is < 0 or > 12
                || settings.Precision is not null && settings.Scale > settings.Precision)
                EntitySchema.Invalid("precision");
            var dimensionTuple = settings.UnitDimensionNId is not null
                && settings.UnitRevision is not null && settings.UnitSourceScope is not null;
            if (unitConstraints && !dimensionTuple || settings.UnitRevision is <= 0
                || settings.UnitSourceScope == ReferenceScopeType.Factory)
                EntitySchema.Invalid("unitDimensionNId");
            if (!dimensionTuple && (settings.DefaultUnitNId is not null || settings.UnitSourceTenantNId is not null))
                EntitySchema.Invalid("defaultUnitNId");
            if (dimensionTuple)
            {
                settings = settings with
                {
                    UnitDimensionNId = ReferenceValidation.NId(settings.UnitDimensionNId!),
                    DefaultUnitNId = settings.DefaultUnitNId is null
                        ? null : ReferenceValidation.NId(settings.DefaultUnitNId, item: true),
                };
                if (settings.UnitSourceScope == ReferenceScopeType.Platform && settings.UnitSourceTenantNId is not null
                    || settings.UnitSourceScope == ReferenceScopeType.Tenant
                    && string.IsNullOrWhiteSpace(settings.UnitSourceTenantNId))
                    EntitySchema.Invalid("unitSourceTenantNId");
            }
        }

        if (settings.DataType == ReferenceDataType.Enum)
        {
            if (string.IsNullOrWhiteSpace(settings.DictionaryNId)) EntitySchema.Invalid("dictionaryNId");
            settings = settings with
            {
                DictionaryNId = ReferenceValidation.NId(settings.DictionaryNId!),
                DefaultValue = NormalizeEnumDefault(settings.DefaultValue, settings.IsArray),
            };
        }
        else if (settings.DictionaryNId is not null) EntitySchema.Invalid("dictionaryNId");

        if (settings.DataType == ReferenceDataType.Reference)
        {
            if (string.IsNullOrWhiteSpace(settings.ReferenceTarget)) EntitySchema.Invalid("referenceTarget");
            settings = settings with { ReferenceTarget = ReferenceValidation.NId(settings.ReferenceTarget!) };
        }
        else if (settings.ReferenceTarget is not null) EntitySchema.Invalid("referenceTarget");

        return settings;
    }

    private static bool ExceedsStorageShape(decimal value)
    {
        var bits = decimal.GetBits(decimal.Abs(value));
        var scale = (bits[3] >> 16) & 0x7f;
        var coefficient = ((System.Numerics.BigInteger)(uint)bits[2] << 64)
            | ((System.Numerics.BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        var digits = coefficient.IsZero ? 1 : coefficient.ToString(CultureInfo.InvariantCulture).Length;
        return scale > 12 || digits > 28;
    }

    private static string? NormalizeEnumDefault(string? value, bool isArray)
    {
        if (value is null) return null;
        if (!isArray) return ReferenceValidation.NId(value, item: true);
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() > 1000)
                EntitySchema.Invalid("defaultValue");
            var items = new List<string>(document.RootElement.GetArrayLength());
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) EntitySchema.Invalid("defaultValue");
                items.Add(ReferenceValidation.NId(item.GetString()!, item: true));
            }
            return JsonSerializer.Serialize(items);
        }
        catch (JsonException)
        {
            EntitySchema.Invalid("defaultValue");
            return null;
        }
    }

    private void ValidateDefault()
    {
        if (Settings.DefaultValue is null) return;
        if (!Settings.IsArray)
        {
            ValidateScalar(Settings.DefaultValue);
            return;
        }
        try
        {
            using var document = JsonDocument.Parse(Settings.DefaultValue);
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() > 1000)
                EntitySchema.Invalid("defaultValue");
            foreach (var item in document.RootElement.EnumerateArray())
                ValidateJsonScalar(item);
        }
        catch (JsonException)
        {
            EntitySchema.Invalid("defaultValue");
        }
    }

    private void ValidateScalar(string value)
    {
        switch (Settings.DataType)
        {
            case ReferenceDataType.String:
            case ReferenceDataType.Date:
            case ReferenceDataType.DateTime:
            case ReferenceDataType.Enum:
            case ReferenceDataType.Reference:
                ValidateText(value);
                break;
            case ReferenceDataType.Integer:
                if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                    EntitySchema.Invalid("defaultValue");
                ValidateNumber(integer);
                break;
            case ReferenceDataType.Decimal:
                if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var number))
                    EntitySchema.Invalid("defaultValue");
                ValidateNumber(number);
                ValidatePrecision(number);
                break;
            case ReferenceDataType.Boolean:
                if (value is not ("true" or "false")) EntitySchema.Invalid("defaultValue");
                break;
            default:
                EntitySchema.Invalid("defaultValue");
                break;
        }
    }

    private void ValidateJsonScalar(JsonElement value)
    {
        switch (Settings.DataType)
        {
            case ReferenceDataType.String:
            case ReferenceDataType.Date:
            case ReferenceDataType.DateTime:
            case ReferenceDataType.Enum:
            case ReferenceDataType.Reference:
                if (value.ValueKind != JsonValueKind.String) EntitySchema.Invalid("defaultValue");
                ValidateText(value.GetString()!);
                break;
            case ReferenceDataType.Integer:
                if (!value.TryGetInt64(out var integer)) EntitySchema.Invalid("defaultValue");
                ValidateNumber(integer);
                break;
            case ReferenceDataType.Decimal:
                if (!value.TryGetDecimal(out var number)) EntitySchema.Invalid("defaultValue");
                ValidateNumber(number);
                ValidatePrecision(number);
                break;
            case ReferenceDataType.Boolean:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    EntitySchema.Invalid("defaultValue");
                break;
            default:
                EntitySchema.Invalid("defaultValue");
                break;
        }
    }

    private void ValidateText(string value)
    {
        if (value.Contains('\0')) EntitySchema.Invalid("defaultValue");
        switch (Settings.DataType)
        {
            case ReferenceDataType.String:
                if (value.Length < Settings.MinLength || value.Length > Settings.MaxLength)
                    EntitySchema.Invalid("defaultValue");
                try
                {
                    if (regex is not null && !regex.IsMatch(value)) EntitySchema.Invalid("defaultValue");
                }
                catch (RegexMatchTimeoutException) { EntitySchema.Invalid("pattern"); }
                break;
            case ReferenceDataType.Date:
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out _)) EntitySchema.Invalid("defaultValue");
                break;
            case ReferenceDataType.DateTime:
                if (!Regex.IsMatch(value,
                        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$",
                        RegexOptions.CultureInvariant)
                    || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    EntitySchema.Invalid("defaultValue");
                break;
            case ReferenceDataType.Enum:
                ReferenceValidation.NId(value, item: true);
                break;
            case ReferenceDataType.Reference:
                if (string.IsNullOrWhiteSpace(value) || value.Length > 128) EntitySchema.Invalid("defaultValue");
                break;
        }
    }

    private void ValidateNumber(decimal value)
    {
        if (decimal.Abs(value) > NumericMaximum || value < Settings.MinValue || value > Settings.MaxValue)
            EntitySchema.Invalid("defaultValue");
    }

    private void ValidatePrecision(decimal value)
    {
        var bits = decimal.GetBits(decimal.Abs(value));
        var scale = (bits[3] >> 16) & 0x7f;
        var coefficient = ((System.Numerics.BigInteger)(uint)bits[2] << 64)
            | ((System.Numerics.BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        var digits = coefficient.IsZero ? 1 : coefficient.ToString(CultureInfo.InvariantCulture).Length;
        if (Settings.Scale is int maximumScale && scale > maximumScale
            || Settings.Precision is int precision && digits > precision)
            EntitySchema.Invalid("defaultValue");
    }
}
