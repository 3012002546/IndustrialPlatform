using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure;

public enum UnitConversionKind { Ratio, AbsoluteTemperature }
public enum UnitRoundingMode { ToEven, AwayFromZero }

public sealed class UnitDimension : AggregateRoot
{
    private UnitDefinition[] units = [];
    public string NId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public ReferenceScopeType ScopeType { get; private set; }
    public string? TenantNId { get; private set; }
    public int Revision { get; private set; } = 1;
    public PublicationStatus Status { get; private set; } = PublicationStatus.Draft;
    public int? SourceRevision { get; private set; }
    public DateTimeOffset? PublishedOn { get; private set; }
    public string? PublishedBy { get; private set; }
    public bool IsSystemDefined { get; private set; }
    public UnitConversionKind ConversionKind { get; private set; } = UnitConversionKind.Ratio;
    public string BaseUnitNId { get; private set; } = string.Empty;
    public IReadOnlyList<UnitDefinition> Units => units.Select(Snapshot).ToArray();

    public UnitDimension(string nId, string name, string? description, ReferenceScopeType scope, string? tenantNId, string? scopeNId)
    {
        ReferenceValidation.Scope(scope, tenantNId, scopeNId);
        NId = ReferenceValidation.NId(nId);
        Name = ReferenceValidation.Name(name);
        Description = ReferenceValidation.Description(description);
        ScopeType = scope;
        TenantNId = tenantNId;
    }

    public void Update(string name, string? description, UnitConversionKind conversionKind, string baseUnitNId,
        IReadOnlyList<UnitDefinition> requestedUnits)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(requestedUnits);
        if (!Enum.IsDefined(conversionKind) || requestedUnits.Count is < 1 or > 200)
            Invalid("units");
        var nextName = ReferenceValidation.Name(name);
        var nextDescription = ReferenceValidation.Description(description);
        var nextBase = ReferenceValidation.NId(baseUnitNId, item: true);
        if (requestedUnits.Select(unit => unit.NId).Distinct(StringComparer.Ordinal).Count() != requestedUnits.Count)
            Invalid("units");
        if (conversionKind == UnitConversionKind.Ratio && requestedUnits.Any(unit => unit.OffsetToBase != 0m))
            Invalid("units");
        var baseUnits = requestedUnits.Where(unit => unit.NId == nextBase).ToArray();
        if (baseUnits.Length != 1 || !baseUnits[0].Enabled || baseUnits[0].FactorToBase != 1m || baseUnits[0].OffsetToBase != 0m)
            Invalid("baseUnitNId");

