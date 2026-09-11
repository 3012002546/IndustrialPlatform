using SqlSugar;

namespace IndustrialPlatform.Collaboration.Infrastructure.Persistence;

internal abstract class CommonCollaborationTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "is_frozen")] public bool IsFrozen { get; set; }
    [SugarColumn(ColumnName = "is_locked")] public bool IsLocked { get; set; }
    [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "entity_type")] public string EntityType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
    [SugarColumn(ColumnName = "optimistic_version")] public long OptimisticVersion { get; set; }
    [SugarColumn(ColumnName = "concurrency_version")] public Guid ConcurrencyVersion { get; set; }
}

[SugarTable("collaboration_conversation")]
internal sealed class ConversationTable : CommonCollaborationTable
{
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "n_id")] public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "participant_low_user_n_id")] public string ParticipantLowUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "participant_high_user_n_id")] public string ParticipantHighUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = "Active";
    [SugarColumn(ColumnName = "last_message_sequence")] public long LastMessageSequence { get; set; }
    [SugarColumn(ColumnName = "last_message_n_id")] public string? LastMessageNId { get; set; }
    [SugarColumn(ColumnName = "last_message_on")] public DateTimeOffset? LastMessageOn { get; set; }
    [SugarColumn(ColumnName = "retention_floor_sequence")] public long RetentionFloorSequence { get; set; }
}

[SugarTable("collaboration_conversation_member")]
internal sealed class ConversationMemberTable : CommonCollaborationTable
{
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "conversation_n_id")] public string ConversationNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "user_n_id")] public string UserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "display_name_snapshot")] public string DisplayNameSnapshot { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "joined_on")] public DateTimeOffset JoinedOn { get; set; }
    [SugarColumn(ColumnName = "visibility_state")] public string VisibilityState { get; set; } = "Visible";
    [SugarColumn(ColumnName = "hidden_on")] public DateTimeOffset? HiddenOn { get; set; }
    [SugarColumn(ColumnName = "hidden_through_sequence")] public long HiddenThroughSequence { get; set; }
    [SugarColumn(ColumnName = "last_read_sequence")] public long LastReadSequence { get; set; }
    [SugarColumn(ColumnName = "last_read_on")] public DateTimeOffset? LastReadOn { get; set; }
    [SugarColumn(ColumnName = "unread_count")] public long UnreadCount { get; set; }
    [SugarColumn(ColumnName = "projection_version")] public long ProjectionVersion { get; set; }
}

[SugarTable("collaboration_message")]
internal sealed class MessageTable : CommonCollaborationTable
{
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "conversation_n_id")] public string ConversationNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "message_n_id")] public string MessageNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "sequence")] public long Sequence { get; set; }
    [SugarColumn(ColumnName = "sender_user_n_id")] public string SenderUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "client_message_n_id")] public string ClientMessageNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "request_hash")] public string RequestHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "message_type")] public string MessageType { get; set; } = "Text";
    [SugarColumn(ColumnName = "text_content")] public string? TextContent { get; set; }
    [SugarColumn(ColumnName = "reply_to_message_n_id")] public string? ReplyToMessageNId { get; set; }
    [SugarColumn(ColumnName = "attachment_n_id")] public string? AttachmentNId { get; set; }
    [SugarColumn(ColumnName = "accepted_on")] public DateTimeOffset AcceptedOn { get; set; }
    [SugarColumn(ColumnName = "retracted_on")] public DateTimeOffset? RetractedOn { get; set; }
    [SugarColumn(ColumnName = "retracted_by_user_n_id")] public string? RetractedByUserNId { get; set; }
    [SugarColumn(ColumnName = "retraction_reason")] public string? RetractionReason { get; set; }
    [SugarColumn(ColumnName = "message_state_version")] public int MessageStateVersion { get; set; }
}

[SugarTable("collaboration_message_personal_visibility")]
internal sealed class MessagePersonalVisibilityTable
{
    [SugarColumn(ColumnName = "tenant_n_id", IsPrimaryKey = true)] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "user_n_id", IsPrimaryKey = true)] public string UserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "conversation_n_id")] public string ConversationNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "message_n_id", IsPrimaryKey = true)] public string MessageNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "hidden_on")] public DateTimeOffset HiddenOn { get; set; }
}

