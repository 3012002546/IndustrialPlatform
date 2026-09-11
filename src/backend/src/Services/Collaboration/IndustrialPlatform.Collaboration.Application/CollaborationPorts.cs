using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.Security;

namespace IndustrialPlatform.Collaboration.Application;

public class CollaborationException : Exception
{
    public CollaborationException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}

public sealed class EventInboxBusyException : CollaborationException, IEventBusDeferredRetryFailure
{
    public EventInboxBusyException(string message)
        : base(409, "COLLAB_EVENT_INBOX_BUSY", message)
    {
    }
}

public sealed record DirectoryUser(string UserNId, string DisplayName, string Status, string SecurityVersion);

public sealed record DirectorySearchPage(IReadOnlyList<DirectoryUser> Items, string? NextCursor);

public interface ICollaborationIdentityDirectory
{
    Task<DirectoryUser?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken);
    Task<DirectorySearchPage> SearchAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken);
    async Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(string tenantNId, IReadOnlyCollection<string> userNIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var userNId in userNIds.Distinct(StringComparer.Ordinal))
        {
            var user = await GetAsync(tenantNId, userNId, cancellationToken);
            if (user is not null) names[userNId] = user.DisplayName;
        }
        return names;
    }
}

public interface ICollaborationPermissionEvaluator
{
    Task<bool> IsSystemAdministratorAsync(string tenantNId, string actorUserNId, string actorSessionNId,
        string actorSecurityVersion, CancellationToken cancellationToken) => Task.FromResult(false);

    Task<bool> HasPermissionAsync(
        string permission,
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        CancellationToken cancellationToken);
}

public sealed record ConversationRecord(
    string TenantNId,
    string ConversationNId,
    string ParticipantLowUserNId,
    string ParticipantHighUserNId,
    string Status,
    long LastMessageSequence,
    string? LastMessageNId,
    DateTimeOffset? LastMessageOn,
    long RetentionFloorSequence,
    long OptimisticVersion,
    Guid ConcurrencyVersion);

public sealed record ConversationMemberRecord(
    string TenantNId,
    string ConversationNId,
    string UserNId,
    string DisplayNameSnapshot,
    DateTimeOffset JoinedOn,
    string VisibilityState,
    DateTimeOffset? HiddenOn,
    long HiddenThroughSequence,
    long LastReadSequence,
    DateTimeOffset? LastReadOn,
    long UnreadCount,
    long ProjectionVersion,
    Guid ConcurrencyVersion = default);

public sealed record MessageRecord(
    string TenantNId,
    string ConversationNId,
    string MessageNId,
    long Sequence,
    string SenderUserNId,
    string ClientMessageNId,
    string RequestHash,
    string MessageType,
    string? TextContent,
    string? ReplyToMessageNId,
    string? AttachmentNId,
    DateTimeOffset AcceptedOn,
    DateTimeOffset? RetractedOn,
    string? RetractedByUserNId,
    string? RetractionReason,
    int MessageStateVersion,
    Guid ConcurrencyVersion = default);

public sealed record AttachmentRecord(
    string TenantNId,
    string ConversationNId,
    string AttachmentNId,
    string UploaderUserNId,
    string? FileNId,
    string FileNameSnapshot,
    string ContentTypeSnapshot,
    long SizeSnapshot,
    string Purpose,
    string FileStateProjection,
    int? FileStateVersion,
    DateTimeOffset? FileObservedOn,
    string ReferenceState,
    string RetentionState,
    string? BoundMessageNId,
    string IntentRequestNId,
    string IntentRequestHash,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    string? ReferenceNId = null);

public sealed record ComplianceDispositionRecord(
    string TenantNId,
    string DispositionNId,
    string SubjectType,
    string SubjectNId,
    string State,
    string Reason,
    string CreatedByUserNId,
    DateTimeOffset CreatedOn,
    DateTimeOffset? ExpiresOn,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    string? RequestNId = null,
    string? RequestHash = null);

public sealed record LegalHoldRecord(
    string TenantNId,
    string HoldCaseNId,
    string State,
    string ScopeJson,
    string ScopeChecksum,
    string Reason,
    string CreatedByUserNId,
    DateTimeOffset CreatedOn,
    DateTimeOffset? ReleasedOn,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    string? RequestNId = null,
    string? RequestHash = null,
    string? OperationId = null,
    string? ReleaseRequestedByUserNId = null,
    string? ReleaseRequestNId = null,
    DateTimeOffset? ReleaseApprovalExpiresOn = null,
    string? ReleaseApprovedByUserNId = null,
    string? ReviewedByUserNId = null,
    DateTimeOffset? ReviewedOn = null,
    string? ExternalCaseReference = null,
    string FileSyncState = "Pending");

