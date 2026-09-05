namespace IndustrialPlatform.ReferenceData.Contracts.Dictionary;

public sealed record DictionaryItemDto(string NId, string Name, string? Description, int Sort, bool Enabled);
public sealed record DictionarySummaryDto(Guid Id, string NId, string Name, string ScopeType, string? TenantNId, int Revision,
    string Status, int EnabledItemCount, long OptimisticVersion, Guid ConcurrencyVersion, DateTimeOffset LastUpdatedOn,
    string? PublishedBy, bool IsFrozen, bool IsLocked);
public sealed record CreateDictionaryRequest(string ScopeType, string? ScopeId, string NId, string Name, string? Description, IReadOnlyList<DictionaryItemDto> Items);
public sealed record UpdateDictionaryRequest(string Name, string? Description, IReadOnlyList<DictionaryItemDto> Items, long ExpectedOptimisticVersion, Guid ExpectedConcurrencyVersion);
public sealed record DictionaryDetailDto(Guid Id, string NId, string Name, string? Description, string ScopeType, string? TenantNId, int Revision,
    string Status, IReadOnlyList<DictionaryItemDto> Items, long OptimisticVersion, Guid ConcurrencyVersion, DateTimeOffset LastUpdatedOn,
    DateTimeOffset? PublishedOn, string? PublishedBy, bool IsFrozen, bool IsLocked);
public sealed record EffectiveDictionaryDto(string NId, string Name, IReadOnlyList<DictionaryItemDto> Items, string SourceScope, string? SourceTenantNId, int Revision, DateTimeOffset PublishedOn);
public sealed record DictionaryPublicationIssue(string Code, string Field);
public sealed record DictionaryPublicationCheckDto(int? PreviousRevision, IReadOnlyList<string> AddedItems, IReadOnlyList<string> ChangedItems,
    IReadOnlyList<string> DisabledItems, IReadOnlyList<DictionaryPublicationIssue> Errors);