[SugarTable("collaboration_chat_attachment")]
internal sealed class AttachmentTable : CommonCollaborationTable
{
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "conversation_n_id")] public string ConversationNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "attachment_n_id")] public string AttachmentNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "uploader_user_n_id")] public string UploaderUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "file_n_id")] public string? FileNId { get; set; }
    [SugarColumn(ColumnName = "file_name_snapshot")] public string FileNameSnapshot { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "content_type_snapshot")] public string ContentTypeSnapshot { get; set; } = "application/octet-stream";
    [SugarColumn(ColumnName = "size_snapshot")] public long SizeSnapshot { get; set; }
    [SugarColumn(ColumnName = "purpose")] public string Purpose { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "file_state_projection")] public string FileStateProjection { get; set; } = "Pending";
    [SugarColumn(ColumnName = "file_state_version")] public int? FileStateVersion { get; set; }
    [SugarColumn(ColumnName = "file_observed_on")] public DateTimeOffset? FileObservedOn { get; set; }
    [SugarColumn(ColumnName = "reference_state")] public string ReferenceState { get; set; } = "Pending";
    [SugarColumn(ColumnName = "retention_state")] public string RetentionState { get; set; } = "Active";
    [SugarColumn(ColumnName = "bound_message_n_id")] public string? BoundMessageNId { get; set; }
    [SugarColumn(ColumnName = "intent_request_n_id")] public string IntentRequestNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "intent_request_hash")] public string IntentRequestHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "reference_n_id")] public string? ReferenceNId { get; set; }
}

[SugarTable("collaboration_compliance_disposition")]
internal sealed class DispositionTable : CommonCollaborationTable
{
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "disposition_n_id")] public string DispositionNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "subject_type")] public string SubjectType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "subject_n_id")] public string SubjectNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "state")] public string State { get; set; } = "Active";
    [SugarColumn(ColumnName = "reason")] public string Reason { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_by_user_n_id")] public string CreatedByUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset? ExpiresOn { get; set; }
    [SugarColumn(ColumnName = "request_n_id")] public string? RequestNId { get; set; }
    [SugarColumn(ColumnName = "request_hash")] public string? RequestHash { get; set; }
}

[SugarTable("collaboration_legal_hold_case")]
internal sealed class LegalHoldTable : CommonCollaborationTable
{
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "hold_case_n_id")] public string HoldCaseNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "state")] public string State { get; set; } = "Active";
    [SugarColumn(ColumnName = "scope_json")] public string ScopeJson { get; set; } = "{}";
    [SugarColumn(ColumnName = "scope_checksum")] public string ScopeChecksum { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "reason")] public string Reason { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_by_user_n_id")] public string CreatedByUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "released_on")] public DateTimeOffset? ReleasedOn { get; set; }
    [SugarColumn(ColumnName = "request_n_id")] public string? RequestNId { get; set; }
    [SugarColumn(ColumnName = "request_hash")] public string? RequestHash { get; set; }
    [SugarColumn(ColumnName = "operation_id")] public string? OperationId { get; set; }
    [SugarColumn(ColumnName = "release_requested_by_user_n_id")] public string? ReleaseRequestedByUserNId { get; set; }
    [SugarColumn(ColumnName = "release_request_n_id")] public string? ReleaseRequestNId { get; set; }
    [SugarColumn(ColumnName = "release_approval_expires_on")] public DateTimeOffset? ReleaseApprovalExpiresOn { get; set; }
    [SugarColumn(ColumnName = "release_approved_by_user_n_id")] public string? ReleaseApprovedByUserNId { get; set; }
    [SugarColumn(ColumnName = "reviewed_by_user_n_id")] public string? ReviewedByUserNId { get; set; }
    [SugarColumn(ColumnName = "reviewed_on")] public DateTimeOffset? ReviewedOn { get; set; }
    [SugarColumn(ColumnName = "external_case_reference")] public string? ExternalCaseReference { get; set; }
    [SugarColumn(ColumnName = "file_sync_state")] public string FileSyncState { get; set; } = "Pending";
}