public sealed record ComplianceExportRecord(
    string TenantNId,
    string ExportNId,
    string State,
    string ScopeJson,
    string ScopeChecksum,
    string Reason,
    string CreatedByUserNId,
    DateTimeOffset CreatedOn,
    DateTimeOffset? ExpiresOn,
    string? ArtifactReference,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    string? RequestNId = null,
    string? RequestHash = null,
    string? CaseReference = null,
    string? ApprovedByUserNId = null,
    DateTimeOffset? ApprovedOn = null,
    DateTimeOffset? ApprovalExpiresOn = null,
    DateTimeOffset? ApprovalConsumedOn = null,
    DateTimeOffset? RunDeadlineOn = null,
    DateTimeOffset? CompletedOn = null,
    string? ErrorCode = null,
    string? FieldsJson = null,
    string? OperationId = null,
    int ExportRetentionHours = 24,
    string? WorkerLeaseNId = null,
    DateTimeOffset? WorkerLeaseUntil = null);

public sealed record RetentionPolicyRecord(
    string TenantNId,
    string PolicyNId,
    int MessageRetentionDays,
    int AttachmentRetentionDays,
    int AuditRetentionDays,
    bool Enabled,
    long OptimisticVersion,
    Guid ConcurrencyVersion,
    int ExportRetentionHours = 24,
    int ReviewDueHours = 24,
    string Status = "Active");

public sealed record ComplianceViewBudgetReservation(
    DateTimeOffset WindowStartOn,
    int ReservedCount,
    int Remaining);

public sealed record CompliancePreparationRecord(
    string TenantNId,
    string ActorUserNId,
    string RequestNId,
    string ActorSessionNId,
    string Action,
    string? TargetNId,
    string RequestHash,
    string ScopeChecksum,
    string CommandJson,
    string SnapshotJson,
    DateTimeOffset PreparedOn,
    DateTimeOffset ExpiresOn);

/// <summary>
/// Canonical, immutable command payload shared by step-up preparation and final execution.
/// Every field that can affect a compliance mutation or export is included in the hash.
/// </summary>
public sealed partial record ComplianceCommandSnapshot(
    string Action,
    string RequestNId,
    string? TargetNId,
    string ScopeChecksum,
    string? Reason,
    string? CaseReference,
    string? Keyword,
    bool ReadOriginal,
    DateTimeOffset? ExpiresOn,
    string? SubjectType,
    string? SubjectNId,
    IReadOnlyList<string> Fields,
    int? MessageRetentionDays,
    int? AttachmentRetentionDays,
    int? AuditRetentionDays,
    bool? Enabled,
    int? ExportRetentionHours,
    int? ReviewDueHours,
    long? ExpectedOptimisticVersion,
    Guid? ExpectedConcurrencyVersion);

public static class ComplianceCommandSnapshotExtensions
{
    public static ComplianceCommandSnapshot BindRequest(this ComplianceCommandSnapshot command, string tenantNId, string actorUserNId, ComplianceScopeDto? scope)
        => command with { TenantNId = tenantNId, ActorUserNId = actorUserNId, Scope = scope };
}

public partial record ComplianceCommandSnapshot
{
    public string? TenantNId { get; init; }
    public string? ActorUserNId { get; init; }
    public ComplianceScopeDto? Scope { get; init; }
}

public static class ComplianceCommandCanonicalizer
{
    public static string Serialize(ComplianceCommandSnapshot command) => JsonSerializer.Serialize(command);

    public static string Hash(ComplianceCommandSnapshot command) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(command)))).ToLowerInvariant();
}

public sealed record ComplianceCommandRecord(
    string TenantNId,
    string ActorUserNId,
    string RequestNId,
    string Action,
    string? TargetNId,
    string RequestHash,
    string ScopeChecksum,
    string Status,
    string? ResultJson,
    DateTimeOffset? CompletedOn,
    string? CommandJson = null);

public sealed record ExportArtifactResult(
    string FileNId,
    string FileName,
    string ContentType,
    long Length,
    string ScanStatus);

