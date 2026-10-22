using System.Text.Json.Nodes;

namespace IndustrialPlatform.PlatformStudio.Contracts;

/// <summary>Studio列表查询；服务端仅接受明确的排序白名单。</summary>
public sealed record StudioQuery(string? Keyword = null, string? Status = null, int Page = 1, int PageSize = 20, string SortBy = "lastUpdatedOn", string SortOrder = "desc");
public sealed record StudioPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, string TotalCount);

/// <summary>领域并发版本；独立于配置结构版本、发布修订号和文件版本。</summary>
public record VersionCommand(string? OptimisticVersion, Guid ConcurrencyVersion);
public sealed record ResourceCreate(string? NId, string? Kind, string? Name, string? Description, string? Folder, JsonObject? Document);
public sealed record DraftSave(string? OptimisticVersion, Guid ConcurrencyVersion, string? Name, string? Description, string? Folder, JsonObject? Document) : VersionCommand(OptimisticVersion, ConcurrencyVersion);
public sealed record ResourcePublish(string? OptimisticVersion, Guid ConcurrencyVersion, string? ReleaseNote, bool Enable, string? DependencyPlanHash) : VersionCommand(OptimisticVersion, ConcurrencyVersion);
public sealed record ResourceRollback(string? OptimisticVersion, Guid ConcurrencyVersion, string? RevisionNId, string? Reason) : VersionCommand(OptimisticVersion, ConcurrencyVersion);
public sealed record ResourceGrant(string SubjectKind, string SubjectNId, string Capability, DateTimeOffset? RevokedOn = null);
public sealed record GrantSave(string? OptimisticVersion, Guid ConcurrencyVersion, IReadOnlyList<ResourceGrant>? Grants) : VersionCommand(OptimisticVersion, ConcurrencyVersion);
public sealed record ResourceDto(string NId, string Kind, string Name, string? Description, string? Folder, bool IsEnabled, string? PublishedRevisionNId, string? PublishedRevisionNo, bool HasDraftChanges, string OwnerUserNId, DateTimeOffset CreatedOn, DateTimeOffset LastUpdatedOn, string OptimisticVersion, Guid ConcurrencyVersion);
public sealed record DraftDto(ResourceDto Resource, JsonObject DraftDocument, IReadOnlyList<object> Diagnostics);
public sealed record RevisionSummaryDto(string NId, string RevisionNo, string ContentHash, string PublishedBy, DateTimeOffset CreatedOn, string ReleaseNote);
public sealed record RevisionDto(string NId, string ResourceNId, string RevisionNo, int DocumentSchemaVersion, JsonObject Document, JsonArray Dependencies, JsonArray SourcePins, string ContentHash, string PublishedBy, DateTimeOffset CreatedOn, string ReleaseNote);

/// <summary>来源管理DTO有意不暴露CredentialRef、连接串和实际受信URL。</summary>
public sealed record SourceCommand(string? NId, string? Name, string? Description, string? Provider, string? EndpointKey, string? CatalogKey, JsonArray? AllowedObjects, int TimeoutSeconds = 30, int MaxRows = 10000, string MaxBytes = "10485760", string? OptimisticVersion = null, Guid ConcurrencyVersion = default);
public sealed record SourceDto(string NId, string Name, string? Description, string Provider, string EndpointKey, string? CatalogKey, JsonArray AllowedObjects, bool IsEnabled, bool CredentialConfigured, string ConfigurationVersion, int TimeoutSeconds, int MaxRows, string MaxBytes, string OptimisticVersion, Guid ConcurrencyVersion);
public sealed record DatasetExecute(string? RevisionNId, JsonObject? Parameters, int? Page = null, int? PageSize = null);
public sealed record DatasetPreview(string? SourceNId, string? SourceConfigurationVersion, JsonObject? Document, JsonObject? Parameters);
public sealed record LedgerWrite(JsonObject? Values, string? RevisionNId, string? OptimisticVersion = null, Guid ConcurrencyVersion = default, string? NId = null);
public sealed record LedgerRecordDto(string NId, string ModelNId, string SchemaRevisionNId, JsonObject Values, DateTimeOffset CreatedOn, DateTimeOffset LastUpdatedOn, string CreatedBy, string LastUpdatedBy, bool IsDeleted, string OptimisticVersion, Guid ConcurrencyVersion);

public sealed record LedgerFilter(string FieldId, string Operator, JsonNode? Value);
public sealed record LedgerQuery(int Page = 1, int PageSize = 20, string? SortFieldId = null, string SortOrder = "desc", bool Deleted = false, IReadOnlyList<LedgerFilter>? Filters = null, LedgerCondition? Condition = null, IReadOnlyList<LedgerSort>? Sorts = null);

public sealed record LedgerCondition(string Operator, string? FieldId = null, JsonNode? Value = null, IReadOnlyList<LedgerCondition>? Items = null);
public sealed record LedgerSort(string FieldId, string Direction = "Asc", string Nulls = "Last");
