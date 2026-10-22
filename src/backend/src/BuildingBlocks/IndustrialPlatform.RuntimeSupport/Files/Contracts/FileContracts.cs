namespace IndustrialPlatform.SystemData.Contracts.Files;

public sealed record CreateUploadSessionRequest
{
    public string? SessionNId { get; init; }
    public string? TransportId { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public long? Length { get; init; }
    public string? Sha256 { get; init; }
    public string? SampleFingerprint { get; init; }
    public string? Purpose { get; init; }
}

public sealed record DiscoverUploadSessionRequest
{
    public string? FileName { get; init; }
    public long? Length { get; init; }
    public string? SampleFingerprint { get; init; }
    public string? Purpose { get; init; }
}

public sealed record ContentHashRequest
{
    public string? Sha256 { get; init; }
}

public sealed record ResumeProofRequest
{
    public int? WriterEpoch { get; init; }
    public string? Proof { get; init; }
}

public sealed record TakeoverUploadRequest
{
    public int? ExpectedWriterEpoch { get; init; }
    public string? IdempotencyKey { get; init; }
    public string? Proof { get; init; }
}

public sealed record SetUploadStateRequest
{
    public string? Reason { get; init; }
}

public sealed record FileReferenceRequest
{
    public string? ReferenceNId { get; init; }
    public string? Purpose { get; init; }
}

public sealed record FileBindingRequest
{
    public string? RequestNId { get; init; }
    public string? FileNId { get; init; }
    public string? ConversationNId { get; init; }
    public string? MessageNId { get; init; }
    public string? AttachmentNId { get; init; }
    public string? UploaderUserNId { get; init; }
    public string? Purpose { get; init; }
}

public sealed record FileBindingReleaseRequest
{
    public string? RequestNId { get; init; }
    public long? ExpectedVersion { get; init; }
}

public sealed record FileBindingV1
{
    public string TenantNId { get; init; } = string.Empty;
    public string ReferenceNId { get; init; } = string.Empty;
    public string FileNId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public long Version { get; init; }
}

public sealed record FileHoldRequest
{
    public string? RequestNId { get; init; }
    public string? ScopeChecksum { get; init; }
    public long? CaseRevision { get; init; }
}

public sealed record FileHoldV1
{
    public string TenantNId { get; init; } = string.Empty;
    public string CaseNId { get; init; } = string.Empty;
    public string FileNId { get; init; } = string.Empty;
    public string ScopeChecksum { get; init; } = string.Empty;
    public long CaseRevision { get; init; }
    public string OwnerService { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset UpdatedOn { get; init; }
    public DateTimeOffset? ReleasedOn { get; init; }
}

public sealed record FileRestrictionRequest
{
    public bool? Restricted { get; init; }
}

public sealed record UploadSessionV1
{
    public string TenantNId { get; init; } = string.Empty;
    public string SessionNId { get; init; } = string.Empty;
    public string TransportId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public long Length { get; init; }
    public long Offset { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public int WriterEpoch { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset ExpiresOn { get; init; }
    public string? FileNId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ResumeTicket { get; init; }
    public DateTimeOffset? ResumeTicketExpiresOn { get; init; }
}

public sealed record FileObjectV1
{
    public string TenantNId { get; init; } = string.Empty;
    public string FileNId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public long Length { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string ScanStatus { get; init; } = string.Empty;
    public bool Restricted { get; init; }
    public string? Purpose { get; init; }
    public string? OwnerUserNId { get; init; }
    public int ReferenceCount { get; init; }
    public IReadOnlyList<FileReferenceSummaryV1> ReferenceSummary { get; init; } = [];
    public string DeletionStatus { get; init; } = string.Empty;
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset LastUpdatedOn { get; init; }
    public DateTimeOffset? RetentionUntil { get; init; }
}

public sealed record FileReferenceSummaryV1
{
    public string ReferenceNId { get; init; } = string.Empty;
    public string OwnerUserNId { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public DateTimeOffset CreatedOn { get; init; }
}

public sealed record FilePageV1
{
    public IReadOnlyList<FileObjectV1> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long Total { get; init; }
}

public sealed record FileUploadDiscoveryV1
{
    public IReadOnlyList<UploadSessionV1> Candidates { get; init; } = [];
}
