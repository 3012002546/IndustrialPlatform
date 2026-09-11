using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Collaboration.Infrastructure.Files;
using IndustrialPlatform.EventBus.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Infrastructure_FileStatusConsumerTests
{
    [Fact]
    public async Task Consumer_requeries_authority_and_never_persists_event_version()
    {
        var repository = new FakeRepository
        {
            Attachments = [CreateAttachment(4)],
        };
        var files = new FakeFiles
        {
            State = new CollaborationFileState("FILE-1", "a.txt", "text/plain", 2, "Clean", false, "Active", DateTimeOffset.UtcNow),
        };
        var consumer = new CollaborationFileStatusConsumer(repository, files, NullLogger<CollaborationFileStatusConsumer>.Instance);

        await consumer.HandleAsync(new FileStatusChangedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            TenantNId = "T-1",
            FileNId = "FILE-1",
            ScanStatus = "Malicious",
            DeletionStatus = "Deleted",
            StateVersion = 999,
            ObservedOn = DateTimeOffset.UtcNow.AddDays(-1),
        });

        Assert.Equal(1, files.ReconciliationCalls);
        Assert.Single(repository.UpdatedAttachments);
        Assert.Equal("Clean", repository.UpdatedAttachments[0].FileStateProjection);
        Assert.Null(repository.UpdatedAttachments[0].FileStateVersion);
        Assert.True(repository.Processed);
    }

    [Fact]
    public async Task Consumer_marks_inbox_failed_when_authority_read_fails_so_redelivery_can_reclaim()
    {
        var repository = new FakeRepository { Attachments = [] };
        var files = new FakeFiles { Failure = new InvalidOperationException("SystemData unavailable") };
        var consumer = new CollaborationFileStatusConsumer(repository, files, NullLogger<CollaborationFileStatusConsumer>.Instance);
        var eventId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.HandleAsync(new FileStatusChangedIntegrationEvent
        {
            EventId = eventId,
            TenantNId = "T-1",
            FileNId = "FILE-1",
            ScanStatus = "Clean",
            ObservedOn = DateTimeOffset.UtcNow,
        }));

        Assert.Equal(eventId, repository.FailedEventId);
        Assert.Contains("unavailable", repository.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.False(repository.Processed);
    }

    [Fact]
    public async Task Consumer_does_not_ack_a_redelivery_while_another_worker_lease_is_active()
    {
        var repository = new FakeRepository
        {
            Claim = new EventInboxClaimResult(EventInboxClaimStatus.Busy, null),
        };
        var files = new FakeFiles
        {
            State = new CollaborationFileState("FILE-1", "a.txt", "text/plain", 2, "Clean", false, "Active", DateTimeOffset.UtcNow),
        };
        var consumer = new CollaborationFileStatusConsumer(repository, files, NullLogger<CollaborationFileStatusConsumer>.Instance);

        var exception = await Assert.ThrowsAsync<EventInboxBusyException>(() => consumer.HandleAsync(new FileStatusChangedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            TenantNId = "T-1",
            FileNId = "FILE-1",
            ScanStatus = "Clean",
            ObservedOn = DateTimeOffset.UtcNow,
        }));

        Assert.Equal("COLLAB_EVENT_INBOX_BUSY", exception.Code);
        Assert.IsType<EventInboxBusyException>(exception);
        Assert.Equal(0, files.ReconciliationCalls);
        Assert.False(repository.Processed);
    }

    private static AttachmentRecord CreateAttachment(int version) => new(
        "T-1", "C-1", "A-1", "U-1", "FILE-1", "a.txt", "text/plain", 2,
        "attachment", "Pending", version, null, "Pending", "Active", null,
        "REQ-1", "HASH-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeFiles : ICollaborationFilePort
    {
        public CollaborationFileState? State { get; init; }
        public Exception? Failure { get; init; }
        public int ReconciliationCalls { get; private set; }
        private static Task<T> Unsupported<T>() => Task.FromException<T>(new NotSupportedException());

        public Task<CollaborationFileState?> GetForReconciliationAsync(string tenantNId, string fileNId, CancellationToken cancellationToken)
        {
            ReconciliationCalls++;
            return Failure is null ? Task.FromResult(State) : Task.FromException<CollaborationFileState?>(Failure);
        }

        public Task<AttachmentUploadResult> CreateUploadAsync(string tenantNId, string userNId, AttachmentRecord attachment, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> GetUploadAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> SetContentHashAsync(string tenantNId, string userNId, string sessionNId, string sha256, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> ResumeProofAsync(string tenantNId, string userNId, string sessionNId, int writerEpoch, string proof, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> TakeoverAsync(string tenantNId, string userNId, string sessionNId, int expectedWriterEpoch, string? idempotencyKey, string? proof, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> PauseAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> ResumeAsync(string tenantNId, string userNId, string sessionNId, int writerEpoch, string proof, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> CancelAsync(string tenantNId, string userNId, string sessionNId, string? reason, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> AppendAsync(string tenantNId, string userNId, string transportId, long expectedOffset, int writerEpoch, Stream content, string? resumeTicket, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> CompleteUploadAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<string?> AddReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken, string? purpose = null) => Task.FromResult<string?>(null);
        public Task<CollaborationFileState?> GetAsync(string tenantNId, string userNId, string fileNId, CancellationToken cancellationToken) => Task.FromResult(State);
        public Task<Stream> OpenContentAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken) => Task.FromResult<Stream>(new MemoryStream());
    }

    private sealed class FakeRepository : ICollaborationRepository
    {
        public EventInboxClaimResult Claim { get; init; } = new(EventInboxClaimStatus.Claimed, "LEASE-1");
        public IReadOnlyList<AttachmentRecord> Attachments { get; init; } = [];
        public List<AttachmentRecord> UpdatedAttachments { get; } = [];
        public bool Processed { get; private set; }
        public Guid FailedEventId { get; private set; }
        public string Failure { get; private set; } = string.Empty;
        private static Task<T> Unsupported<T>() => Task.FromException<T>(new NotSupportedException());
        private static Task Unsupported() => Task.FromException(new NotSupportedException());

        public Task<EventInboxClaimResult> TryClaimEventInboxAsync(Guid eventId, string tenantNId, string eventType, DateTimeOffset receivedOn, CancellationToken cancellationToken) => Task.FromResult(Claim);
        public Task<bool> MarkEventInboxProcessedAsync(Guid eventId, string leaseNId, DateTimeOffset processedOn, CancellationToken cancellationToken) { Processed = true; return Task.FromResult(true); }
        public Task<bool> MarkEventInboxFailedAsync(Guid eventId, string failure, string leaseNId, DateTimeOffset failedOn, CancellationToken cancellationToken) { FailedEventId = eventId; Failure = failure; return Task.FromResult(true); }
        public Task<IReadOnlyList<AttachmentRecord>> ListAttachmentsByFileAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) => Task.FromResult(Attachments);
        public Task<AttachmentRecord> UpdateAttachmentAsync(AttachmentRecord attachment, CancellationToken cancellationToken) { UpdatedAttachments.Add(attachment); return Task.FromResult(attachment); }

        public Task<ConversationRecord?> GetConversationAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken) => Unsupported<ConversationRecord?>();
        public Task<ConversationRecord?> FindConversationByPairAsync(string tenantNId, string lowUserNId, string highUserNId, CancellationToken cancellationToken) => Unsupported<ConversationRecord?>();
        public Task<IReadOnlyList<ConversationRecord>> ListConversationsAsync(string tenantNId, string userNId, int page, int pageSize, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ConversationRecord>>();
        public Task<ConversationRecord> CreateConversationAsync(ConversationRecord conversation, ConversationMemberRecord currentMember, ConversationMemberRecord peerMember, CancellationToken cancellationToken) => Unsupported<ConversationRecord>();
        public Task<ConversationMemberRecord?> GetMemberAsync(string tenantNId, string conversationNId, string userNId, CancellationToken cancellationToken) => Unsupported<ConversationMemberRecord?>();
        public Task<IReadOnlyList<ConversationMemberRecord>> GetMembersAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ConversationMemberRecord>>();
        public Task<MessageRecord?> FindMessageByClientAsync(string tenantNId, string senderUserNId, string clientMessageNId, CancellationToken cancellationToken) => Unsupported<MessageRecord?>();
        public Task<MessageRecord?> GetMessageAsync(string tenantNId, string conversationNId, string messageNId, CancellationToken cancellationToken) => Unsupported<MessageRecord?>();
        public Task<MessageRecord?> GetMessageByIdAsync(string tenantNId, string messageNId, CancellationToken cancellationToken) => Unsupported<MessageRecord?>();
        public Task<MessageRecord?> GetMessageByAttachmentAsync(string tenantNId, string attachmentNId, CancellationToken cancellationToken) => Unsupported<MessageRecord?>();
        public Task<MessageRecord> AppendMessageAsync(ConversationRecord conversation, MessageRecord message, CancellationToken cancellationToken) => Unsupported<MessageRecord>();
        public Task<IReadOnlyList<MessageRecord>> GetMessagesAsync(string tenantNId, string conversationNId, string mode, long? sequence, int pageSize, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MessageRecord>>();
        public Task UpdateReadCursorAsync(string tenantNId, string conversationNId, string userNId, long sequence, CancellationToken cancellationToken) => Unsupported();
        public Task HideMemberAsync(string tenantNId, string conversationNId, string userNId, long throughSequence, CancellationToken cancellationToken) => Unsupported();
        public Task RestoreMemberAsync(string tenantNId, string conversationNId, string userNId, CancellationToken cancellationToken) => Unsupported();
        public Task RetractMessageAsync(string tenantNId, string conversationNId, string messageNId, string userNId, string reason, CancellationToken cancellationToken) => Unsupported();
        public Task<AttachmentRecord?> GetAttachmentAsync(string tenantNId, string conversationNId, string attachmentNId, CancellationToken cancellationToken) => Unsupported<AttachmentRecord?>();
        public Task<AttachmentRecord?> GetAttachmentByIdAsync(string tenantNId, string attachmentNId, CancellationToken cancellationToken) => Unsupported<AttachmentRecord?>();
        public Task<AttachmentRecord> CreateAttachmentAsync(AttachmentRecord attachment, CancellationToken cancellationToken) => Unsupported<AttachmentRecord>();
        public Task<IReadOnlyList<MessageRecord>> SearchComplianceMessagesAsync(string tenantNId, ComplianceScopeDto scope, string? keyword, int page, int pageSize, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<MessageRecord>>();
        public Task<ComplianceDispositionRecord> CreateDispositionAsync(ComplianceDispositionRecord disposition, CancellationToken cancellationToken) => Unsupported<ComplianceDispositionRecord>();
        public Task<IReadOnlyList<ComplianceDispositionRecord>> ListDispositionsAsync(string tenantNId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ComplianceDispositionRecord>>();
        public Task<LegalHoldRecord> CreateLegalHoldAsync(LegalHoldRecord hold, CancellationToken cancellationToken) => Unsupported<LegalHoldRecord>();
        public Task<IReadOnlyList<LegalHoldRecord>> ListLegalHoldsAsync(string tenantNId, string? status, int page, int pageSize, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<LegalHoldRecord>>();
        public Task<LegalHoldRecord?> GetLegalHoldAsync(string tenantNId, string holdCaseNId, CancellationToken cancellationToken) => Unsupported<LegalHoldRecord?>();
        public Task<LegalHoldRecord> UpdateLegalHoldAsync(LegalHoldRecord hold, CancellationToken cancellationToken) => Unsupported<LegalHoldRecord>();
        public Task<ComplianceExportRecord> CreateExportAsync(ComplianceExportRecord exportRecord, CancellationToken cancellationToken) => Unsupported<ComplianceExportRecord>();
        public Task<IReadOnlyList<ComplianceExportRecord>> ListExportsAsync(string tenantNId, string? status, int page, int pageSize, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ComplianceExportRecord>>();
        public Task<ComplianceExportRecord?> GetExportAsync(string tenantNId, string exportNId, CancellationToken cancellationToken) => Unsupported<ComplianceExportRecord?>();
        public Task<ComplianceExportRecord> UpdateExportAsync(ComplianceExportRecord exportRecord, CancellationToken cancellationToken) => Unsupported<ComplianceExportRecord>();
        public Task<RetentionPolicyRecord> GetRetentionPolicyAsync(string tenantNId, CancellationToken cancellationToken) => Unsupported<RetentionPolicyRecord>();
        public Task<RetentionPolicyRecord> UpdateRetentionPolicyAsync(RetentionPolicyRecord policy, CancellationToken cancellationToken) => Unsupported<RetentionPolicyRecord>();
    }
}
