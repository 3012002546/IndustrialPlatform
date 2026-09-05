using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndustrialPlatform.ReferenceData.Contracts.DynamicProperty;

public sealed record DynamicFieldRequest(string NId, string Name, string DataType, bool Required, bool Enabled, int Sort,
    JsonElement? DefaultValue, int? MinLength = null, int? MaxLength = null, decimal? MinValue = null, decimal? MaxValue = null,
    int? Scale = null, string? Pattern = null, string? DictionaryNId = null, string? ReferenceTarget = null, string? Description = null);
public sealed record DynamicFieldDto(Guid Id, string NId, string Name, string DataType, bool Required, bool Enabled, int Sort,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] JsonElement? DefaultValue, string? DefaultValueJson,
    int? MinLength, int? MaxLength, decimal? MinValue, decimal? MaxValue, int? Scale, string? Pattern,
    string? DictionaryNId, string? ReferenceTarget, string? Description, bool HasHadValue, bool WasPublished,
    string? MinValueJson, string? MaxValueJson);
public sealed record CreateDynamicConfigurationRequest(string ScopeType, string? ScopeId, string NId, string Name,
    string? Description, IReadOnlyList<DynamicFieldRequest> Fields);
public sealed record UpdateDynamicConfigurationRequest(string Name, string? Description, IReadOnlyList<DynamicFieldRequest> Fields,
    long ExpectedOptimisticVersion, Guid ExpectedConcurrencyVersion);
public sealed record DynamicConfigurationSummaryDto(Guid Id, string NId, string Name, string ScopeType, string? TenantNId,
    int Revision, string Status, int FieldCount, int RecordCount, long ValueCount, long OptimisticVersion,
    Guid ConcurrencyVersion, DateTimeOffset LastUpdatedOn, bool IsFrozen, bool IsLocked, DateTimeOffset? PublishedOn);
public sealed record DynamicConfigurationDetailDto(Guid Id, string NId, string Name, string? Description, string ScopeType,
    string? TenantNId, int Revision, string Status, IReadOnlyList<DynamicFieldDto> Fields, int RecordCount, long ValueCount,
    long OptimisticVersion, Guid ConcurrencyVersion, DateTimeOffset LastUpdatedOn, DateTimeOffset? PublishedOn,
    string? PublishedBy, bool IsFrozen, bool IsLocked);
public sealed record DynamicRecordRequest(string NId, string? Name, string? Category, int Sort, bool Enabled,
    IReadOnlyDictionary<string, JsonElement> Values, long ExpectedOptimisticVersion, Guid ExpectedConcurrencyVersion);
public sealed record DynamicRecordDto(Guid Id, string NId, string? Name, string? Category, int Sort, bool Enabled,
    IReadOnlyDictionary<string, JsonElement> Values, IReadOnlyDictionary<string, string> ValuesJson, int Revision,
    bool IsFrozen, bool IsLocked);
public sealed record DynamicRecordMutationDto(DynamicRecordDto Record, long OptimisticVersion, Guid ConcurrencyVersion);
public sealed record DynamicSchemaDto(Guid DefinitionId, string NId, string Name, IReadOnlyList<DynamicFieldDto> Fields,
    string SourceScope, string? SourceTenantNId, int Revision, DateTimeOffset PublishedOn);
public sealed record DynamicPublicationIssue(string Code, string? Field);
public sealed record DynamicPublicationCheckDto(int? PreviousRevision, IReadOnlyList<string> AddedFields,
    IReadOnlyList<string> ChangedFields, IReadOnlyList<string> DisabledFields, int RecordCount, long ValueCount,
    IReadOnlyList<DynamicPublicationIssue> Errors);
