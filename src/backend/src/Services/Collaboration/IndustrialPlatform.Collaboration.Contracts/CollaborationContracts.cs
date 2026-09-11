namespace IndustrialPlatform.Collaboration.Contracts;

using System.Text.Json.Serialization;

public static class CollaborationPermissions
{
    public const string MessagingRead = "collaboration.messaging.read";
    public const string MessagingConversationStart = "collaboration.messaging.conversation.start";
    public const string MessagingWrite = "collaboration.messaging.write";
    public const string MessagingReadCursorUpdate = "collaboration.messaging.read-cursor.update";
    public const string MessagingConversationHide = "collaboration.messaging.conversation.hide";
    public const string MessagingConversationRestore = "collaboration.messaging.conversation.restore";
    public const string MessagingRetract = "collaboration.messaging.retract";
    public const string MessagingAttachmentSend = "collaboration.messaging.attachment.send";
    public const string MessagingAttachmentDownload = "collaboration.messaging.attachment.download";
    public const string ComplianceRead = "collaboration.compliance.read";
    public const string ComplianceView = "collaboration.compliance.view";
    public const string ComplianceReadOriginal = "collaboration.compliance.read-original";
    public const string ComplianceDispose = "collaboration.compliance.dispose";
    public const string ComplianceExportRequest = "collaboration.compliance.export.request";
    public const string ComplianceExportDownload = "collaboration.compliance.export.download";
    public const string ComplianceExportApprove = "collaboration.compliance.export.approve";
    public const string ComplianceLegalHoldCreate = "collaboration.compliance.legal-hold.create";
    public const string ComplianceLegalHoldReview = "collaboration.compliance.legal-hold.review";
    public const string ComplianceLegalHoldRelease = "collaboration.compliance.legal-hold.release";
    public const string ComplianceLegalHoldReleaseApprove = "collaboration.compliance.legal-hold.release.approve";
    public const string ComplianceRetentionManage = "collaboration.compliance.retention.manage";
    public const string ComplianceRetentionUpdate = "collaboration.compliance.retention.update";
    public const string PresenceConnect = "collaboration.presence.connect";
    public const string PresenceRead = "collaboration.presence.read";
    public const string PresenceWrite = "collaboration.presence.write";
}

public static class CollaborationServiceConstants
{
    public const string ServiceKey = "collaboration";
    public const string MessagingSchema = "collaboration_messaging";
    public const string InternalIdentityAudience = "identity.pf05";
    public const string InternalSystemDataAudience = "systemdata.pf05";
    public const string InternalEmbeddedHostAudience = "embedded-host.pf05";
    public const string AttachmentPurpose = "CollaborationMessageAttachment";
    public const string ComplianceExportPurpose = "CollaborationComplianceExport";
    public const string ComplianceLegalHoldPurpose = "CollaborationLegalHold";
    public const int MaxMessageLength = 4000;
    public const long MaxAttachmentLength = 50L * 1024 * 1024;
}

public sealed record CollaborationDirectoryUserDto(
    string UserNId,
    string DisplayName,
    bool CanStart);

public sealed record CollaborationDirectoryPageDto(
    IReadOnlyList<CollaborationDirectoryUserDto> Items,
    string? NextCursor);

public sealed record CreateConversationRequest
{
    public string? PeerUserNId { get; init; }
    public string? RequestNId { get; init; }
}

