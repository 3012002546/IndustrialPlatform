namespace IndustrialPlatform.ReferenceData.Contracts.CodingRule;

public sealed record CreateCodingRuleRequest(
    string ScopeType,
    string NId,
    string Name,
    string TargetEntityNId,
    string Template,
    string ResetPolicy);

public sealed record UpdateCodingRuleRequest(
    string Name,
    string TargetEntityNId,
    string Template,
    string ResetPolicy,
    long ExpectedOptimisticVersion,
    Guid ExpectedConcurrencyVersion);

public sealed record CodingRuleSummaryDto(
    Guid Id,
    string NId,
    string Name,
    string TargetEntityNId,
    string Template,
    string ResetPolicy,
    string ScopeType,
    string? TenantNId,
    int Revision,
    string Status,
    int? SourceRevision,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    DateTimeOffset LastUpdatedOn,
    DateTimeOffset? PublishedOn,
    string? PublishedBy,
    bool IsFrozen,
    bool IsLocked);

public sealed record CodingRuleDetailDto(
    Guid Id,
    string NId,
    string Name,
    string TargetEntityNId,
    string Template,
    string ResetPolicy,
    string ScopeType,
    string? TenantNId,
    int Revision,
    string Status,
    int? SourceRevision,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    DateTimeOffset? PublishedOn,
    string? PublishedBy,
    bool IsFrozen,
    bool IsLocked);

public sealed record PreviewCodeRequest(
    string? SourceScope = null,
    string? SourceTenantNId = null,
    int? RuleRevision = null,
    string? FactoryId = null);

public sealed record GenerateCodeRequest(
    string SourceScope,
    string? SourceTenantNId,
    int RuleRevision,
    string? FactoryId = null);

public sealed record CodePreviewDto(
    string CodingRuleNId,
    int RuleRevision,
    string SourceScope,
    string? SourceTenantNId,
    string Code,
    long SampleSequence,
    string PeriodKey,
    DateTimeOffset PreviewedOn,
    bool ConsumesSequence);

public sealed record GeneratedCodeDto(
    string CodingRuleNId,
    int RuleRevision,
    string Code,
    long Sequence,
    string PeriodKey,
    DateTimeOffset GeneratedOn);