[SugarTable("collaboration_compliance_export")]
internal sealed class ExportTable : CommonCollaborationTable
{
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "export_n_id")] public string ExportNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "state")] public string State { get; set; } = "Prepared";
    [SugarColumn(ColumnName = "scope_json")] public string ScopeJson { get; set; } = "{}";
    [SugarColumn(ColumnName = "scope_checksum")] public string ScopeChecksum { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "reason")] public string Reason { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_by_user_n_id")] public string CreatedByUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset? ExpiresOn { get; set; }
    [SugarColumn(ColumnName = "artifact_reference")] public string? ArtifactReference { get; set; }
    [SugarColumn(ColumnName = "request_n_id")] public string? RequestNId { get; set; }
    [SugarColumn(ColumnName = "request_hash")] public string? RequestHash { get; set; }
    [SugarColumn(ColumnName = "case_reference")] public string? CaseReference { get; set; }
    [SugarColumn(ColumnName = "approved_by_user_n_id")] public string? ApprovedByUserNId { get; set; }
    [SugarColumn(ColumnName = "approved_on")] public DateTimeOffset? ApprovedOn { get; set; }
    [SugarColumn(ColumnName = "approval_expires_on")] public DateTimeOffset? ApprovalExpiresOn { get; set; }
    [SugarColumn(ColumnName = "approval_consumed_on")] public DateTimeOffset? ApprovalConsumedOn { get; set; }
    [SugarColumn(ColumnName = "run_deadline_on")] public DateTimeOffset? RunDeadlineOn { get; set; }
    [SugarColumn(ColumnName = "completed_on")] public DateTimeOffset? CompletedOn { get; set; }
    [SugarColumn(ColumnName = "error_code")] public string? ErrorCode { get; set; }
    [SugarColumn(ColumnName = "fields_json")] public string? FieldsJson { get; set; }
    [SugarColumn(ColumnName = "operation_id")] public string? OperationId { get; set; }
    [SugarColumn(ColumnName = "export_retention_hours")] public int ExportRetentionHours { get; set; }
    [SugarColumn(ColumnName = "worker_lease_n_id")] public string? WorkerLeaseNId { get; set; }
    [SugarColumn(ColumnName = "worker_lease_until")] public DateTimeOffset? WorkerLeaseUntil { get; set; }
}

[SugarTable("collaboration_retention_policy")]
internal sealed class RetentionPolicyTable : CommonCollaborationTable
{
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "policy_n_id")] public string PolicyNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "message_retention_days")] public int MessageRetentionDays { get; set; }
    [SugarColumn(ColumnName = "attachment_retention_days")] public int AttachmentRetentionDays { get; set; }
    [SugarColumn(ColumnName = "audit_retention_days")] public int AuditRetentionDays { get; set; }
    [SugarColumn(ColumnName = "enabled")] public bool Enabled { get; set; }
    [SugarColumn(ColumnName = "export_retention_hours")] public int ExportRetentionHours { get; set; }
    [SugarColumn(ColumnName = "review_due_hours")] public int ReviewDueHours { get; set; }
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = "Active";
}

[SugarTable("collaboration_retention_checkpoint")]
internal sealed class RetentionCheckpointTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "operation_n_id")] public string OperationNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "cutoff_on")] public DateTimeOffset CutoffOn { get; set; }
    [SugarColumn(ColumnName = "last_accepted_on")] public DateTimeOffset? LastAcceptedOn { get; set; }
    [SugarColumn(ColumnName = "last_message_id")] public Guid? LastMessageId { get; set; }
    [SugarColumn(ColumnName = "stage")] public string Stage { get; set; } = "Inspect";
    [SugarColumn(ColumnName = "lease_owner")] public string? LeaseOwner { get; set; }
    [SugarColumn(ColumnName = "lease_expires_on")] public DateTimeOffset? LeaseExpiresOn { get; set; }
    [SugarColumn(ColumnName = "updated_on")] public DateTimeOffset UpdatedOn { get; set; }
    [SugarColumn(ColumnName = "error_code")] public string? ErrorCode { get; set; }
}

