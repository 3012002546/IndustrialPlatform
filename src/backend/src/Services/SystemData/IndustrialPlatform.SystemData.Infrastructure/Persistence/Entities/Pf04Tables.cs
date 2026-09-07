using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

[SugarTable("system_file_upload_session")]
public sealed class FileUploadSessionTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "session_n_id")] public string SessionNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "transport_id")] public string TransportId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "uploader_user_n_id")] public string UploaderUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "purpose")] public string Purpose { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "file_name")] public string FileName { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "content_type")] public string ContentType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "expected_length")] public long ExpectedLength { get; set; }
    [SugarColumn(ColumnName = "expected_sha256")] public string? ExpectedSha256 { get; set; }
    [SugarColumn(ColumnName = "sample_fingerprint")] public string? SampleFingerprint { get; set; }
    [SugarColumn(ColumnName = "current_offset")] public long CurrentOffset { get; set; }
    [SugarColumn(ColumnName = "writer_epoch")] public int WriterEpoch { get; set; }
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset ExpiresOn { get; set; }
    [SugarColumn(ColumnName = "completed_on")] public DateTimeOffset? CompletedOn { get; set; }
    [SugarColumn(ColumnName = "file_n_id")] public string? FileNId { get; set; }
    [SugarColumn(ColumnName = "error_code")] public string? ErrorCode { get; set; }
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
}

[SugarTable("system_file_object")]
public sealed class FileObjectTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "file_n_id")] public string FileNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "upload_session_n_id")] public string UploadSessionNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "owner_user_n_id")] public string OwnerUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "file_name")] public string FileName { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "content_type")] public string ContentType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "content_length")] public long ContentLength { get; set; }
    [SugarColumn(ColumnName = "sha256")] public string Sha256 { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "storage_key")] public string StorageKey { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "scan_status")] public string ScanStatus { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "restricted")] public bool Restricted { get; set; }
    [SugarColumn(ColumnName = "deletion_status")] public string DeletionStatus { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
    [SugarColumn(ColumnName = "deleted_on")] public DateTimeOffset? DeletedOn { get; set; }
    [SugarColumn(ColumnName = "retention_until")] public DateTimeOffset? RetentionUntil { get; set; }
}

[SugarTable("system_file_scan_attempt")]
public sealed class FileScanAttemptTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "scan_n_id")] public string ScanNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "file_n_id")] public string FileNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "engine")] public string Engine { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "started_on")] public DateTimeOffset StartedOn { get; set; }
    [SugarColumn(ColumnName = "completed_on")] public DateTimeOffset? CompletedOn { get; set; }
    [SugarColumn(ColumnName = "detail")] public string? Detail { get; set; }
}

[SugarTable("system_file_reference_grant")]
public sealed class FileReferenceGrantTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "reference_n_id")] public string ReferenceNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "file_n_id")] public string FileNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "owner_user_n_id")] public string OwnerUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "purpose")] public string Purpose { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "deleted_on")] public DateTimeOffset? DeletedOn { get; set; }
}

[SugarTable("system_notification_announcement")]
public sealed class NotificationAnnouncementTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "announcement_n_id")] public string AnnouncementNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "title")] public string Title { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "body")] public string Body { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "priority")] public int Priority { get; set; }
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "audience_user_n_ids_json")] public string AudienceUserNIdsJson { get; set; } = "[]";
    [SugarColumn(ColumnName = "published_on")] public DateTimeOffset? PublishedOn { get; set; }
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset? ExpiresOn { get; set; }
    [SugarColumn(ColumnName = "created_by_user_n_id")] public string CreatedByUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
    [SugarColumn(ColumnName = "revoked_on")] public DateTimeOffset? RevokedOn { get; set; }
    [SugarColumn(ColumnName = "resource_n_id")] public string? ResourceNId { get; set; }
    [SugarColumn(ColumnName = "target_route")] public string? TargetRoute { get; set; }
    [SugarColumn(ColumnName = "idempotency_key")] public string? IdempotencyKey { get; set; }
}

