using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndustrialPlatform.ReferenceData.Contracts.Parameter;

public sealed record CreateConfigurationDomainRequest(string ScopeType, string? ScopeId, string NId, string Name, string? Description, string ChangeReason);
public sealed record UpdateConfigurationDomainRequest(string NId, string Name, string? Description, string ChangeReason,
    long ExpectedOptimisticVersion, Guid ExpectedConcurrencyVersion, string ScopeType, string? ScopeId);
public sealed record ConfigurationDomainStateRequest(string ChangeReason, long ExpectedAppDomainOptimisticVersion, Guid ExpectedAppDomainConcurrencyVersion);
public sealed record ConfigurationChildStateRequest(string ChangeReason, long ExpectedAppDomainOptimisticVersion, Guid ExpectedAppDomainConcurrencyVersion);
public sealed record ConfigurationKeyRequest(string NId, string Name, string? Description, string DataType, string ValueMode,
    JsonElement? Value, JsonElement? DefaultValue, bool IsMandatory, bool IsReadOnly, string? DictionaryNId, string? ReferenceTarget,
    string Status, int Sort, string ChangeReason, long ExpectedAppDomainOptimisticVersion, Guid ExpectedAppDomainConcurrencyVersion,
    IReadOnlyList<ConfigurationInitialValue>? InitialValues = null);
public sealed record ConfigurationInitialValue(string NId, string? Name, JsonElement Value, int Sort, bool IsDefault, bool Enabled);
public sealed record ConfigurationValueRequest(string NId, string? Name, JsonElement Value, int Sort, bool IsDefault, bool Enabled,
    string ChangeReason, long ExpectedAppDomainOptimisticVersion, Guid ExpectedAppDomainConcurrencyVersion);
public sealed record ConfigurationDomainSummaryDto(Guid Id, string NId, string Name, string ScopeType, string? TenantNId, string Status,
    long Revision, int KeyCount, DateTimeOffset LastUpdatedOn, long OptimisticVersion, Guid ConcurrencyVersion, bool IsFrozen, bool IsLocked);
public sealed record ConfigurationDomainDetailDto(Guid Id, string NId, string Name, string? Description, string ScopeType, string? TenantNId,
    string Status, long Revision, IReadOnlyList<ConfigurationKeyDto> Keys, DateTimeOffset LastUpdatedOn,
    long OptimisticVersion, Guid ConcurrencyVersion, bool IsFrozen, bool IsLocked);
public sealed record ConfigurationKeyDto(Guid Id, string NId, string FullNId, string Name, string? Description, string DataType, string ValueMode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] JsonElement? Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] JsonElement? DefaultValue,
    string? ValueJson, string? DefaultValueJson, bool IsMandatory, bool IsReadOnly, string? DictionaryNId, string? ReferenceTarget,
    string Status, int Sort, IReadOnlyList<ConfigurationMultiValueDto> MultiValues, bool HasHadValue, bool IsFrozen, bool IsLocked)
{
    public bool IsMultiValue => ValueMode == "Multi";
}
public sealed record ConfigurationMultiValueDto(Guid Id, string NId, string? Name, JsonElement Value, string ValueJson, int Sort, bool IsDefault, bool Enabled);
public sealed record EffectiveConfigurationDto(string AppDomainNId, string KeyNId, string FullNId, string DataType, string ValueMode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] JsonElement? Value, string? ValueJson,
    IReadOnlyList<ConfigurationMultiValueDto> MultiValues, bool UsesDefaultValue, bool BlocksInheritance,
    string SourceScope, string? SourceTenantNId, long Revision, DateTimeOffset LastUpdatedOn);
public sealed record EffectiveConfigurationDomainDto(string AppDomainNId, IReadOnlyList<EffectiveConfigurationDto> Keys);
public sealed record ConfigurationHistoryDto(Guid Id, Guid AppDomainId, Guid? KeyId, string ObjectType, Guid ObjectId, string FullNId,
    string ChangeType, string ChangeReason, string BeforeSummary, string AfterSummary, long Revision, string UserNId, string TraceId, DateTimeOffset CreatedOn);