public sealed record CollaborationOutboxRecord(
    Guid EventId,
    string TenantNId,
    string EventType,
    string Payload,
    DateTimeOffset CreatedOn,
    DateTimeOffset? PublishedOn,
    int RetryCount,
    string? LastError,
    string? LeaseNId = null,
    DateTimeOffset? LeaseUntil = null,
    DateTimeOffset? DeadLetteredOn = null);

public sealed record CollaborationEventInboxRecord(
    Guid EventId,
    string TenantNId,
    string EventType,
    string Status,
    DateTimeOffset ReceivedOn,
    DateTimeOffset? ProcessedOn,
    int RetryCount,
    string? LastError);

public enum EventInboxClaimStatus
{
    Claimed,
    AlreadyProcessed,
    Busy,
}

public sealed record EventInboxClaimResult(EventInboxClaimStatus Status, string? LeaseNId);

public sealed record CollaborationOutboxHealth(int PendingCount, int DeadLetterCount);

public sealed record RetentionFileReference(
    string AttachmentNId,
    string FileNId,
    string ReferenceNId,
    string UploaderUserNId,
    string OperationNId);

public sealed record RetentionSweepResult(
    int MessagesPurged,
    IReadOnlyList<RetentionFileReference> FilesPendingRelease,
    bool CheckpointCompleted);

public interface ICollaborationRealtimePublisher
{
    Task PublishAsync(CollaborationOutboxRecord message, CancellationToken cancellationToken);
}