[SugarTable("system_notification_message")]
public sealed class NotificationMessageTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "notification_n_id")] public string NotificationNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "announcement_n_id")] public string? AnnouncementNId { get; set; }
    [SugarColumn(ColumnName = "kind")] public string Kind { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "title")] public string Title { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "body")] public string Body { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "sender_user_n_id")] public string SenderUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset? ExpiresOn { get; set; }
    [SugarColumn(ColumnName = "revoked_on")] public DateTimeOffset? RevokedOn { get; set; }
    [SugarColumn(ColumnName = "resource_n_id")] public string? ResourceNId { get; set; }
    [SugarColumn(ColumnName = "target_route")] public string? TargetRoute { get; set; }
    [SugarColumn(ColumnName = "idempotency_key")] public string? IdempotencyKey { get; set; }
}

[SugarTable("system_notification_inbox_delivery")]
public sealed class NotificationInboxDeliveryTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "notification_n_id")] public string NotificationNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "recipient_user_n_id")] public string RecipientUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "delivered_on")] public DateTimeOffset DeliveredOn { get; set; }
    [SugarColumn(ColumnName = "read_on")] public DateTimeOffset? ReadOn { get; set; }
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset? ExpiresOn { get; set; }
    [SugarColumn(ColumnName = "revoked_on")] public DateTimeOffset? RevokedOn { get; set; }
}

[SugarTable("system_audit_fact")]
public sealed class AuditFactTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "producer_service_key")] public string ProducerServiceKey { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "audit_event_n_id")] public string AuditEventNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "occurred_on")] public DateTimeOffset OccurredOn { get; set; }
    [SugarColumn(ColumnName = "received_on")] public DateTimeOffset ReceivedOn { get; set; }
    [SugarColumn(ColumnName = "actor_user_n_id")] public string? ActorUserNId { get; set; }
    [SugarColumn(ColumnName = "action")] public string Action { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "object_type")] public string ObjectType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "object_n_id")] public string? ObjectNId { get; set; }
    [SugarColumn(ColumnName = "payload_json")] public string PayloadJson { get; set; } = "{}";
    [SugarColumn(ColumnName = "payload_hash")] public string PayloadHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "trace_id")] public string? TraceId { get; set; }
    [SugarColumn(ColumnName = "severity")] public string Severity { get; set; } = "Info";
    [SugarColumn(ColumnName = "source_ip")] public string? SourceIp { get; set; }
    [SugarColumn(ColumnName = "user_agent")] public string? UserAgent { get; set; }
}

[SugarTable("system_audit_lifecycle")]
public sealed class AuditLifecycleTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "producer_service_key")] public string ProducerServiceKey { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "audit_event_n_id")] public string AuditEventNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "state")] public string State { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "retention_until")] public DateTimeOffset? RetentionUntil { get; set; }
    [SugarColumn(ColumnName = "archived_on")] public DateTimeOffset? ArchivedOn { get; set; }
    [SugarColumn(ColumnName = "deleted_on")] public DateTimeOffset? DeletedOn { get; set; }
    [SugarColumn(ColumnName = "legal_hold")] public bool LegalHold { get; set; }
    [SugarColumn(ColumnName = "legal_hold_reason")] public string? LegalHoldReason { get; set; }
    [SugarColumn(ColumnName = "changed_on")] public DateTimeOffset ChangedOn { get; set; }
}

[SugarTable("system_audit_ingress_failure")]
public sealed class AuditIngressFailureTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "failure_n_id")] public string FailureNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "producer_service_key")] public string ProducerServiceKey { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "audit_event_n_id")] public string? AuditEventNId { get; set; }
    [SugarColumn(ColumnName = "error_code")] public string ErrorCode { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "error_summary")] public string ErrorSummary { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "payload_hash")] public string? PayloadHash { get; set; }
    [SugarColumn(ColumnName = "occurred_on")] public DateTimeOffset OccurredOn { get; set; }
}

[SugarTable("system_audit_outbox")]
public sealed class AuditOutboxTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "event_id")] public Guid EventId { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "payload")] public string Payload { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "published_on")] public DateTimeOffset? PublishedOn { get; set; }
    [SugarColumn(ColumnName = "retry_count")] public int RetryCount { get; set; }
    [SugarColumn(ColumnName = "last_error")] public string? LastError { get; set; }
    [SugarColumn(ColumnName = "next_attempt_on")] public DateTimeOffset? NextAttemptOn { get; set; }
    [SugarColumn(ColumnName = "dead_on")] public DateTimeOffset? DeadOn { get; set; }
}