[SugarTable("collaboration_compliance_view_budget")]
internal sealed class ComplianceViewBudgetTable
{
    [SugarColumn(ColumnName = "tenant_n_id", IsPrimaryKey = true)] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "actor_user_n_id", IsPrimaryKey = true)] public string ActorUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "window_start_on", IsPrimaryKey = true)] public DateTimeOffset WindowStartOn { get; set; }
    [SugarColumn(ColumnName = "result_count")] public int ResultCount { get; set; }
}

[SugarTable("collaboration_compliance_preparation")]
internal sealed class CompliancePreparationTable
{
    [SugarColumn(ColumnName = "tenant_n_id", IsPrimaryKey = true)] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "actor_user_n_id", IsPrimaryKey = true)] public string ActorUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "request_n_id", IsPrimaryKey = true)] public string RequestNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "actor_session_n_id")] public string ActorSessionNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "action")] public string Action { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "target_n_id")] public string? TargetNId { get; set; }
    [SugarColumn(ColumnName = "request_hash")] public string RequestHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "scope_checksum")] public string ScopeChecksum { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "command_json")] public string CommandJson { get; set; } = "{}";
    [SugarColumn(ColumnName = "snapshot_json")] public string SnapshotJson { get; set; } = "{}";
    [SugarColumn(ColumnName = "prepared_on")] public DateTimeOffset PreparedOn { get; set; }
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset ExpiresOn { get; set; }
}

[SugarTable("collaboration_compliance_command")]
internal sealed class ComplianceCommandTable
{
    [SugarColumn(ColumnName = "tenant_n_id", IsPrimaryKey = true)] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "actor_user_n_id", IsPrimaryKey = true)] public string ActorUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "request_n_id", IsPrimaryKey = true)] public string RequestNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "action")] public string Action { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "target_n_id")] public string? TargetNId { get; set; }
    [SugarColumn(ColumnName = "request_hash")] public string RequestHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "scope_checksum")] public string ScopeChecksum { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "command_json")] public string? CommandJson { get; set; }
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = "Completed";
    [SugarColumn(ColumnName = "result_json")] public string? ResultJson { get; set; }
    [SugarColumn(ColumnName = "completed_on")] public DateTimeOffset? CompletedOn { get; set; }
}

[SugarTable("collaboration_outbox_message")]
internal sealed class CollaborationOutboxTable
{
    [SugarColumn(ColumnName = "event_id", IsPrimaryKey = true)] public Guid EventId { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "event_type")] public string EventType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "payload")] public string Payload { get; set; } = "{}";
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "published_on")] public DateTimeOffset? PublishedOn { get; set; }
    [SugarColumn(ColumnName = "retry_count")] public int RetryCount { get; set; }
    [SugarColumn(ColumnName = "last_error")] public string? LastError { get; set; }
    [SugarColumn(ColumnName = "lease_n_id")] public string? LeaseNId { get; set; }
    [SugarColumn(ColumnName = "lease_until")] public DateTimeOffset? LeaseUntil { get; set; }
    [SugarColumn(ColumnName = "dead_lettered_on")] public DateTimeOffset? DeadLetteredOn { get; set; }
}

[SugarTable("collaboration_event_inbox")]
internal sealed class CollaborationEventInboxTable
{
    [SugarColumn(ColumnName = "event_id", IsPrimaryKey = true)] public Guid EventId { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "event_type")] public string EventType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = "Processing";
    [SugarColumn(ColumnName = "received_on")] public DateTimeOffset ReceivedOn { get; set; }
    [SugarColumn(ColumnName = "processed_on")] public DateTimeOffset? ProcessedOn { get; set; }
    [SugarColumn(ColumnName = "retry_count")] public int RetryCount { get; set; }
    [SugarColumn(ColumnName = "last_error")] public string? LastError { get; set; }
    [SugarColumn(ColumnName = "lease_n_id")] public string? LeaseNId { get; set; }
    [SugarColumn(ColumnName = "lease_until")] public DateTimeOffset? LeaseUntil { get; set; }
}
