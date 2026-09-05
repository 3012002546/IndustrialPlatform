namespace IndustrialPlatform.ReferenceData.Contracts.Metadata;

public sealed record MetadataAttributeRequest(
    string NId,
    string Name,
    string DataType,
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
    string? UnitSourceScope,
    string? UnitSourceTenantNId,
    string? Description);

public sealed record MetadataAttributeDto(
    Guid Id,
    string NId,
    string Name,
    string DataType,
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
    string? UnitSourceScope,
    string? UnitSourceTenantNId,
    string? Description,
    bool WasPublished,
    bool IsFrozen,
    bool IsLocked);

public sealed record RuntimeMetadataAttributeDto(
    string NId,
    string Name,
    string DataType,
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
    string? UnitSourceScope,
    string? UnitSourceTenantNId,
    string? Description);

public sealed record CreateMetadataSchemaRequest(
    string ScopeType,
    string NId,
    string Name,
    string? Description,
    IReadOnlyList<MetadataAttributeRequest> Attributes);

public sealed record UpdateMetadataSchemaRequest(
    string Name,
    string? Description,
    IReadOnlyList<MetadataAttributeRequest> Attributes,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

public sealed record MetadataSchemaSummaryDto(
    Guid Id,
    string NId,
    string Name,
    string? Description,
    string ScopeType,
    string? TenantNId,
    int Revision,
    string Status,
    int? SourceRevision,
    int AttributeCount,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    DateTimeOffset LastUpdatedOn,
    DateTimeOffset? PublishedOn,
    string? PublishedBy,
    bool IsFrozen,
    bool IsLocked);

public sealed record MetadataSchemaDetailDto(
    Guid Id,
    string NId,
    string Name,
    string? Description,
    string ScopeType,
    string? TenantNId,
    int Revision,
    string Status,
    int? SourceRevision,
    IReadOnlyList<MetadataAttributeDto> Attributes,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    DateTimeOffset LastUpdatedOn,
    DateTimeOffset? PublishedOn,
    string? PublishedBy,
    bool IsFrozen,
    bool IsLocked);

public sealed record EffectiveSchemaDto(
    string NId,
    string Name,
    string? Description,
    IReadOnlyList<RuntimeMetadataAttributeDto> Attributes,
    string SourceScope,
    string? SourceTenantNId,
    int Revision,
    DateTimeOffset PublishedOn);

public sealed record MetadataPublicationIssue(string Code, string? Field);
public sealed record MetadataCompatibilityChange(string AttributeNId, string Code);
public sealed record MetadataPublicationCheckDto(
    int? PreviousRevision,
    IReadOnlyList<string> AddedAttributes,
    IReadOnlyList<string> TightenedAttributes,
    IReadOnlyList<string> RelaxedAttributes,
    IReadOnlyList<string> DisabledAttributes,
    IReadOnlyList<MetadataCompatibilityChange> IncompatibleChanges,
    IReadOnlyList<MetadataPublicationIssue> Errors);