public sealed record ConversationSummaryDto
{
    public string ConversationNId { get; init; } = string.Empty;
    public bool Created { get; init; }
    public string PeerUserNId { get; init; } = string.Empty;
    public string PeerDisplayName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Status { get; init; } = "Active";
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long LastMessageSequence { get; init; }
    public string? LastMessageNId { get; init; }
    public DateTimeOffset? LastMessageOn { get; init; }
    public ConversationLastMessagePreviewDto? LastMessagePreview { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long UnreadCount { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long OptimisticVersion { get; init; }
    public Guid ConcurrencyVersion { get; init; }
    public string VisibilityState { get; init; } = "Visible";
    public PresenceDto? Presence { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long ProjectionVersion { get; init; }
}

public sealed record ConversationLastMessagePreviewDto
{
    public string MessageNId { get; init; } = string.Empty;
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Sequence { get; init; }
    public DateTimeOffset AcceptedOn { get; init; }
    public string MessageType { get; init; } = "Text";
    public string State { get; init; } = "Accepted";
    public string? Text { get; init; }
}

public sealed record ConversationMemberDto
{
    public string UserNId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string VisibilityState { get; init; } = "Visible";
    public DateTimeOffset JoinedOn { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long LastReadSequence { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long UnreadCount { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long OptimisticVersion { get; init; }
    public Guid ConcurrencyVersion { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long ProjectionVersion { get; init; }
}

public sealed record ConversationVisibilityDto
{
    public string VisibilityState { get; init; } = "Visible";
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long OptimisticVersion { get; init; }
    public Guid ConcurrencyVersion { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long ProjectionVersion { get; init; }
}

public sealed record ReadCursorDto
{
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long LastReadSequence { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long UnreadCount { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long ProjectionVersion { get; init; }
    public Guid ConcurrencyVersion { get; init; }
}

public sealed record ConversationDetailDto
{
    public string ConversationNId { get; init; } = string.Empty;
    public string Status { get; init; } = "Active";
    public ConversationMemberDto CurrentMember { get; init; } = new();
    public ConversationMemberDto PeerMember { get; init; } = new();
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long LastMessageSequence { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long RetentionFloorSequence { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long OptimisticVersion { get; init; }
    public Guid ConcurrencyVersion { get; init; }
}

public sealed record ConversationPageDto
{
    public IReadOnlyList<ConversationSummaryDto> Items { get; init; } = [];
    public string? NextCursor { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Total { get; init; }
}

public sealed record SendMessageRequest
{
    public string? ClientMessageNId { get; init; }
    public string? MessageType { get; init; }
    public string? TextContent { get; init; }
    public string? ReplyToMessageNId { get; init; }
    public string? AttachmentNId { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed record MessageAttachmentDto
{
    public string AttachmentNId { get; init; } = string.Empty;
    public string? FileNId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public string MediaType { get; init; } = "application/octet-stream";
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Size { get; init; }
    public string FileState { get; init; } = "Pending";
    public string ReferenceState { get; init; } = "Pending";
}

public sealed record AttachmentDetailDto
{
    public string AttachmentNId { get; init; } = string.Empty;
    public string State { get; init; } = "Pending";
    public string? FileName { get; init; }
    public string? MediaType { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long? SizeBytes { get; init; }
    public DateTimeOffset? ObservedOn { get; init; }
    public string? FileNId { get; init; }
    public string ReferenceState { get; init; } = "Pending";
}

public sealed record MessageDto
{
    public string MessageNId { get; init; } = string.Empty;
    public string ConversationNId { get; init; } = string.Empty;
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Sequence { get; init; }
    public string SenderUserNId { get; init; } = string.Empty;
    public string SenderDisplayName { get; init; } = string.Empty;
    public string ClientMessageNId { get; init; } = string.Empty;
    public string MessageType { get; init; } = "Text";
    public string? TextContent { get; init; }
    public string? ReplyToMessageNId { get; init; }
    public MessageAttachmentDto? Attachment { get; init; }
    public DateTimeOffset AcceptedOn { get; init; }
    public DateTimeOffset? RetractedOn { get; init; }
    public string? RetractionReason { get; init; }
    public string State { get; init; } = "Accepted";
    [JsonConverter(typeof(CollaborationInt32StringConverter))]
    public int MessageStateVersion { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long OptimisticVersion { get; init; }
    public Guid ConcurrencyVersion { get; init; }
}

public sealed record MessagePageDto
{
    public IReadOnlyList<MessageDto> Items { get; init; } = [];
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long HighWatermarkSequence { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long RetentionFloorSequence { get; init; }
    [JsonConverter(typeof(CollaborationNullableInt64StringConverter))]
    public long? NextAfterSequence { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long EarliestAvailableSequence { get; init; }
    public string? NextCursor { get; init; }
    public bool HasMore { get; init; }
    public DateTimeOffset? RetentionCutoffOn { get; init; }
    public string RecoveryAction { get; init; } = "ReloadFromEarliest";
}

public sealed record ReadCursorRequest
{
    [JsonConverter(typeof(CollaborationNullableInt64StringConverter))]
    public long? Sequence { get; init; }
}

public sealed record HideConversationRequest
{
    [JsonConverter(typeof(CollaborationNullableInt64StringConverter))]
    public long? ThroughSequence { get; init; }
    [JsonConverter(typeof(CollaborationNullableInt64StringConverter))]
    public long? ExpectedOptimisticVersion { get; init; }
    public Guid? ExpectedConcurrencyVersion { get; init; }
}

public sealed record RetractMessageRequest
{
    public string? Reason { get; init; }
    [JsonConverter(typeof(CollaborationNullableInt64StringConverter))]
    public long? ExpectedOptimisticVersion { get; init; }
    public Guid? ExpectedConcurrencyVersion { get; init; }
}

public sealed record PersonalMessageVisibilityRequest
{
    public bool? Hidden { get; init; }
}

public sealed record PersonalMessageVisibilityDto
{
    public string MessageNId { get; init; } = string.Empty;
    public bool Hidden { get; init; }
}

public sealed record AttachmentIntentRequest
{
    public string? ConversationNId { get; init; }
    public string? AttachmentNId { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public string? MediaType { get; init; }
    [JsonConverter(typeof(CollaborationNullableInt64StringConverter))]
    public long? Size { get; init; }
    [JsonConverter(typeof(CollaborationNullableInt64StringConverter))]
    public long? SizeBytes { get; init; }
    public string? Sha256 { get; init; }
    public string? RequestNId { get; init; }
}

public sealed record AttachmentIntentDto
{
    public string AttachmentNId { get; init; } = string.Empty;
    public string ConversationNId { get; init; } = string.Empty;
    public string? UploadSessionNId { get; init; }
    public string? TransportId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Size { get; init; }
    public string MediaType { get; init; } = "application/octet-stream";
    [JsonPropertyName("sizeBytes")]
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long SizeBytes => Size;
    public string State { get; init; } = "Pending";
    public DateTimeOffset ExpiresOn { get; init; }
    public AttachmentFileUploadDto? FileUpload { get; init; }
}

public sealed record AttachmentFileUploadDto
{
    public AttachmentUploadSessionDto Session { get; init; } = new();
    public string UploadUrl { get; init; } = string.Empty;
}

public sealed record AttachmentUploadSessionDto
{
    public string SessionNId { get; init; } = string.Empty;
    public string TransportId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string MediaType { get; init; } = "application/octet-stream";
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long SizeBytes { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Offset { get; init; }
    public int WriterEpoch { get; init; }
    public string State { get; init; } = "Pending";
    public string? FileNId { get; init; }
    public DateTimeOffset ExpiresOn { get; init; }
    public string? ResumeTicket { get; init; }
}

public sealed record AttachmentAuthorizationRequest
{
    public string? FileNId { get; init; }
    public string? RequestNId { get; init; }
    public string? Purpose { get; init; }
}

public sealed record AttachmentAuthorizationDto
{
    public string AttachmentNId { get; init; } = string.Empty;
    public string FileNId { get; init; } = string.Empty;
    public string ReferenceNId { get; init; } = string.Empty;
    public string State { get; init; } = "Authorized";
    public object? Authorization { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
}

public sealed record AttachmentContentHashRequest
{
    public string? Sha256 { get; init; }
}

public sealed record AttachmentResumeProofRequest
{
    public int WriterEpoch { get; init; }
    public string? Proof { get; init; }
}

public sealed record AttachmentTakeoverRequest
{
    public int ExpectedWriterEpoch { get; init; }
    public string? IdempotencyKey { get; init; }
    public string? Proof { get; init; }
}

public sealed record AttachmentCancelRequest
{
    public string? Reason { get; init; }
}

public sealed record PresenceDto
{
    public string UserNId { get; init; } = string.Empty;
    public string State { get; init; } = "Offline";
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Revision { get; init; }
    public DateTimeOffset ObservedOn { get; init; }
    public DateTimeOffset ExpiresOn { get; init; }
}

public sealed record PresenceUpdateRequest
{
    public string? State { get; init; }
}

public sealed record ComplianceScopeDto
{
    public int SchemaVersion { get; init; } = 1;
    public string ScopeType { get; init; } = "Conversation";
    public string? ConversationNId { get; init; }
    public IReadOnlyList<string>? ConversationNIds { get; init; }
    public IReadOnlyList<string>? UserNIds { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? Until { get; init; }
    public DateTimeOffset? FromOn { get; init; }
    public DateTimeOffset? ToOn { get; init; }
    public IReadOnlyList<string>? MessageNIds { get; init; }
}

public sealed record ComplianceSearchRequest
{
    public ComplianceScopeDto? Scope { get; init; }
    public string? Keyword { get; init; }
    public string? Cursor { get; init; }
    public int PageSize { get; init; } = 20;
    public int? Limit { get; init; }
    public string? RequestNId { get; init; }
    public string? Reason { get; init; }
    public string? CaseReference { get; init; }
    public bool ReadOriginal { get; init; }
}

public sealed record StepUpContextRequest
{
    public string? Action { get; init; }
    public string? TargetNId { get; init; }
    public ComplianceScopeDto? Scope { get; init; }
    public string? ScopeChecksum { get; init; }
    public string? RequestNId { get; init; }
    public string? Reason { get; init; }
    public string? Keyword { get; init; }
    public bool? ReadOriginal { get; init; }
    public string? CaseReference { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectNId { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public IReadOnlyList<string>? Fields { get; init; }
    public int? MessageRetentionDays { get; init; }
    public int? AttachmentRetentionDays { get; init; }
    public int? AuditRetentionDays { get; init; }
    public bool? Enabled { get; init; }
    public int? ExportRetentionHours { get; init; }
    public int? ReviewDueHours { get; init; }
    [JsonConverter(typeof(CollaborationNullableInt64StringConverter))]
    public long? ExpectedOptimisticVersion { get; init; }
    public Guid? ExpectedConcurrencyVersion { get; init; }
}

public sealed record StepUpContextDto
{
    public string RequestNId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string ScopeChecksum { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public string Binding { get; init; } = string.Empty;
    public DateTimeOffset ExpiresOn { get; init; }
}

public sealed record ComplianceMessageDto
{
    public string ConversationNId { get; init; } = string.Empty;
    public string MessageNId { get; init; } = string.Empty;
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Sequence { get; init; }
    public string SenderUserNId { get; init; } = string.Empty;
    public string MessageType { get; init; } = string.Empty;
    public string? TextContent { get; init; }
    public DateTimeOffset AcceptedOn { get; init; }
    public string State { get; init; } = "Accepted";
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long MessageStateVersion { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long OptimisticVersion { get; init; }
    public Guid ConcurrencyVersion { get; init; }
    public string? AttachmentNId { get; init; }
}

public sealed record ComplianceSearchPageDto
{
    public IReadOnlyList<ComplianceMessageDto> Items { get; init; } = [];
    public string? NextCursor { get; init; }
    public string ScopeChecksum { get; init; } = string.Empty;
    public int RemainingViewBudget { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
}

public sealed record LegalHoldPageDto
{
    public IReadOnlyList<LegalHoldCaseDto> Items { get; init; } = [];
    public string? NextCursor { get; init; }
}

public sealed record ComplianceExportPageDto
{
    public IReadOnlyList<ComplianceExportDto> Items { get; init; } = [];
    public string? NextCursor { get; init; }
}

public sealed record ComplianceDispositionDto
{
    public string DispositionNId { get; init; } = string.Empty;
    public string MessageNId { get; init; } = string.Empty;
    public string SubjectType { get; init; } = string.Empty;
    public string SubjectNId { get; init; } = string.Empty;
    public string State { get; init; } = "Active";
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public string CreatedByUserNId { get; init; } = string.Empty;
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long DispositionVersion { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long MessageStateVersion { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long OptimisticVersion { get; init; }
    public Guid ConcurrencyVersion { get; init; }
}

public sealed record CreateDispositionRequest
{
    public string? MessageNId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectNId { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public string? RequestNId { get; init; }
    public string? CaseReference { get; init; }
    [JsonConverter(typeof(CollaborationNullableInt64StringConverter))]
    public long? ExpectedOptimisticVersion { get; init; }
    public Guid? ExpectedConcurrencyVersion { get; init; }
}

public sealed record LegalHoldCaseDto
{
    public string HoldCaseNId { get; init; } = string.Empty;
    public string State { get; init; } = "Draft";
    public string Reason { get; init; } = string.Empty;
    public ComplianceScopeDto Scope { get; init; } = new();
    public string CreatedByUserNId { get; init; } = string.Empty;
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset? ReleasedOn { get; init; }
    public string? OperationId { get; init; }
    public string ScopeChecksum { get; init; } = string.Empty;
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long OptimisticVersion { get; init; }
    public Guid ConcurrencyVersion { get; init; }
    public string? ExternalCaseReference { get; init; }
    public string? FileSyncState { get; init; }
    public string? ReviewedByUserNId { get; init; }
    public DateTimeOffset? ReviewedOn { get; init; }
    public string? ReleaseRequestedByUserNId { get; init; }
    public DateTimeOffset? ReleaseApprovalExpiresOn { get; init; }
    public string? ReleaseApprovedByUserNId { get; init; }
}

public sealed record CreateLegalHoldRequest
{
    public ComplianceScopeDto? Scope { get; init; }
    public string? Reason { get; init; }
    public string? RequestNId { get; init; }
    public string? CaseReference { get; init; }
}

public sealed record UpdateLegalHoldRequest
{
    public string? Action { get; init; }
    public string? Reason { get; init; }
    public string? RequestNId { get; init; }
    public string? ScopeChecksum { get; init; }
    public string? CaseReference { get; init; }
    [JsonConverter(typeof(CollaborationNullableInt64StringConverter))]
    public long? ExpectedOptimisticVersion { get; init; }
    public Guid? ExpectedConcurrencyVersion { get; init; }
}

public sealed record ComplianceExportDto
{
    public string ExportNId { get; init; } = string.Empty;
    public string State { get; init; } = "Prepared";
    public string ScopeChecksum { get; init; } = string.Empty;
    public string? DownloadToken { get; init; }
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public string? OperationId { get; init; }
    public ComplianceScopeDto? Scope { get; init; }
    public string? CaseReference { get; init; }
    public string? RequestedByUserNId { get; init; }
    public string? ApprovedByUserNId { get; init; }
    public DateTimeOffset? ApprovedOn { get; init; }
    public DateTimeOffset? ApprovalExpiresOn { get; init; }
    public DateTimeOffset? ApprovalConsumedOn { get; init; }
    public DateTimeOffset? RunDeadlineOn { get; init; }
    public DateTimeOffset? CompletedOn { get; init; }
    public string? ErrorCode { get; init; }
    public int ExportRetentionHours { get; init; }
    public IReadOnlyList<string> Fields { get; init; } = [];
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long OptimisticVersion { get; init; }
    public Guid ConcurrencyVersion { get; init; }
}

public sealed record PrepareComplianceExportRequest
{
    public ComplianceScopeDto? Scope { get; init; }
    public string? Reason { get; init; }
    public string? RequestNId { get; init; }
    public string? CaseReference { get; init; }
    public IReadOnlyList<string>? Fields { get; init; }
}

public sealed record ApproveComplianceExportRequest
{
    public string? Reason { get; init; }
    public string? RequestNId { get; init; }
    public string? ScopeChecksum { get; init; }
    [JsonConverter(typeof(CollaborationNullableInt64StringConverter))]
    public long? ExpectedOptimisticVersion { get; init; }
    public Guid? ExpectedConcurrencyVersion { get; init; }
}

public sealed record ComplianceDownloadAuthorizationRequest
{
    public string? RequestNId { get; init; }
}

public sealed record ComplianceDownloadAuthorizationDto
{
    public object Authorization { get; init; } = new();
    public DateTimeOffset ExpiresOn { get; init; }
}

public sealed record RetentionPolicyDto
{
    public string PolicyNId { get; init; } = string.Empty;
    public int MessageRetentionDays { get; init; }
    public int AttachmentRetentionDays { get; init; }
    public int AuditRetentionDays { get; init; }
    public bool Enabled { get; init; }
    public int ExportRetentionHours { get; init; }
    public int ReviewDueHours { get; init; }
    public string Status { get; init; } = "Active";
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long OptimisticVersion { get; init; }
    public Guid ConcurrencyVersion { get; init; }
}

public sealed record UpdateRetentionPolicyRequest
{
    public int? MessageRetentionDays { get; init; }
    public int? AttachmentRetentionDays { get; init; }
    public int? AuditRetentionDays { get; init; }
    public bool? Enabled { get; init; }
    public int? ExportRetentionHours { get; init; }
    public int? ReviewDueHours { get; init; }
    public string? RequestNId { get; init; }
    public string? Reason { get; init; }
    [JsonConverter(typeof(CollaborationNullableInt64StringConverter))]
    public long? ExpectedOptimisticVersion { get; init; }
    public Guid? ExpectedConcurrencyVersion { get; init; }
}

public sealed record CollaborationHealthDto(string Service, string Status, string SchemaVersion, bool Ready);
