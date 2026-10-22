using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

[SugarTable("system_collaboration_file_reference")]
public sealed class CollaborationFileReferenceTable
{
    [SugarColumn(ColumnName = "tenant_n_id", IsPrimaryKey = true)] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "reference_n_id", IsPrimaryKey = true)] public string ReferenceNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "file_n_id")] public string FileNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "conversation_n_id")] public string ConversationNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "message_n_id")] public string MessageNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "attachment_n_id")] public string AttachmentNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "uploader_user_n_id")] public string UploaderUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "purpose")] public string Purpose { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "owner_service")] public string OwnerService { get; set; } = "collaboration";
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = "Active";
    [SugarColumn(ColumnName = "version")] public long Version { get; set; }
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "released_on")] public DateTimeOffset? ReleasedOn { get; set; }
}

[SugarTable("system_collaboration_file_hold")]
public sealed class CollaborationFileHoldTable
{
    [SugarColumn(ColumnName = "tenant_n_id", IsPrimaryKey = true)] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "case_n_id", IsPrimaryKey = true)] public string CaseNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "file_n_id", IsPrimaryKey = true)] public string FileNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "scope_checksum")] public string ScopeChecksum { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "case_revision")] public long CaseRevision { get; set; }
    [SugarColumn(ColumnName = "owner_service")] public string OwnerService { get; set; } = "collaboration";
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = "Active";
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "updated_on")] public DateTimeOffset UpdatedOn { get; set; }
    [SugarColumn(ColumnName = "released_on")] public DateTimeOffset? ReleasedOn { get; set; }
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