        var nextUnits = requestedUnits.Select(requested =>
            units.SingleOrDefault(current => current.NId == requested.NId)?.WithSettings(requested) ?? requested).ToArray();
        if (units.Any(current => nextUnits.All(next => next.NId != current.NId)
            && (current.IsFrozen || current.IsLocked)))
            throw new ReferenceDataException("REF-INVALID-STATE", 409);
        foreach (var unit in nextUnits) unit.AttachTo(Id);
        Name = nextName;
        Description = nextDescription;
        ConversionKind = conversionKind;
        BaseUnitNId = nextBase;
        units = nextUnits.OrderBy(unit => unit.Sort).ThenBy(unit => unit.NId, StringComparer.Ordinal).ToArray();
        Touch();
    }

    public void CheckVersion(long expectedOptimisticVersion, Guid expectedConcurrencyVersion)
    {
        if (OptimisticVersion != expectedOptimisticVersion || ConcurrencyVersion != expectedConcurrencyVersion)
            throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    public void Publish(string userNId)
    {
        EnsureNotSystem();
        EnsureDraft();
        ValidateConfigured();
        Status = PublicationStatus.Published;
        PublishedOn = DateTimeOffset.UtcNow;
        PublishedBy = string.IsNullOrWhiteSpace(userNId) ? throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "userNId") : userNId;
        Touch();
    }

    public void Supersede()
    {
        EnsureNotSystem();
        EnsureMutable();
        EnsureCapacity();
        if (Status != PublicationStatus.Published) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        Status = PublicationStatus.Superseded;
        Touch();
    }

    public void Disable()
    {
        EnsureNotSystem();
        EnsureMutable();
        EnsureCapacity();
        if (Status is not (PublicationStatus.Draft or PublicationStatus.Published))
            throw new ReferenceDataException("REF-INVALID-STATE", 409);
        Status = PublicationStatus.Disabled;
        Touch();
    }

    public UnitDimension Clone(int revision)
    {
        EnsureNotSystem();
        EnsureMutable();
        if (PublishedOn is null || revision <= Revision) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        var clone = new UnitDimension(NId, Name, Description, ScopeType, TenantNId, null)
        {
            Revision = revision,
            SourceRevision = Revision,
        };
        clone.Update(Name, Description, ConversionKind, BaseUnitNId,
            units.Select(unit => new UnitDefinition(unit.NId, unit.Name, unit.Symbol, unit.FactorToBase, unit.OffsetToBase,
                unit.DecimalPlaces, unit.RoundingMode, unit.Enabled, unit.Sort)).ToArray());
        return clone;
    }

    public UnitConversion Convert(string fromUnitNId, string toUnitNId, decimal value)
    {
        var sourceNId = NormalizeConversionNId(fromUnitNId);
        var targetNId = NormalizeConversionNId(toUnitNId);
        var source = units.SingleOrDefault(unit => unit.NId == sourceNId && unit.Enabled);
        var target = units.SingleOrDefault(unit => unit.NId == targetNId && unit.Enabled);
        if (source is null || target is null) Invalid("unitNId");
        try
        {
            var baseValue = checked(value * source!.FactorToBase + source.OffsetToBase);
            var raw = checked((baseValue - target!.OffsetToBase) / target.FactorToBase);
            var rounded = decimal.Round(raw, target.DecimalPlaces,
                target.RoundingMode == UnitRoundingMode.ToEven ? MidpointRounding.ToEven : MidpointRounding.AwayFromZero);
            return new UnitConversion(value, rounded, raw != rounded, Snapshot(source), Snapshot(target));
        }
        catch (OverflowException)
        {
            throw new ReferenceDataException("REF-UNIT-NUMERIC-OVERFLOW", 422, "value");
        }
    }

    public new void Freeze() { EnsureNotSystem(); if (!IsFrozen) EnsureCapacity(); base.Freeze(); }
    public new void Unfreeze() { EnsureNotSystem(); if (IsFrozen) EnsureCapacity(); base.Unfreeze(); }
    public new void Lock() { EnsureNotSystem(); if (!IsLocked) EnsureCapacity(); base.Lock(); }
    public new void Unlock() { EnsureNotSystem(); if (IsLocked) EnsureCapacity(); base.Unlock(); }
    public new void MarkDeleted() { EnsureNotSystem(); if (!IsDeleted) EnsureCapacity(); base.MarkDeleted(); }
    public new void Restore() { EnsureNotSystem(); if (IsDeleted) EnsureCapacity(); base.Restore(); }

    public static UnitDimension CreateSystemDefined(string nId, string name, string? description,
        UnitConversionKind conversionKind, string baseUnitNId, IReadOnlyList<UnitDefinition> units, int revision,
        DateTimeOffset publishedOn)
    {
        if (revision < 1) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "revision");
        var definition = new UnitDimension(nId, name, description, ReferenceScopeType.Platform, null, null);
        definition.Update(name, description, conversionKind, baseUnitNId, units);
        definition.Revision = revision;
        definition.Status = PublicationStatus.Published;
        definition.PublishedOn = publishedOn.ToUniversalTime();
        definition.PublishedBy = "SYSTEM";
        definition.IsSystemDefined = true;
        definition.CreatedOn = definition.PublishedOn.Value;
        definition.LastUpdatedOn = definition.PublishedOn.Value;
        return definition;
    }

    public static UnitDimension Restore(UnitDimensionState state, IReadOnlyList<UnitDefinition> units)
    {
        var definition = new UnitDimension(state.NId, state.Name, state.Description, state.ScopeType, state.TenantNId, null)
        {
            Id = state.Id,
            Revision = state.Revision,
            Status = state.Status,
            SourceRevision = state.SourceRevision,
            PublishedOn = state.PublishedOn,
            PublishedBy = state.PublishedBy,
            IsSystemDefined = state.IsSystemDefined,
            ConversionKind = state.ConversionKind,
            BaseUnitNId = state.BaseUnitNId,
            IsFrozen = state.IsFrozen,
            IsLocked = state.IsLocked,
            IsDeleted = state.IsDeleted,
            CreatedOn = state.CreatedOn,
            LastUpdatedOn = state.LastUpdatedOn,
            OptimisticVersion = state.OptimisticVersion,
            ConcurrencyVersion = state.ConcurrencyVersion,
            units = units.ToArray(),
        };
        foreach (var unit in definition.units) unit.AttachTo(state.Id);
        return definition;
    }

    private void ValidateConfigured()
    {
        if (units.Length == 0 || units.Length > 200) Invalid("units");
        var baseUnit = units.SingleOrDefault(unit => unit.NId == BaseUnitNId);
        if (baseUnit is null || !baseUnit.Enabled || baseUnit.FactorToBase != 1m || baseUnit.OffsetToBase != 0m)
            Invalid("baseUnitNId");
        if (ConversionKind == UnitConversionKind.Ratio && units.Any(unit => unit.OffsetToBase != 0m)) Invalid("units");
    }

    private void EnsureDraft()
    {
        EnsureNotSystem();
        EnsureMutable();
        if (Status != PublicationStatus.Draft) throw new ReferenceDataException("REF-INVALID-STATE", 409);
        EnsureCapacity();
    }

    private void EnsureMutable()
    {
        if (IsDeleted || IsFrozen || IsLocked) throw new ReferenceDataException("REF-INVALID-STATE", 409);
    }

    private void EnsureNotSystem()
    {
        if (IsSystemDefined) throw new ReferenceDataException("REF-UNIT-SYSTEM-DEFINED", 409);
    }

    private void EnsureCapacity()
    {
        if (OptimisticVersion == long.MaxValue)
            throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
    }

    private static UnitDefinition Snapshot(UnitDefinition unit) => UnitDefinition.Restore(
        unit.Id, unit.UnitDimensionId, unit.NId, unit.Name, unit.Symbol, unit.FactorToBase, unit.OffsetToBase,
        unit.DecimalPlaces, unit.RoundingMode, unit.Enabled, unit.Sort, unit.IsFrozen, unit.IsLocked, unit.IsDeleted,
        unit.CreatedOn, unit.LastUpdatedOn, unit.OptimisticVersion, unit.ConcurrencyVersion);

    private static string NormalizeConversionNId(string value)
    {
        try { return ReferenceValidation.NId(value, item: true); }
        catch (ReferenceDataException) { throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, "unitNId"); }
    }

    private static void Invalid(string field) => throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, field);
}