public interface ICollaborationRepository
{
    Task<ConversationRecord?> GetConversationAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken);
    Task<ConversationRecord?> FindConversationByPairAsync(string tenantNId, string lowUserNId, string highUserNId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationRecord>> ListConversationsAsync(string tenantNId, string userNId, int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationRecord>> ListConversationsAsync(string tenantNId, string userNId, int page, int pageSize, string visibility, bool unreadOnly, CancellationToken cancellationToken)
        => ListConversationsAsync(tenantNId, userNId, page, pageSize, cancellationToken);
    Task<ConversationRecord> CreateConversationAsync(ConversationRecord conversation, ConversationMemberRecord currentMember, ConversationMemberRecord peerMember, CancellationToken cancellationToken);
    Task<ConversationMemberRecord?> GetMemberAsync(string tenantNId, string conversationNId, string userNId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationMemberRecord>> GetMembersAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken);
    async Task<IReadOnlyList<ConversationMemberRecord>> ListMembersForConversationsAsync(string tenantNId, IReadOnlyCollection<string> conversationNIds, CancellationToken cancellationToken)
    {
        var members = new List<ConversationMemberRecord>();
        foreach (var conversationNId in conversationNIds)
            members.AddRange(await GetMembersAsync(tenantNId, conversationNId, cancellationToken));
        return members;
    }
    Task<IReadOnlyDictionary<string, MessageRecord>> ListLatestVisibleMessagesForUserAsync(string tenantNId, IReadOnlyCollection<string> conversationNIds, string userNId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, MessageRecord>>(new Dictionary<string, MessageRecord>(StringComparer.Ordinal));
    Task<MessageRecord?> FindMessageByClientAsync(string tenantNId, string senderUserNId, string clientMessageNId, CancellationToken cancellationToken);
    Task<MessageRecord?> GetMessageAsync(string tenantNId, string conversationNId, string messageNId, CancellationToken cancellationToken);
    Task<MessageRecord?> GetMessageByIdAsync(string tenantNId, string messageNId, CancellationToken cancellationToken);
    Task<MessageRecord?> GetMessageByAttachmentAsync(string tenantNId, string attachmentNId, CancellationToken cancellationToken);
    Task<MessageRecord> AppendMessageAsync(ConversationRecord conversation, MessageRecord message, CancellationToken cancellationToken);
    async Task<MessageRecord> AppendMessageAndBindAttachmentAsync(ConversationRecord conversation, MessageRecord message, string attachmentNId, CancellationToken cancellationToken)
    {
        var accepted = await AppendMessageAsync(conversation, message, cancellationToken);
        var attachment = await GetAttachmentAsync(accepted.TenantNId, accepted.ConversationNId, attachmentNId, cancellationToken)
            ?? throw new InvalidOperationException("Attachment disappeared while binding the accepted message.");
        if (attachment.BoundMessageNId is not null && !string.Equals(attachment.BoundMessageNId, accepted.MessageNId, StringComparison.Ordinal))
            throw new InvalidOperationException("Attachment is already bound to another message.");
        if (attachment.BoundMessageNId is null)
            await UpdateAttachmentAsync(attachment with { BoundMessageNId = accepted.MessageNId, LastUpdatedOn = accepted.AcceptedOn }, cancellationToken);
        return accepted;
    }
    Task<IReadOnlyList<MessageRecord>> GetMessagesAsync(string tenantNId, string conversationNId, string mode, long? sequence, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<MessageRecord>> GetMessagesAsync(string tenantNId, string conversationNId, string mode, long? sequence, long? fromSequence, long? toSequence, int pageSize, int historyPage, CancellationToken cancellationToken)
        => GetMessagesAsync(tenantNId, conversationNId, mode, sequence, pageSize, cancellationToken);
    async Task<IReadOnlyList<MessageRecord>> GetMessagesAsync(string tenantNId, string conversationNId, string mode, long? sequence, long? fromSequence, long? toSequence, int pageSize, int historyPage, long retentionFloorSequence, CancellationToken cancellationToken)
    {
        var rows = await GetMessagesAsync(tenantNId, conversationNId, mode, sequence, fromSequence, toSequence, pageSize, historyPage, cancellationToken);
        return rows.Where(row => row.Sequence > retentionFloorSequence).ToArray();
    }
    Task<IReadOnlyList<MessageRecord>> GetMessagesForUserAsync(string tenantNId, string conversationNId, string userNId, string mode, long? sequence, int pageSize, CancellationToken cancellationToken)
        => GetMessagesAsync(tenantNId, conversationNId, mode, sequence, pageSize, cancellationToken);
    Task<IReadOnlyList<MessageRecord>> GetMessagesForUserAsync(string tenantNId, string conversationNId, string userNId, string mode, long? sequence, long? fromSequence, long? toSequence, int pageSize, int historyPage, long retentionFloorSequence, CancellationToken cancellationToken)
        => GetMessagesAsync(tenantNId, conversationNId, mode, sequence, fromSequence, toSequence, pageSize, historyPage, retentionFloorSequence, cancellationToken);
    Task<bool> IsMessageHiddenForUserAsync(string tenantNId, string conversationNId, string messageNId, string userNId, CancellationToken cancellationToken)
        => Task.FromResult(false);
    Task<bool> HideMessageForUserAsync(string tenantNId, string conversationNId, string messageNId, string userNId, CancellationToken cancellationToken)
        => Task.FromResult(false);
    Task UpdateReadCursorAsync(string tenantNId, string conversationNId, string userNId, long sequence, CancellationToken cancellationToken);
    Task HideMemberAsync(string tenantNId, string conversationNId, string userNId, long throughSequence, CancellationToken cancellationToken);
    Task<ConversationMemberRecord> HideMemberAsync(string tenantNId, string conversationNId, string userNId, long throughSequence, long? expectedOptimisticVersion, Guid? expectedConcurrencyVersion, CancellationToken cancellationToken)
        => HideMemberAndReadAsync(tenantNId, conversationNId, userNId, throughSequence, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken);
    Task RestoreMemberAsync(string tenantNId, string conversationNId, string userNId, CancellationToken cancellationToken);
    Task<ConversationMemberRecord> RestoreMemberAsync(string tenantNId, string conversationNId, string userNId, long? expectedOptimisticVersion, Guid? expectedConcurrencyVersion, CancellationToken cancellationToken)
        => RestoreMemberAndReadAsync(tenantNId, conversationNId, userNId, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken);
    Task RetractMessageAsync(string tenantNId, string conversationNId, string messageNId, string userNId, string reason, CancellationToken cancellationToken);
    Task<MessageRecord> RetractMessageAsync(string tenantNId, string conversationNId, string messageNId, string userNId, string reason, long? expectedOptimisticVersion, Guid? expectedConcurrencyVersion, CancellationToken cancellationToken)
        => RetractMessageAndReadAsync(tenantNId, conversationNId, messageNId, userNId, reason, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken);
    Task<AttachmentRecord?> GetAttachmentAsync(string tenantNId, string conversationNId, string attachmentNId, CancellationToken cancellationToken);
    Task<AttachmentRecord?> GetAttachmentByIdAsync(string tenantNId, string attachmentNId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AttachmentRecord>> ListAttachmentsByFileAsync(string tenantNId, string fileNId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<AttachmentRecord>>([]);
    Task<AttachmentRecord> CreateAttachmentAsync(AttachmentRecord attachment, CancellationToken cancellationToken);
    Task<AttachmentRecord> UpdateAttachmentAsync(AttachmentRecord attachment, CancellationToken cancellationToken);
    Task<IReadOnlyList<MessageRecord>> SearchComplianceMessagesAsync(string tenantNId, ComplianceScopeDto scope, string? keyword, int page, int pageSize, CancellationToken cancellationToken);
    Task<ComplianceDispositionRecord> CreateDispositionAsync(ComplianceDispositionRecord disposition, CancellationToken cancellationToken);
    Task<IReadOnlyList<ComplianceDispositionRecord>> ListDispositionsAsync(string tenantNId, CancellationToken cancellationToken);
    Task<LegalHoldRecord> CreateLegalHoldAsync(LegalHoldRecord hold, CancellationToken cancellationToken);
    Task<IReadOnlyList<LegalHoldRecord>> ListLegalHoldsAsync(string tenantNId, string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListLegalHoldTenantsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    Task<LegalHoldRecord?> GetLegalHoldAsync(string tenantNId, string holdCaseNId, CancellationToken cancellationToken);
    Task<LegalHoldRecord> UpdateLegalHoldAsync(LegalHoldRecord hold, CancellationToken cancellationToken);
    Task<ComplianceExportRecord> CreateExportAsync(ComplianceExportRecord exportRecord, CancellationToken cancellationToken);
    Task<IReadOnlyList<ComplianceExportRecord>> ListExportsAsync(string tenantNId, string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<ComplianceExportRecord?> GetExportAsync(string tenantNId, string exportNId, CancellationToken cancellationToken);
    Task<ComplianceExportRecord> UpdateExportAsync(ComplianceExportRecord exportRecord, CancellationToken cancellationToken);
    Task<ComplianceExportRecord?> UpdateExportIfOwnedAsync(
        ComplianceExportRecord exportRecord,
        string workerLeaseNId,
        long expectedOptimisticVersion,
        CancellationToken cancellationToken) =>
        Task.FromException<ComplianceExportRecord?>(new NotSupportedException("当前导出持久化适配器未提供租约条件更新。"));
    Task<ComplianceExportRecord?> TryClaimExportAsync(string tenantNId, string exportNId, string workerLeaseNId, DateTimeOffset leaseUntil, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<ComplianceExportRecord?>(null);
    Task<IReadOnlyList<string>> ListExportTenantsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    Task<ComplianceViewBudgetReservation> ReserveComplianceViewBudgetAsync(string tenantNId, string actorUserNId, DateTimeOffset windowStartOn, int amount, CancellationToken cancellationToken) =>
        Task.FromResult(new ComplianceViewBudgetReservation(windowStartOn, amount, Math.Max(0, 200 - amount)));
    Task<ComplianceViewBudgetReservation> ReleaseComplianceViewBudgetAsync(string tenantNId, string actorUserNId, DateTimeOffset windowStartOn, int amount, CancellationToken cancellationToken) =>
        Task.FromResult(new ComplianceViewBudgetReservation(windowStartOn, 0, 200));
    Task<CompliancePreparationRecord?> GetCompliancePreparationAsync(string tenantNId, string actorUserNId, string requestNId, CancellationToken cancellationToken) =>
        Task.FromResult<CompliancePreparationRecord?>(null);
    Task<CompliancePreparationRecord> SaveCompliancePreparationAsync(CompliancePreparationRecord preparation, CancellationToken cancellationToken) =>
        Task.FromResult(preparation);
    Task<ComplianceCommandRecord?> GetComplianceCommandAsync(string tenantNId, string actorUserNId, string requestNId, CancellationToken cancellationToken) =>
        Task.FromResult<ComplianceCommandRecord?>(null);
    Task<ComplianceCommandRecord> SaveComplianceCommandAsync(ComplianceCommandRecord command, CancellationToken cancellationToken) =>
        Task.FromResult(command);
    Task<bool> TryClaimComplianceCommandAsync(string tenantNId, string actorUserNId, string requestNId, string requestHash, DateTimeOffset claimedOn, CancellationToken cancellationToken) =>
        Task.FromResult(false);
    Task<RetentionPolicyRecord> GetRetentionPolicyAsync(string tenantNId, CancellationToken cancellationToken);
    Task<RetentionPolicyRecord> UpdateRetentionPolicyAsync(RetentionPolicyRecord policy, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListRetentionTenantsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    Task<RetentionSweepResult> RunRetentionSweepAsync(string tenantNId, string workerNId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(new RetentionSweepResult(0, [], true));
    Task MarkRetentionAttachmentReleasedAsync(string tenantNId, string attachmentNId, CancellationToken cancellationToken) => Task.CompletedTask;
    Task<IReadOnlyList<CollaborationOutboxRecord>> ListPendingOutboxAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CollaborationOutboxRecord>>([]);
    Task<CollaborationOutboxRecord?> TryClaimOutboxAsync(Guid eventId, string leaseNId, DateTimeOffset leaseUntil, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<CollaborationOutboxRecord?>(null);
    Task<bool> MarkOutboxPublishedAsync(Guid eventId, string leaseNId, DateTimeOffset publishedOn, CancellationToken cancellationToken) => Task.FromResult(false);
    Task<bool> MarkOutboxFailedAsync(Guid eventId, string leaseNId, int retryCount, string lastError, bool deadLetter, DateTimeOffset observedOn, CancellationToken cancellationToken) => Task.FromResult(false);
    Task<EventInboxClaimResult> TryClaimEventInboxAsync(Guid eventId, string tenantNId, string eventType, DateTimeOffset receivedOn, CancellationToken cancellationToken) =>
        Task.FromResult(new EventInboxClaimResult(EventInboxClaimStatus.Busy, null));
    Task<bool> MarkEventInboxProcessedAsync(Guid eventId, string leaseNId, DateTimeOffset processedOn, CancellationToken cancellationToken) => Task.FromResult(false);
    Task<bool> MarkEventInboxFailedAsync(Guid eventId, string failure, string leaseNId, DateTimeOffset failedOn, CancellationToken cancellationToken) => Task.FromResult(false);
    Task<CollaborationOutboxHealth?> GetOutboxHealthAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<CollaborationOutboxHealth?>(null);

    private async Task<ConversationMemberRecord> HideMemberAndReadAsync(string tenantNId, string conversationNId, string userNId, long throughSequence, long? expectedOptimisticVersion, Guid? expectedConcurrencyVersion, CancellationToken cancellationToken)
    {
        await HideMemberAsync(tenantNId, conversationNId, userNId, throughSequence, cancellationToken);
        return await GetMemberAsync(tenantNId, conversationNId, userNId, cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_CONVERSATION_MEMBER_NOT_FOUND", "会话成员不存在。");
    }

    private async Task<ConversationMemberRecord> RestoreMemberAndReadAsync(string tenantNId, string conversationNId, string userNId, long? expectedOptimisticVersion, Guid? expectedConcurrencyVersion, CancellationToken cancellationToken)
    {
        await RestoreMemberAsync(tenantNId, conversationNId, userNId, cancellationToken);
        return await GetMemberAsync(tenantNId, conversationNId, userNId, cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_CONVERSATION_MEMBER_NOT_FOUND", "会话成员不存在。");
    }

    private async Task<MessageRecord> RetractMessageAndReadAsync(string tenantNId, string conversationNId, string messageNId, string userNId, string reason, long? expectedOptimisticVersion, Guid? expectedConcurrencyVersion, CancellationToken cancellationToken)
    {
        await RetractMessageAsync(tenantNId, conversationNId, messageNId, userNId, reason, cancellationToken);
        return await GetMessageAsync(tenantNId, conversationNId, messageNId, cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_MESSAGE_NOT_FOUND", "消息不存在。");
    }
}

public interface ICollaborationFilePort
{
    Task<AttachmentUploadResult> CreateUploadAsync(string tenantNId, string userNId, AttachmentRecord attachment, CancellationToken cancellationToken);
    Task<AttachmentUploadResult> GetUploadAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken);
    Task<AttachmentUploadResult> SetContentHashAsync(string tenantNId, string userNId, string sessionNId, string sha256, CancellationToken cancellationToken);
    Task<AttachmentUploadResult> ResumeProofAsync(string tenantNId, string userNId, string sessionNId, int writerEpoch, string proof, CancellationToken cancellationToken);
    Task<AttachmentUploadResult> TakeoverAsync(string tenantNId, string userNId, string sessionNId, int expectedWriterEpoch, string? idempotencyKey, string? proof, CancellationToken cancellationToken);
    Task<AttachmentUploadResult> PauseAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken);
    Task<AttachmentUploadResult> ResumeAsync(string tenantNId, string userNId, string sessionNId, int writerEpoch, string proof, CancellationToken cancellationToken);
    Task<AttachmentUploadResult> CancelAsync(string tenantNId, string userNId, string sessionNId, string? reason, CancellationToken cancellationToken);
    Task<AttachmentUploadResult> AppendAsync(string tenantNId, string userNId, string transportId, long expectedOffset, int writerEpoch, Stream content, string? resumeTicket, CancellationToken cancellationToken);
    Task<AttachmentUploadResult> CompleteUploadAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken);
    Task<string?> AddReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken, string? purpose = null);
    Task<string?> BindReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, string conversationNId, string messageNId, string attachmentNId, string uploaderUserNId, string purpose, string requestNId, CancellationToken cancellationToken) =>
        AddReferenceAsync(tenantNId, userNId, fileNId, referenceNId, cancellationToken, purpose);
    Task ReleaseReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, long expectedVersion, string requestNId, CancellationToken cancellationToken) =>
        ReleaseExportReferenceAsync(tenantNId, userNId, fileNId, referenceNId, cancellationToken);
    Task<ExportArtifactResult> CreateExportArtifactAsync(string tenantNId, string userNId, string exportNId, string fileName, string contentType, Stream content, CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前文件适配器未提供合规导出写入能力。");
    Task<string?> AddExportReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken) =>
        AddReferenceAsync(tenantNId, userNId, fileNId, referenceNId, cancellationToken);
    Task<string?> BindExportReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, string conversationNId, string exportNId, CancellationToken cancellationToken) =>
        AddExportReferenceAsync(tenantNId, userNId, fileNId, referenceNId, cancellationToken);
    Task ReleaseExportReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken) => Task.CompletedTask;
    Task AddLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, CancellationToken cancellationToken) => Task.CompletedTask;
    Task AddLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, string requestNId, string scopeChecksum, long caseRevision, CancellationToken cancellationToken) =>
        AddLegalHoldReferenceAsync(tenantNId, userNId, fileNId, holdCaseNId, cancellationToken);
    Task ReleaseLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, CancellationToken cancellationToken) => Task.CompletedTask;
    Task ReleaseLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, string requestNId, string scopeChecksum, long caseRevision, CancellationToken cancellationToken) =>
        ReleaseLegalHoldReferenceAsync(tenantNId, userNId, fileNId, holdCaseNId, cancellationToken);
    Task<CollaborationFileState?> GetAsync(string tenantNId, string userNId, string fileNId, CancellationToken cancellationToken);
    Task<CollaborationFileState?> GetForReconciliationAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) =>
        Task.FromException<CollaborationFileState?>(new NotSupportedException("当前文件适配器未提供无用户上下文的权威回读能力。"));
    Task<Stream> OpenContentAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken);
}

