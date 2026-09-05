namespace IndustrialPlatform.ReferenceData.Contracts.UnitOfMeasure;

public sealed record UnitDefinitionRequest(
    string NId,
    string Name,
    string Symbol,
    string FactorToBase,
    string OffsetToBase,
    int DecimalPlaces,
    string RoundingMode,
    bool Enabled,
    int Sort);

public sealed record UnitDefinitionDto(
    Guid Id,
    string NId,
    string Name,
    string Symbol,
    string FactorToBase,
    string OffsetToBase,
    int DecimalPlaces,
    string RoundingMode,
    bool Enabled,
    int Sort,
    bool IsFrozen,
    bool IsLocked);

public sealed record CreateUnitDimensionRequest(
    string ScopeType,
    string? ScopeId,
    string NId,
    string Name,
    string? Description,
    string ConversionKind,
    string BaseUnitNId,
    IReadOnlyList<UnitDefinitionRequest> Units);

public sealed record UpdateUnitDimensionRequest(
    string Name,
    string? Description,
    string ConversionKind,
    string BaseUnitNId,
    IReadOnlyList<UnitDefinitionRequest> Units,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

public sealed record UnitDimensionSummaryDto(
    Guid Id,
    string NId,
    string Name,
    string? Description,
    string ScopeType,
    string? TenantNId,
    int Revision,
    string Status,
    int? SourceRevision,
    bool IsSystemDefined,
    string ConversionKind,
    string BaseUnitNId,
    int UnitCount,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    DateTimeOffset LastUpdatedOn,
    DateTimeOffset? PublishedOn,
    string? PublishedBy,
    bool IsFrozen,
    bool IsLocked);

public sealed record UnitDimensionDetailDto(
    Guid Id,
    string NId,
    string Name,
    string? Description,
    string ScopeType,
    string? TenantNId,
    int Revision,
    string Status,
    int? SourceRevision,
    DateTimeOffset? PublishedOn,
    string? PublishedBy,
    bool IsSystemDefined,
    string ConversionKind,
    string BaseUnitNId,
    IReadOnlyList<UnitDefinitionDto> Units,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    DateTimeOffset LastUpdatedOn,
    bool IsFrozen,
    bool IsLocked);

public sealed record AvailableUnitDimensionDto(
    string NId,
    string Name,
    string SourceScope,
    string? SourceTenantNId,
    int Revision,
    DateTimeOffset PublishedOn,
    bool IsSystemDefined,
    string ConversionKind,
    string BaseUnitNId,
    int UnitCount);

// Runtime reads expose stable business identity, source, revision and value semantics only.
public sealed record RuntimeUnitDefinitionDto(
    string NId,
    string Name,
    string Symbol,
    string FactorToBase,
    string OffsetToBase,
    int DecimalPlaces,
    string RoundingMode,
    bool Enabled,
    int Sort);

public sealed record UnitDimensionDto(
    string NId,
    string Name,
    string? Description,
    string SourceScope,
    string? SourceTenantNId,
    int Revision,
    string Status,
    int? SourceRevision,
    DateTimeOffset? PublishedOn,
    bool IsSystemDefined,
    string ConversionKind,
    string BaseUnitNId,
    IReadOnlyList<RuntimeUnitDefinitionDto> Units);

public sealed record UnitConversionRequest(
    string SourceScope,
    string? SourceTenantNId,
    string UnitDimensionNId,
    int UnitRevision,
    string FromUnitNId,
    string ToUnitNId,
    string Value);

public sealed record UnitConversionSnapshotDto(
    string SourceFactorToBase,
    string SourceOffsetToBase,
    string TargetFactorToBase,
    string TargetOffsetToBase,
    int DecimalPlaces,
    string RoundingMode);

public sealed record UnitConversionResultDto(
    string UnitDimensionNId,
    int UnitRevision,
    string SourceScope,
    string? SourceTenantNId,
    string FromUnitNId,
    string ToUnitNId,
    string InputValue,
    string ResultValue,
    bool WasRounded,
    UnitConversionSnapshotDto ConversionSnapshot);