public sealed record UnitDimensionState(
    Guid Id,
    string NId,
    string Name,
    string? Description,
    ReferenceScopeType ScopeType,
    string? TenantNId,
    int Revision,
    PublicationStatus Status,
    int? SourceRevision,
    DateTimeOffset? PublishedOn,
    string? PublishedBy,
    bool IsSystemDefined,
    UnitConversionKind ConversionKind,
    string BaseUnitNId,
    bool IsFrozen,
    bool IsLocked,
    bool IsDeleted,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    long OptimisticVersion,
    Guid ConcurrencyVersion);

public sealed class UnitDefinition : Entity
{
    private const decimal NumericMaximum = 9999999999999999.999999999999m;
    public Guid UnitDimensionId { get; private set; }
    public string NId { get; }
    public string Name { get; }
    public string Symbol { get; }
    public decimal FactorToBase { get; }
    public decimal OffsetToBase { get; }
    public int DecimalPlaces { get; }
    public UnitRoundingMode RoundingMode { get; }
    public bool Enabled { get; }
    public int Sort { get; }

    public UnitDefinition(string nId, string name, string symbol, decimal factorToBase, decimal offsetToBase,
        int decimalPlaces, UnitRoundingMode roundingMode, bool enabled, int sort)
    {
        NId = ReferenceValidation.NId(nId, item: true);
        Name = ReferenceValidation.Name(name);
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 64)
            throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, "symbol");
        if (factorToBase <= 0m || factorToBase > NumericMaximum || Scale(factorToBase) > 12)
            throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, "factorToBase");
        if (decimal.Abs(offsetToBase) > NumericMaximum || Scale(offsetToBase) > 12)
            throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, "offsetToBase");
        if (decimalPlaces is < 0 or > 12) throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, "decimalPlaces");
        if (!Enum.IsDefined(roundingMode)) throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, "roundingMode");
        if (sort < 0) throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, "sort");
        Symbol = symbol.Trim();
        FactorToBase = factorToBase;
        OffsetToBase = offsetToBase;
        DecimalPlaces = decimalPlaces;
        RoundingMode = roundingMode;
        Enabled = enabled;
        Sort = sort;
    }

    internal void AttachTo(Guid dimensionId) => UnitDimensionId = dimensionId;

    internal UnitDefinition WithSettings(UnitDefinition requested)
    {
        if (NId != requested.NId)
            throw new ReferenceDataException("REF-UNIT-CONVERSION-INVALID", 422, "nId");
        if (Name == requested.Name && Symbol == requested.Symbol && FactorToBase == requested.FactorToBase
            && OffsetToBase == requested.OffsetToBase && DecimalPlaces == requested.DecimalPlaces
            && RoundingMode == requested.RoundingMode && Enabled == requested.Enabled && Sort == requested.Sort)
            return this;
        if (IsDeleted || IsFrozen || IsLocked)
            throw new ReferenceDataException("REF-INVALID-STATE", 409);
        if (OptimisticVersion == long.MaxValue)
            throw new ReferenceDataException("REF-CONCURRENCY-CONFLICT", 409);
        var next = Restore(Id, UnitDimensionId, requested.NId, requested.Name, requested.Symbol,
            requested.FactorToBase, requested.OffsetToBase, requested.DecimalPlaces, requested.RoundingMode,
            requested.Enabled, requested.Sort, IsFrozen, IsLocked, IsDeleted, CreatedOn, LastUpdatedOn,
            OptimisticVersion, ConcurrencyVersion);
        next.Touch();
        return next;
    }

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0x7f;

    public static UnitDefinition Restore(Guid id, Guid dimensionId, string nId, string name, string symbol,
        decimal factorToBase, decimal offsetToBase, int decimalPlaces, UnitRoundingMode roundingMode, bool enabled,
        int sort, bool isFrozen, bool isLocked, bool isDeleted, DateTimeOffset createdOn, DateTimeOffset lastUpdatedOn,
        long optimisticVersion, Guid concurrencyVersion) => new(nId, name, symbol, factorToBase, offsetToBase,
            decimalPlaces, roundingMode, enabled, sort)
    {
        Id = id,
        UnitDimensionId = dimensionId,
        IsFrozen = isFrozen,
        IsLocked = isLocked,
        IsDeleted = isDeleted,
        CreatedOn = createdOn,
        LastUpdatedOn = lastUpdatedOn,
        OptimisticVersion = optimisticVersion,
        ConcurrencyVersion = concurrencyVersion,
    };
}

public sealed record UnitConversion(
    decimal InputValue,
    decimal ResultValue,
    bool WasRounded,
    UnitDefinition Source,
    UnitDefinition Target);