public sealed record AttachmentUploadResult(
    string? UploadSessionNId,
    string? TransportId,
    DateTimeOffset ExpiresOn,
    string? FileName = null,
    string? ContentType = null,
    long? Length = null,
    long? Offset = null,
    int? WriterEpoch = null,
    string? Status = null,
    string? ResumeTicket = null,
    string? FileNId = null,
    string? Sha256 = null);

public sealed record CollaborationFileState(
    string FileNId,
    string FileName,
    string ContentType,
    long Length,
    string ScanStatus,
    bool Restricted,
    string DeletionStatus,
    DateTimeOffset? ObservedOn = null);


public sealed record CollaborationFileContent(Stream Content, string ContentType, string FileName);

public interface ICollaborationAuditPort
{
    Task WriteAsync(string tenantNId, TrustedCollaborationCall? serviceCall, string actorUserNId, string action, string objectType, string objectNId, object payload, CancellationToken cancellationToken);
}

public interface ICollaborationPresence
{
    PresenceDto SetPresence(string tenantNId, string userNId, string state, string? connectionNId = null);
    PresenceDto GetPresence(string tenantNId, string userNId);
    void RemovePresence(string tenantNId, string userNId, string connectionNId);
}

public interface IComplianceStepUpVerifier
{
    Task EnsureValidAsync(string proof, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId, string scopeChecksum, string requestHash, CancellationToken cancellationToken);
}

public interface IStepUpBindingIssuer
{
    string Issue(
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        string action,
        string requestNId,
        string scopeChecksum,
        string requestHash,
        DateTimeOffset issuedOn,
        TimeSpan lifetime);
}
