using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Infrastructure.Database;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.Infrastructure.Persistence;

/// <summary>
/// SqlSugar persistence for the collaboration bounded context. Conversation creation and
/// message append are serialized per tenant/pair in-process and protected by database
/// unique constraints across instances.
/// </summary>
public sealed class SqlCollaborationRepository : ICollaborationRepository
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);
    private readonly SqlSugarDbContext _dbContext;

    public SqlCollaborationRepository(SqlSugarDbContext dbContext) => _dbContext = dbContext;

    public async Task<ConversationRecord?> GetConversationAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<ConversationTable>()
            .Where(item => item.TenantNId == tenantNId && item.NId == conversationNId && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<ConversationRecord?> FindConversationByPairAsync(string tenantNId, string lowUserNId, string highUserNId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<ConversationTable>()
            .Where(item => item.TenantNId == tenantNId
                && item.ParticipantLowUserNId == lowUserNId
                && item.ParticipantHighUserNId == highUserNId
                && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<ConversationRecord>> ListConversationsAsync(string tenantNId, string userNId, int page, int pageSize, CancellationToken cancellationToken)
        => await ListConversationsAsync(tenantNId, userNId, page, pageSize, "Visible", false, cancellationToken);

    public async Task<IReadOnlyList<ConversationRecord>> ListConversationsAsync(string tenantNId, string userNId, int page, int pageSize, string visibility, bool unreadOnly, CancellationToken cancellationToken)
    {
        var memberQuery = _dbContext.SqlSugar.Queryable<ConversationMemberTable>()
            .Where(item => item.TenantNId == tenantNId && item.UserNId == userNId && !item.IsDeleted);
        memberQuery = visibility == "Hidden"
            ? memberQuery.Where(item => item.VisibilityState == "Hidden")
            : memberQuery.Where(item => item.VisibilityState == "Visible");
        if (unreadOnly)
            memberQuery = memberQuery.Where(item => item.UnreadCount > 0);
        var conversationIds = await memberQuery.Select(item => item.ConversationNId).ToListAsync(cancellationToken);
        if (conversationIds.Count == 0)
            return [];
        var rows = await _dbContext.SqlSugar.Queryable<ConversationTable>()
            .Where(item => item.TenantNId == tenantNId && conversationIds.Contains(item.NId) && !item.IsDeleted)
            .OrderBy(item => item.LastMessageOn, SqlSugar.OrderByType.Desc)
            .Skip(Math.Max(page - 1, 0) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<IReadOnlyDictionary<string, MessageRecord>> ListLatestVisibleMessagesForUserAsync(string tenantNId, IReadOnlyCollection<string> conversationNIds, string userNId, CancellationToken cancellationToken)
    {
        if (conversationNIds.Count == 0)
            return new Dictionary<string, MessageRecord>(StringComparer.Ordinal);
        var rows = await _dbContext.SqlSugar.Queryable<MessageTable>()
            .Where(item => item.TenantNId == tenantNId && conversationNIds.Contains(item.ConversationNId) && !item.IsDeleted)
            .Where(item => !SqlFunc.Subqueryable<MessagePersonalVisibilityTable>()
                .Where(hidden => hidden.TenantNId == tenantNId
                    && hidden.UserNId == userNId
                    && hidden.ConversationNId == item.ConversationNId
                    && hidden.MessageNId == item.MessageNId)
                .Any())
            .OrderBy(item => item.Sequence, SqlSugar.OrderByType.Desc)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord)
            .GroupBy(item => item.ConversationNId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    public async Task<ConversationRecord> CreateConversationAsync(ConversationRecord conversation, ConversationMemberRecord currentMember, ConversationMemberRecord peerMember, CancellationToken cancellationToken)
    {
        var key = $"{conversation.TenantNId}:{conversation.ParticipantLowUserNId}:{conversation.ParticipantHighUserNId}";
        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindConversationByPairAsync(conversation.TenantNId, conversation.ParticipantLowUserNId, conversation.ParticipantHighUserNId, cancellationToken);
            if (existing is not null)
                return existing;
            var sugar = _dbContext.SqlSugar;
            sugar.Ado.BeginTran();
            try
            {
                await sugar.Insertable(ToRow(conversation)).ExecuteCommandAsync(cancellationToken);
                await sugar.Insertable(ToRow(currentMember)).ExecuteCommandAsync(cancellationToken);
                await sugar.Insertable(ToRow(peerMember)).ExecuteCommandAsync(cancellationToken);
                sugar.Ado.CommitTran();
                return conversation;
            }
            catch
            {
                sugar.Ado.RollbackTran();
                var raced = await FindConversationByPairAsync(conversation.TenantNId, conversation.ParticipantLowUserNId, conversation.ParticipantHighUserNId, cancellationToken);
                if (raced is not null)
                    return raced;
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ConversationMemberRecord?> GetMemberAsync(string tenantNId, string conversationNId, string userNId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<ConversationMemberTable>()
            .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && item.UserNId == userNId && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<ConversationMemberRecord>> GetMembersAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<ConversationMemberTable>()
            .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && !item.IsDeleted)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<IReadOnlyList<ConversationMemberRecord>> ListMembersForConversationsAsync(string tenantNId, IReadOnlyCollection<string> conversationNIds, CancellationToken cancellationToken)
    {
        if (conversationNIds.Count == 0)
            return [];
        var rows = await _dbContext.SqlSugar.Queryable<ConversationMemberTable>()
            .Where(item => item.TenantNId == tenantNId && conversationNIds.Contains(item.ConversationNId) && !item.IsDeleted)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<MessageRecord?> FindMessageByClientAsync(string tenantNId, string senderUserNId, string clientMessageNId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<MessageTable>()
            .Where(item => item.TenantNId == tenantNId && item.SenderUserNId == senderUserNId && item.ClientMessageNId == clientMessageNId && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<MessageRecord?> GetMessageAsync(string tenantNId, string conversationNId, string messageNId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<MessageTable>()
            .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && item.MessageNId == messageNId && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<MessageRecord?> GetMessageByIdAsync(string tenantNId, string messageNId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<MessageTable>()
            .Where(item => item.TenantNId == tenantNId && item.MessageNId == messageNId && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<MessageRecord?> GetMessageByAttachmentAsync(string tenantNId, string attachmentNId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<MessageTable>()
            .Where(item => item.TenantNId == tenantNId && item.AttachmentNId == attachmentNId && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public Task<MessageRecord> AppendMessageAsync(ConversationRecord conversation, MessageRecord message, CancellationToken cancellationToken) =>
        AppendMessageCoreAsync(conversation, message, null, cancellationToken);

    public Task<MessageRecord> AppendMessageAndBindAttachmentAsync(ConversationRecord conversation, MessageRecord message, string attachmentNId, CancellationToken cancellationToken) =>
        AppendMessageCoreAsync(conversation, message, attachmentNId, cancellationToken);

    private async Task<MessageRecord> AppendMessageCoreAsync(ConversationRecord conversation, MessageRecord message, string? attachmentNId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var sugar = _dbContext.SqlSugar;
            sugar.Ado.BeginTran();
            try
            {
                var current = await GetConversationAsync(conversation.TenantNId, conversation.ConversationNId, cancellationToken)
                    ?? throw new InvalidOperationException("Conversation disappeared while appending a message.");
                var existing = await FindMessageByClientAsync(conversation.TenantNId, message.SenderUserNId, message.ClientMessageNId, cancellationToken);
                if (existing is not null)
                {
                    if (attachmentNId is not null)
                        await BindAttachmentToMessageAsync(sugar, existing.TenantNId, existing.ConversationNId, attachmentNId, existing.MessageNId, existing.AcceptedOn, cancellationToken);
                    sugar.Ado.CommitTran();
                    return existing;
                }

                var nextConcurrencyVersion = Guid.NewGuid();
                var updatedOn = message.AcceptedOn;
                var affected = await sugar.Ado.ExecuteCommandAsync(
                    "UPDATE collaboration_conversation SET last_message_sequence = last_message_sequence + 1, last_message_n_id = @messageNId, last_message_on = @acceptedOn, optimistic_version = optimistic_version + 1, concurrency_version = @concurrencyVersion, last_updated_on = @acceptedOn WHERE tenant_n_id = @tenantNId AND n_id = @conversationNId AND is_deleted = @isDeleted AND last_message_sequence = @expectedSequence AND optimistic_version = @expectedOptimisticVersion",
                    new[]
                    {
                        new SqlSugar.SugarParameter("@messageNId", message.MessageNId),
                        new SqlSugar.SugarParameter("@acceptedOn", updatedOn),
                        new SqlSugar.SugarParameter("@concurrencyVersion", nextConcurrencyVersion),
                        new SqlSugar.SugarParameter("@tenantNId", conversation.TenantNId),
                        new SqlSugar.SugarParameter("@conversationNId", conversation.ConversationNId),
                        new SqlSugar.SugarParameter("@isDeleted", false),
                        new SqlSugar.SugarParameter("@expectedSequence", current.LastMessageSequence),
                        new SqlSugar.SugarParameter("@expectedOptimisticVersion", current.OptimisticVersion),
                    },
                    cancellationToken);
                if (affected != 1)
                {
                    sugar.Ado.RollbackTran();
                    await Task.Delay(TimeSpan.FromMilliseconds(20 * (attempt + 1)), cancellationToken);
                    continue;
                }

                var updatedConversation = await GetConversationAsync(conversation.TenantNId, conversation.ConversationNId, cancellationToken)
                    ?? throw new InvalidOperationException("Conversation disappeared after sequence allocation.");
                var accepted = message with { Sequence = updatedConversation.LastMessageSequence };
                await sugar.Insertable(ToRow(accepted)).ExecuteCommandAsync(cancellationToken);
                if (attachmentNId is not null)
                    await BindAttachmentToMessageAsync(sugar, accepted.TenantNId, accepted.ConversationNId, attachmentNId, accepted.MessageNId, accepted.AcceptedOn, cancellationToken);
                var members = await sugar.Queryable<ConversationMemberTable>()
                    .Where(item => item.TenantNId == accepted.TenantNId && item.ConversationNId == accepted.ConversationNId && !item.IsDeleted)
                    .ToListAsync(cancellationToken);
                foreach (var member in members)
                {
                    if (!string.Equals(member.UserNId, accepted.SenderUserNId, StringComparison.Ordinal))
                        member.UnreadCount++;
                    member.ProjectionVersion++;
                    member.OptimisticVersion = member.ProjectionVersion;
                    member.ConcurrencyVersion = Guid.NewGuid();
                    member.LastUpdatedOn = DateTimeOffset.UtcNow;
                    await sugar.Updateable(member).ExecuteCommandAsync(cancellationToken);
                }
                await sugar.Insertable(new CollaborationOutboxTable
                {
                    EventId = Guid.NewGuid(),
                    TenantNId = accepted.TenantNId,
                    EventType = "collaboration.message.accepted.v1",
                    Payload = JsonSerializer.Serialize(new { accepted.ConversationNId, accepted.MessageNId, accepted.Sequence, accepted.SenderUserNId }),
                    CreatedOn = accepted.AcceptedOn,
                    RetryCount = 0,
                }).ExecuteCommandAsync(cancellationToken);
                sugar.Ado.CommitTran();
                return accepted;
            }
            catch
            {
                sugar.Ado.RollbackTran();
                if (attempt == 4)
                    throw;
                await Task.Delay(TimeSpan.FromMilliseconds(20 * (attempt + 1)), cancellationToken);
            }
        }

        throw new InvalidOperationException("Could not allocate a message sequence after concurrent updates.");
    }

    private static async Task BindAttachmentToMessageAsync(ISqlSugarClient sugar, string tenantNId, string conversationNId, string attachmentNId, string messageNId, DateTimeOffset updatedOn, CancellationToken cancellationToken)
    {
        var attachment = await sugar.Queryable<AttachmentTable>()
            .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && item.AttachmentNId == attachmentNId && !item.IsDeleted)
            .FirstAsync(cancellationToken)
            ?? throw new InvalidOperationException("Attachment disappeared while binding the accepted message.");
        if (attachment.BoundMessageNId is not null && !string.Equals(attachment.BoundMessageNId, messageNId, StringComparison.Ordinal))
            throw new InvalidOperationException("Attachment is already bound to another message.");
        if (attachment.BoundMessageNId is null)
        {
            var affected = await sugar.Ado.ExecuteCommandAsync(
                "UPDATE collaboration_chat_attachment SET bound_message_n_id = @messageNId, last_updated_on = @updatedOn WHERE tenant_n_id = @tenantNId AND conversation_n_id = @conversationNId AND attachment_n_id = @attachmentNId AND is_deleted = @isDeleted AND bound_message_n_id IS NULL",
                new[]
                {
                    new SugarParameter("@messageNId", messageNId),
                    new SugarParameter("@updatedOn", updatedOn),
                    new SugarParameter("@tenantNId", tenantNId),
                    new SugarParameter("@conversationNId", conversationNId),
                    new SugarParameter("@attachmentNId", attachmentNId),
                    new SugarParameter("@isDeleted", false),
                },
                cancellationToken);
            if (affected != 1)
            {
                var current = await sugar.Queryable<AttachmentTable>()
                    .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && item.AttachmentNId == attachmentNId && !item.IsDeleted)
                    .FirstAsync(cancellationToken);
                if (current?.BoundMessageNId is null || !string.Equals(current.BoundMessageNId, messageNId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Attachment is already bound to another message.");
            }
        }
    }

    public async Task<IReadOnlyList<CollaborationOutboxRecord>> ListPendingOutboxAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<CollaborationOutboxTable>()
            .Where(item => item.PublishedOn == null
                && item.DeadLetteredOn == null
                && (item.LeaseUntil == null || item.LeaseUntil <= now))
            .OrderBy(item => item.CreatedOn, SqlSugar.OrderByType.Asc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<CollaborationOutboxRecord?> TryClaimOutboxAsync(
        Guid eventId,
        string leaseNId,
        DateTimeOffset leaseUntil,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var affected = await _dbContext.SqlSugar.Updateable<CollaborationOutboxTable>()
            .SetColumns(item => new CollaborationOutboxTable
            {
                LeaseNId = leaseNId,
                LeaseUntil = leaseUntil,
            })
            .Where(item => item.EventId == eventId
                && item.PublishedOn == null
                && item.DeadLetteredOn == null
                && (item.LeaseUntil == null || item.LeaseUntil <= now))
            .ExecuteCommandAsync(cancellationToken);
        if (affected != 1)
            return null;
        var claimed = await _dbContext.SqlSugar.Queryable<CollaborationOutboxTable>()
            .Where(item => item.EventId == eventId && item.LeaseNId == leaseNId)
            .FirstAsync(cancellationToken);
        return claimed is null ? null : ToRecord(claimed);
    }

    public async Task<bool> MarkOutboxPublishedAsync(
        Guid eventId,
        string leaseNId,
        DateTimeOffset publishedOn,
        CancellationToken cancellationToken)
    {
        var affected = await _dbContext.SqlSugar.Updateable<CollaborationOutboxTable>()
            .SetColumns(item => new CollaborationOutboxTable
            {
                PublishedOn = publishedOn,
                LeaseNId = null,
                LeaseUntil = null,
            })
            .Where(item => item.EventId == eventId && item.LeaseNId == leaseNId && item.PublishedOn == null)
            .ExecuteCommandAsync(cancellationToken);
        return affected == 1;
    }

    public async Task<bool> MarkOutboxFailedAsync(
        Guid eventId,
        string leaseNId,
        int retryCount,
        string lastError,
        bool deadLetter,
        DateTimeOffset observedOn,
        CancellationToken cancellationToken)
    {
        var nextAttemptOn = deadLetter
            ? (DateTimeOffset?)null
            : observedOn.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(8, Math.Max(1, retryCount)))));
        var affected = await _dbContext.SqlSugar.Updateable<CollaborationOutboxTable>()
            .SetColumns(item => new CollaborationOutboxTable
            {
                RetryCount = Math.Max(0, retryCount),
                LastError = lastError.Length > 2000 ? lastError.Substring(0, 2000) : lastError,
                DeadLetteredOn = deadLetter ? observedOn : null,
                LeaseNId = null,
                LeaseUntil = nextAttemptOn,
            })
            .Where(item => item.EventId == eventId && item.LeaseNId == leaseNId && item.PublishedOn == null)
            .ExecuteCommandAsync(cancellationToken);
        return affected == 1;
    }

    public async Task<EventInboxClaimResult> TryClaimEventInboxAsync(Guid eventId, string tenantNId, string eventType, DateTimeOffset receivedOn, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseUntil = now.AddSeconds(30);
        var leaseNId = $"inbox-{Guid.NewGuid():N}";
        try
        {
            await _dbContext.SqlSugar.Insertable(new CollaborationEventInboxTable
            {
                EventId = eventId,
                TenantNId = tenantNId,
                EventType = eventType,
                Status = "Processing",
                ReceivedOn = receivedOn,
                RetryCount = 0,
                LeaseNId = leaseNId,
                LeaseUntil = leaseUntil,
            }).ExecuteCommandAsync(cancellationToken);
            return new EventInboxClaimResult(EventInboxClaimStatus.Claimed, leaseNId);
        }
        catch (Exception exception) when (IsUniqueConstraintViolation(exception))
        {
            var existing = await _dbContext.SqlSugar.Queryable<CollaborationEventInboxTable>()
                .Where(item => item.EventId == eventId)
                .FirstAsync(cancellationToken);
            if (existing is null)
                return new EventInboxClaimResult(EventInboxClaimStatus.Busy, null);
            if (existing.Status == "Processed")
                return new EventInboxClaimResult(EventInboxClaimStatus.AlreadyProcessed, null);
            if (existing.Status == "Processing" && ReadUtcTimestamp(existing.LeaseUntil) is { } existingLeaseUntil && existingLeaseUntil > now)
                return new EventInboxClaimResult(EventInboxClaimStatus.Busy, null);

            var affected = await _dbContext.SqlSugar.Updateable<CollaborationEventInboxTable>()
                .SetColumns(item => new CollaborationEventInboxTable
                {
                    TenantNId = tenantNId,
                    EventType = eventType,
                    Status = "Processing",
                    ReceivedOn = receivedOn,
                    ProcessedOn = null,
                    RetryCount = item.RetryCount + 1,
                    LastError = null,
                    LeaseNId = leaseNId,
                    LeaseUntil = leaseUntil,
                })
                .Where(item => item.EventId == eventId
                    && item.Status != "Processed"
                    && (item.Status == "Failed" || item.LeaseUntil == null || item.LeaseUntil <= now))
                .ExecuteCommandAsync(cancellationToken);
            if (affected == 1)
                return new EventInboxClaimResult(EventInboxClaimStatus.Claimed, leaseNId);

            var current = await _dbContext.SqlSugar.Queryable<CollaborationEventInboxTable>()
                .Where(item => item.EventId == eventId)
                .FirstAsync(cancellationToken);
            return current?.Status == "Processed"
                ? new EventInboxClaimResult(EventInboxClaimStatus.AlreadyProcessed, null)
                : new EventInboxClaimResult(EventInboxClaimStatus.Busy, null);
        }
    }

    public async Task<bool> MarkEventInboxProcessedAsync(Guid eventId, string leaseNId, DateTimeOffset processedOn, CancellationToken cancellationToken)
        => await _dbContext.SqlSugar.Updateable<CollaborationEventInboxTable>()
            .SetColumns(item => new CollaborationEventInboxTable { Status = "Processed", ProcessedOn = processedOn, LeaseNId = null, LeaseUntil = null, LastError = null })
            .Where(item => item.EventId == eventId && item.Status == "Processing" && item.LeaseNId == leaseNId)
            .ExecuteCommandAsync(cancellationToken) == 1;

    public async Task<bool> MarkEventInboxFailedAsync(Guid eventId, string failure, string leaseNId, DateTimeOffset failedOn, CancellationToken cancellationToken)
        => await _dbContext.SqlSugar.Updateable<CollaborationEventInboxTable>()
            .SetColumns(item => new CollaborationEventInboxTable
            {
                Status = "Failed",
                ProcessedOn = null,
                RetryCount = item.RetryCount + 1,
                LastError = failure.Length > 2000 ? failure.Substring(0, 2000) : failure,
                LeaseNId = null,
                LeaseUntil = null,
            })
            .Where(item => item.EventId == eventId && item.Status == "Processing" && item.LeaseNId == leaseNId)
            .ExecuteCommandAsync(cancellationToken) == 1;

    public async Task<CollaborationOutboxHealth?> GetOutboxHealthAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await _dbContext.SqlSugar.Queryable<CollaborationOutboxTable>()
            .Where(item => item.PublishedOn == null && item.DeadLetteredOn == null && (item.LeaseUntil == null || item.LeaseUntil <= now))
            .CountAsync(cancellationToken);
        var deadLetter = await _dbContext.SqlSugar.Queryable<CollaborationOutboxTable>()
            .Where(item => item.DeadLetteredOn != null)
            .CountAsync(cancellationToken);
        return new CollaborationOutboxHealth(pending, deadLetter);
    }

    public async Task<IReadOnlyList<MessageRecord>> GetMessagesAsync(string tenantNId, string conversationNId, string mode, long? sequence, int pageSize, CancellationToken cancellationToken)
        => await GetMessagesAsync(tenantNId, conversationNId, mode, sequence, null, null, pageSize, 1, cancellationToken);

    public async Task<IReadOnlyList<MessageRecord>> GetMessagesAsync(string tenantNId, string conversationNId, string mode, long? sequence, long? fromSequence, long? toSequence, int pageSize, int historyPage, CancellationToken cancellationToken)
        => await GetMessagesAsync(tenantNId, conversationNId, mode, sequence, fromSequence, toSequence, pageSize, historyPage, 0, cancellationToken);

    public async Task<IReadOnlyList<MessageRecord>> GetMessagesAsync(string tenantNId, string conversationNId, string mode, long? sequence, long? fromSequence, long? toSequence, int pageSize, int historyPage, long retentionFloorSequence, CancellationToken cancellationToken)
        => await GetMessagesCoreAsync(tenantNId, conversationNId, null, mode, sequence, fromSequence, toSequence, pageSize, historyPage, retentionFloorSequence, cancellationToken);

    public async Task<IReadOnlyList<MessageRecord>> GetMessagesForUserAsync(string tenantNId, string conversationNId, string userNId, string mode, long? sequence, int pageSize, CancellationToken cancellationToken)
        => await GetMessagesCoreAsync(tenantNId, conversationNId, userNId, mode, sequence, null, null, pageSize, 1, 0, cancellationToken);

    public async Task<IReadOnlyList<MessageRecord>> GetMessagesForUserAsync(string tenantNId, string conversationNId, string userNId, string mode, long? sequence, long? fromSequence, long? toSequence, int pageSize, int historyPage, long retentionFloorSequence, CancellationToken cancellationToken)
        => await GetMessagesCoreAsync(tenantNId, conversationNId, userNId, mode, sequence, fromSequence, toSequence, pageSize, historyPage, retentionFloorSequence, cancellationToken);

    private async Task<IReadOnlyList<MessageRecord>> GetMessagesCoreAsync(string tenantNId, string conversationNId, string? userNId, string mode, long? sequence, long? fromSequence, long? toSequence, int pageSize, int historyPage, long retentionFloorSequence, CancellationToken cancellationToken)
    {
        var query = _dbContext.SqlSugar.Queryable<MessageTable>()
            .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && !item.IsDeleted);
        if (!string.IsNullOrWhiteSpace(userNId))
            query = query.Where(item => !SqlFunc.Subqueryable<MessagePersonalVisibilityTable>()
                .Where(hidden => hidden.TenantNId == tenantNId
                    && hidden.UserNId == userNId
                    && hidden.ConversationNId == conversationNId
                    && hidden.MessageNId == item.MessageNId)
                .Any());
        if (retentionFloorSequence > 0)
            query = query.Where(item => item.Sequence > retentionFloorSequence);
        List<MessageTable> rows;
        if (mode == "after")
        {
            rows = await query.Where(item => item.Sequence > (sequence ?? 0))
                .OrderBy(item => item.Sequence, SqlSugar.OrderByType.Asc)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }
        else if (mode == "window")
        {
            var from = fromSequence ?? 0;
            var to = toSequence ?? 0;
            rows = await query.Where(item => item.Sequence >= from && item.Sequence <= to)
                .OrderBy(item => item.Sequence, SqlSugar.OrderByType.Asc)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        }
        else
        {
            rows = await query.OrderBy(item => item.Sequence, SqlSugar.OrderByType.Desc)
                .Skip(Math.Max(historyPage - 1, 0) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            rows.Reverse();
        }
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<bool> IsMessageHiddenForUserAsync(string tenantNId, string conversationNId, string messageNId, string userNId, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Queryable<MessagePersonalVisibilityTable>()
            .AnyAsync(item => item.TenantNId == tenantNId
                && item.UserNId == userNId
                && item.ConversationNId == conversationNId
                && item.MessageNId == messageNId, cancellationToken);

    public async Task<bool> HideMessageForUserAsync(string tenantNId, string conversationNId, string messageNId, string userNId, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var affected = await sugar.Ado.ExecuteCommandAsync(
                """
                INSERT INTO collaboration_message_personal_visibility (tenant_n_id, user_n_id, conversation_n_id, message_n_id, hidden_on)
                VALUES (@TenantNId, @UserNId, @ConversationNId, @MessageNId, @HiddenOn)
                ON CONFLICT (tenant_n_id, user_n_id, message_n_id) DO NOTHING
                """,
                new SqlSugar.SugarParameter("@TenantNId", tenantNId),
                new SqlSugar.SugarParameter("@UserNId", userNId),
                new SqlSugar.SugarParameter("@ConversationNId", conversationNId),
                new SqlSugar.SugarParameter("@MessageNId", messageNId),
                new SqlSugar.SugarParameter("@HiddenOn", now));
            if (affected == 1)
                await InsertOutboxAsync(sugar, tenantNId, "collaboration.message.personal-hidden.v1", new
                {
                    ConversationNId = conversationNId,
                    MessageNId = messageNId,
                    UserNId = userNId,
                }, now, cancellationToken);
            sugar.Ado.CommitTran();
            return affected == 1;
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task UpdateReadCursorAsync(string tenantNId, string conversationNId, string userNId, long sequence, CancellationToken cancellationToken)
    {
        var row = await RequireMemberRowAsync(tenantNId, conversationNId, userNId, cancellationToken);
        if (sequence <= row.LastReadSequence)
            return;
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            var now = DateTimeOffset.UtcNow;
            row.LastReadSequence = sequence;
            row.UnreadCount = 0;
            row.LastReadOn = now;
            row.ProjectionVersion++;
            row.OptimisticVersion = row.ProjectionVersion;
            row.ConcurrencyVersion = Guid.NewGuid();
            row.LastUpdatedOn = now;
            await sugar.Updateable(row).ExecuteCommandAsync(cancellationToken);
            await InsertOutboxAsync(sugar, tenantNId, "collaboration.read-cursor.advanced.v1", new
            {
                ConversationNId = conversationNId,
                UserNId = userNId,
                Sequence = sequence,
                ProjectionVersion = row.ProjectionVersion,
            }, now, cancellationToken);
            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task HideMemberAsync(string tenantNId, string conversationNId, string userNId, long throughSequence, CancellationToken cancellationToken)
    {
        _ = await HideMemberAsync(tenantNId, conversationNId, userNId, throughSequence, null, null, cancellationToken);
    }

    public async Task<ConversationMemberRecord> HideMemberAsync(string tenantNId, string conversationNId, string userNId, long throughSequence, long? expectedOptimisticVersion, Guid? expectedConcurrencyVersion, CancellationToken cancellationToken)
    {
        var gate = Locks.GetOrAdd($"{tenantNId}:{conversationNId}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var row = await RequireMemberRowAsync(tenantNId, conversationNId, userNId, cancellationToken);
            EnsureMemberVersion(row, expectedOptimisticVersion, expectedConcurrencyVersion);
            if (row.VisibilityState == "Hidden")
                return ToRecord(row);
            row.VisibilityState = "Hidden";
            row.HiddenOn = DateTimeOffset.UtcNow;
            row.HiddenThroughSequence = Math.Max(row.HiddenThroughSequence, throughSequence);
            row.UnreadCount = 0;
            row.LastUpdatedOn = DateTimeOffset.UtcNow;
            row.ProjectionVersion++;
            row.OptimisticVersion = row.ProjectionVersion;
            row.ConcurrencyVersion = Guid.NewGuid();
            await _dbContext.SqlSugar.Updateable(row).ExecuteCommandAsync(cancellationToken);
            return ToRecord(row);
        }
        finally { gate.Release(); }
    }

    public async Task RestoreMemberAsync(string tenantNId, string conversationNId, string userNId, CancellationToken cancellationToken)
    {
        _ = await RestoreMemberAsync(tenantNId, conversationNId, userNId, null, null, cancellationToken);
    }

    public async Task<ConversationMemberRecord> RestoreMemberAsync(string tenantNId, string conversationNId, string userNId, long? expectedOptimisticVersion, Guid? expectedConcurrencyVersion, CancellationToken cancellationToken)
    {
        var gate = Locks.GetOrAdd($"{tenantNId}:{conversationNId}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var row = await RequireMemberRowAsync(tenantNId, conversationNId, userNId, cancellationToken);
            EnsureMemberVersion(row, expectedOptimisticVersion, expectedConcurrencyVersion);
            if (row.VisibilityState == "Visible")
                return ToRecord(row);
            row.VisibilityState = "Visible";
            row.HiddenOn = null;
            row.LastUpdatedOn = DateTimeOffset.UtcNow;
            row.ProjectionVersion++;
            row.OptimisticVersion = row.ProjectionVersion;
            row.ConcurrencyVersion = Guid.NewGuid();
            await _dbContext.SqlSugar.Updateable(row).ExecuteCommandAsync(cancellationToken);
            return ToRecord(row);
        }
        finally { gate.Release(); }
    }

    public async Task RetractMessageAsync(string tenantNId, string conversationNId, string messageNId, string userNId, string reason, CancellationToken cancellationToken)
    {
        _ = await RetractMessageAsync(tenantNId, conversationNId, messageNId, userNId, reason, null, null, cancellationToken);
    }

    public async Task<MessageRecord> RetractMessageAsync(string tenantNId, string conversationNId, string messageNId, string userNId, string reason, long? expectedOptimisticVersion, Guid? expectedConcurrencyVersion, CancellationToken cancellationToken)
    {
        var gate = Locks.GetOrAdd($"{tenantNId}:{conversationNId}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var row = await _dbContext.SqlSugar.Queryable<MessageTable>()
                .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && item.MessageNId == messageNId && !item.IsDeleted)
                .FirstAsync(cancellationToken) ?? throw new CollaborationException(404, "COLLAB_MESSAGE_NOT_FOUND", "消息不存在。");
            if (!string.Equals(row.SenderUserNId, userNId, StringComparison.Ordinal))
                throw new CollaborationException(403, "COLLAB_MESSAGE_RETRACT_FORBIDDEN", "只能撤回自己发送的消息。");
            EnsureMessageVersion(row, expectedOptimisticVersion, expectedConcurrencyVersion);
            if (row.RetractedOn is not null)
                return ToRecord(row);
            row.RetractedOn = DateTimeOffset.UtcNow;
            row.RetractedByUserNId = userNId;
            row.RetractionReason = reason;
            row.MessageStateVersion++;
            row.OptimisticVersion = row.MessageStateVersion;
            row.ConcurrencyVersion = Guid.NewGuid();
            row.LastUpdatedOn = DateTimeOffset.UtcNow;
            var sugar = _dbContext.SqlSugar;
            sugar.Ado.BeginTran();
            try
            {
                await sugar.Updateable(row).ExecuteCommandAsync(cancellationToken);
                await InsertOutboxAsync(sugar, tenantNId, "collaboration.message.retracted.v1", new
                {
                    ConversationNId = conversationNId,
                    MessageNId = messageNId,
                    MessageStateVersion = row.MessageStateVersion,
                }, row.LastUpdatedOn, cancellationToken);
                sugar.Ado.CommitTran();
            }
            catch
            {
                sugar.Ado.RollbackTran();
                throw;
            }
            return ToRecord(row);
        }
        finally { gate.Release(); }
    }

    public async Task<AttachmentRecord?> GetAttachmentAsync(string tenantNId, string conversationNId, string attachmentNId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<AttachmentTable>()
            .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && item.AttachmentNId == attachmentNId && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<AttachmentRecord?> GetAttachmentByIdAsync(string tenantNId, string attachmentNId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<AttachmentTable>()
            .Where(item => item.TenantNId == tenantNId && item.AttachmentNId == attachmentNId && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<AttachmentRecord>> ListAttachmentsByFileAsync(string tenantNId, string fileNId, CancellationToken cancellationToken)
        => (await _dbContext.SqlSugar.Queryable<AttachmentTable>()
            .Where(item => item.TenantNId == tenantNId && item.FileNId == fileNId && !item.IsDeleted)
            .ToListAsync(cancellationToken))
            .Select(ToRecord)
            .ToArray();

    public async Task<AttachmentRecord> CreateAttachmentAsync(AttachmentRecord attachment, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Insertable(ToRow(attachment)).ExecuteCommandAsync(cancellationToken);
        return attachment;
    }

    public async Task<AttachmentRecord> UpdateAttachmentAsync(AttachmentRecord attachment, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Updateable(ToRow(attachment)).ExecuteCommandAsync(cancellationToken);
        return attachment;
    }

    public async Task<IReadOnlyList<MessageRecord>> SearchComplianceMessagesAsync(string tenantNId, ComplianceScopeDto scope, string? keyword, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.SqlSugar.Queryable<MessageTable>()
            .Where(item => item.TenantNId == tenantNId && !item.IsDeleted);
        var conversations = scope.ConversationNIds?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        var users = scope.UserNIds?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        var messageIds = scope.MessageNIds?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        var fromOn = scope.FromOn ?? scope.From;
        var toOn = scope.ToOn ?? scope.Until;
        if (!string.IsNullOrWhiteSpace(scope.ConversationNId))
            query = query.Where(item => item.ConversationNId == scope.ConversationNId);
        if (conversations is { Length: > 0 })
            query = query.Where(item => conversations.Contains(item.ConversationNId));
        if (users is { Length: > 0 })
            query = query.Where(item => users.Contains(item.SenderUserNId));
        if (messageIds is { Length: > 0 })
            query = query.Where(item => messageIds.Contains(item.MessageNId));
        if (fromOn is not null)
            query = query.Where(item => item.AcceptedOn >= fromOn.Value);
        if (toOn is not null)
            query = query.Where(item => item.AcceptedOn < toOn.Value);
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(item => item.TextContent != null && item.TextContent.Contains(keyword));

        var rows = await query
            .OrderBy(item => item.AcceptedOn, SqlSugar.OrderByType.Desc)
            .Skip(Math.Max(page - 1, 0) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<ComplianceDispositionRecord> CreateDispositionAsync(ComplianceDispositionRecord disposition, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await sugar.Insertable(ToRow(disposition)).ExecuteCommandAsync(cancellationToken);
            if (string.Equals(disposition.SubjectType, "message", StringComparison.OrdinalIgnoreCase))
            {
                var message = await sugar.Queryable<MessageTable>()
                    .Where(item => item.TenantNId == disposition.TenantNId && item.MessageNId == disposition.SubjectNId && !item.IsDeleted)
                    .FirstAsync(cancellationToken)
                    ?? throw new CollaborationException(404, "COLLAB_MESSAGE_NOT_FOUND", "处置目标消息不存在。");
                message.MessageStateVersion++;
                message.OptimisticVersion = message.MessageStateVersion;
                message.ConcurrencyVersion = Guid.NewGuid();
                message.LastUpdatedOn = DateTimeOffset.UtcNow;
                await sugar.Updateable(message).ExecuteCommandAsync(cancellationToken);
            }
            sugar.Ado.CommitTran();
            return disposition;
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task<IReadOnlyList<ComplianceDispositionRecord>> ListDispositionsAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<DispositionTable>()
            .Where(item => item.TenantNId == tenantNId && !item.IsDeleted)
            .OrderBy(item => item.CreatedOn, SqlSugar.OrderByType.Desc)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<LegalHoldRecord> CreateLegalHoldAsync(LegalHoldRecord hold, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Insertable(ToRow(hold)).ExecuteCommandAsync(cancellationToken);
        return hold;
    }

    public async Task<IReadOnlyList<LegalHoldRecord>> ListLegalHoldsAsync(string tenantNId, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.SqlSugar.Queryable<LegalHoldTable>()
            .Where(item => item.TenantNId == tenantNId && !item.IsDeleted);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(item => item.State == status);
        var rows = await query.OrderBy(item => item.CreatedOn, SqlSugar.OrderByType.Desc)
            .Skip(Math.Max(page - 1, 0) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<IReadOnlyList<string>> ListLegalHoldTenantsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.SqlSugar.Queryable<LegalHoldTable>()
            .Where(item => !item.IsDeleted)
            .Select(item => item.TenantNId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<LegalHoldRecord?> GetLegalHoldAsync(string tenantNId, string holdCaseNId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<LegalHoldTable>()
            .Where(item => item.TenantNId == tenantNId && item.HoldCaseNId == holdCaseNId && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<LegalHoldRecord> UpdateLegalHoldAsync(LegalHoldRecord hold, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Updateable(ToRow(hold)).ExecuteCommandAsync(cancellationToken);
        return hold;
    }

    public async Task<ComplianceExportRecord> CreateExportAsync(ComplianceExportRecord exportRecord, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Insertable(ToRow(exportRecord)).ExecuteCommandAsync(cancellationToken);
        return exportRecord;
    }

    public async Task<IReadOnlyList<ComplianceExportRecord>> ListExportsAsync(string tenantNId, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.SqlSugar.Queryable<ExportTable>()
            .Where(item => item.TenantNId == tenantNId && !item.IsDeleted);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(item => item.State == status);
        var rows = await query.OrderBy(item => item.CreatedOn, SqlSugar.OrderByType.Desc)
            .Skip(Math.Max(page - 1, 0) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToArray();
    }

    public async Task<ComplianceExportRecord?> GetExportAsync(string tenantNId, string exportNId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<ExportTable>()
            .Where(item => item.TenantNId == tenantNId && item.ExportNId == exportNId && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<ComplianceExportRecord> UpdateExportAsync(ComplianceExportRecord exportRecord, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Updateable(ToRow(exportRecord)).ExecuteCommandAsync(cancellationToken);
        return exportRecord;
    }

    public async Task<ComplianceExportRecord?> UpdateExportIfOwnedAsync(
        ComplianceExportRecord exportRecord,
        string workerLeaseNId,
        long expectedOptimisticVersion,
        CancellationToken cancellationToken)
    {
        var affected = await _dbContext.SqlSugar.Updateable(ToRow(exportRecord))
            .Where(item => item.TenantNId == exportRecord.TenantNId
                && item.ExportNId == exportRecord.ExportNId
                && !item.IsDeleted
                && (item.State == "Queued" || item.State == "Running")
                && item.WorkerLeaseNId == workerLeaseNId
                && item.OptimisticVersion == expectedOptimisticVersion)
            .ExecuteCommandAsync(cancellationToken);
        return affected == 1
            ? await GetExportAsync(exportRecord.TenantNId, exportRecord.ExportNId, cancellationToken)
            : null;
    }

    public async Task<ComplianceExportRecord?> TryClaimExportAsync(
        string tenantNId,
        string exportNId,
        string workerLeaseNId,
        DateTimeOffset leaseUntil,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var affected = await _dbContext.SqlSugar.Updateable<ExportTable>()
            .SetColumns(item => new ExportTable
            {
                WorkerLeaseNId = workerLeaseNId,
                WorkerLeaseUntil = leaseUntil,
            })
            .Where(item => item.TenantNId == tenantNId
                && item.ExportNId == exportNId
                && !item.IsDeleted
                && (item.State == "Queued" || item.State == "Running")
                && (item.WorkerLeaseNId == null || item.WorkerLeaseUntil == null || item.WorkerLeaseUntil <= now))
            .ExecuteCommandAsync(cancellationToken);
        if (affected != 1)
            return null;
        var claimed = await GetExportAsync(tenantNId, exportNId, cancellationToken);
        return claimed?.WorkerLeaseNId == workerLeaseNId ? claimed : null;
    }

    public async Task<IReadOnlyList<string>> ListExportTenantsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.SqlSugar.Queryable<ExportTable>()
            .Where(item => !item.IsDeleted)
            .Select(item => item.TenantNId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<ComplianceViewBudgetReservation> ReserveComplianceViewBudgetAsync(
        string tenantNId,
        string actorUserNId,
        DateTimeOffset windowStartOn,
        int amount,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
            return new ComplianceViewBudgetReservation(windowStartOn, 0, 200);

        var dbType = _dbContext.SqlSugar.CurrentConnectionConfig.DbType;
        var sql = dbType == SqlSugar.DbType.PostgreSQL
            ? """
              INSERT INTO collaboration_compliance_view_budget (tenant_n_id, actor_user_n_id, window_start_on, result_count)
              VALUES (@TenantNId, @ActorUserNId, @WindowStartOn, @Amount)
              ON CONFLICT (tenant_n_id, actor_user_n_id, window_start_on)
              DO UPDATE SET result_count = collaboration_compliance_view_budget.result_count + EXCLUDED.result_count
              WHERE collaboration_compliance_view_budget.result_count + EXCLUDED.result_count <= 200
              """
            : """
              INSERT INTO collaboration_compliance_view_budget (tenant_n_id, actor_user_n_id, window_start_on, result_count)
              VALUES (@TenantNId, @ActorUserNId, @WindowStartOn, @Amount)
              ON CONFLICT(tenant_n_id, actor_user_n_id, window_start_on)
              DO UPDATE SET result_count = result_count + excluded.result_count
              WHERE result_count + excluded.result_count <= 200
              """;
        var affected = await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            sql,
            new SqlSugar.SugarParameter("@TenantNId", tenantNId),
            new SqlSugar.SugarParameter("@ActorUserNId", actorUserNId),
            new SqlSugar.SugarParameter("@WindowStartOn", windowStartOn),
            new SqlSugar.SugarParameter("@Amount", amount));

        var row = await _dbContext.SqlSugar.Queryable<ComplianceViewBudgetTable>()
            .Where(item => item.TenantNId == tenantNId && item.ActorUserNId == actorUserNId && item.WindowStartOn == windowStartOn)
            .FirstAsync(cancellationToken);
        var reserved = row?.ResultCount ?? 0;
        return new ComplianceViewBudgetReservation(windowStartOn, affected == 1 ? amount : -1, Math.Max(0, 200 - reserved));
    }

    public async Task<ComplianceViewBudgetReservation> ReleaseComplianceViewBudgetAsync(
        string tenantNId,
        string actorUserNId,
        DateTimeOffset windowStartOn,
        int amount,
        CancellationToken cancellationToken)
    {
        if (amount > 0)
        {
            await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
                "UPDATE collaboration_compliance_view_budget SET result_count = CASE WHEN result_count > @Amount THEN result_count - @Amount ELSE 0 END WHERE tenant_n_id = @TenantNId AND actor_user_n_id = @ActorUserNId AND window_start_on = @WindowStartOn",
                new SqlSugar.SugarParameter("@Amount", amount),
                new SqlSugar.SugarParameter("@TenantNId", tenantNId),
                new SqlSugar.SugarParameter("@ActorUserNId", actorUserNId),
                new SqlSugar.SugarParameter("@WindowStartOn", windowStartOn));
        }

        var row = await _dbContext.SqlSugar.Queryable<ComplianceViewBudgetTable>()
            .Where(item => item.TenantNId == tenantNId && item.ActorUserNId == actorUserNId && item.WindowStartOn == windowStartOn)
            .FirstAsync(cancellationToken);
        var reserved = row?.ResultCount ?? 0;
        return new ComplianceViewBudgetReservation(windowStartOn, reserved, Math.Max(0, 200 - reserved));
    }

    public async Task<CompliancePreparationRecord?> GetCompliancePreparationAsync(
        string tenantNId,
        string actorUserNId,
        string requestNId,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<CompliancePreparationTable>()
            .Where(item => item.TenantNId == tenantNId && item.ActorUserNId == actorUserNId && item.RequestNId == requestNId)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<CompliancePreparationRecord> SaveCompliancePreparationAsync(
        CompliancePreparationRecord preparation,
        CancellationToken cancellationToken)
    {
        var existing = await GetCompliancePreparationAsync(preparation.TenantNId, preparation.ActorUserNId, preparation.RequestNId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, preparation.RequestHash, StringComparison.Ordinal))
                throw new CollaborationException(409, "COLLAB_COMPLIANCE_IDEMPOTENCY_CONFLICT", "同一请求标识对应了不同的合规准备内容。");
            return existing;
        }

        try
        {
            await _dbContext.SqlSugar.Insertable(ToRow(preparation)).ExecuteCommandAsync(cancellationToken);
            return preparation;
        }
        catch
        {
            var raced = await GetCompliancePreparationAsync(preparation.TenantNId, preparation.ActorUserNId, preparation.RequestNId, cancellationToken);
            if (raced is not null)
            {
                if (!string.Equals(raced.RequestHash, preparation.RequestHash, StringComparison.Ordinal))
                    throw new CollaborationException(409, "COLLAB_COMPLIANCE_IDEMPOTENCY_CONFLICT", "同一请求标识对应了不同的合规准备内容。");
                return raced;
            }
            throw;
        }
    }

    public async Task<ComplianceCommandRecord?> GetComplianceCommandAsync(
        string tenantNId,
        string actorUserNId,
        string requestNId,
        CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<ComplianceCommandTable>()
            .Where(item => item.TenantNId == tenantNId && item.ActorUserNId == actorUserNId && item.RequestNId == requestNId)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<ComplianceCommandRecord> SaveComplianceCommandAsync(
        ComplianceCommandRecord command,
        CancellationToken cancellationToken)
    {
        var existing = await GetComplianceCommandAsync(command.TenantNId, command.ActorUserNId, command.RequestNId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, command.RequestHash, StringComparison.Ordinal))
                throw new CollaborationException(409, "COLLAB_COMPLIANCE_IDEMPOTENCY_CONFLICT", "同一请求标识对应了不同的合规命令。");
            return existing;
        }

        try
        {
            await _dbContext.SqlSugar.Insertable(ToRow(command)).ExecuteCommandAsync(cancellationToken);
            return command;
        }
        catch
        {
            var raced = await GetComplianceCommandAsync(command.TenantNId, command.ActorUserNId, command.RequestNId, cancellationToken);
            if (raced is not null)
            {
                if (!string.Equals(raced.RequestHash, command.RequestHash, StringComparison.Ordinal))
                    throw new CollaborationException(409, "COLLAB_COMPLIANCE_IDEMPOTENCY_CONFLICT", "同一请求标识对应了不同的合规命令。");
                return raced;
            }
            throw;
        }
    }

    public async Task<bool> TryClaimComplianceCommandAsync(
        string tenantNId,
        string actorUserNId,
        string requestNId,
        string requestHash,
        DateTimeOffset claimedOn,
        CancellationToken cancellationToken)
    {
        var affected = await _dbContext.SqlSugar.Updateable<ComplianceCommandTable>()
            .SetColumns(item => new ComplianceCommandTable
            {
                Status = "Claimed",
                CompletedOn = claimedOn,
            })
            .Where(item => item.TenantNId == tenantNId
                && item.ActorUserNId == actorUserNId
                && item.RequestNId == requestNId
                && item.RequestHash == requestHash
                && item.Action == "compliance.export.download"
                && item.Status == "Authorized")
            .ExecuteCommandAsync(cancellationToken);
        return affected == 1;
    }

    public async Task<RetentionPolicyRecord> GetRetentionPolicyAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<RetentionPolicyTable>()
            .Where(item => item.TenantNId == tenantNId && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        if (row is not null)
            return ToRecord(row);
        var now = DateTimeOffset.UtcNow;
        var policy = new RetentionPolicyRecord(tenantNId, "default", 365, 365, 365, true, 1, Guid.NewGuid());
        await _dbContext.SqlSugar.Insertable(ToRow(policy, now)).ExecuteCommandAsync(cancellationToken);
        return policy;
    }

    public async Task<RetentionPolicyRecord> UpdateRetentionPolicyAsync(RetentionPolicyRecord policy, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Updateable(ToRow(policy)).ExecuteCommandAsync(cancellationToken);
        return policy;
    }

    public async Task<IReadOnlyList<string>> ListRetentionTenantsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.SqlSugar.Queryable<RetentionPolicyTable>()
            .Where(item => !item.IsDeleted && item.Enabled && item.Status == "Active")
            .Select(item => item.TenantNId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<RetentionSweepResult> RunRetentionSweepAsync(
        string tenantNId,
        string workerNId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var policy = await GetRetentionPolicyAsync(tenantNId, cancellationToken);
        if (!policy.Enabled || !string.Equals(policy.Status, "Active", StringComparison.Ordinal))
            return new RetentionSweepResult(0, [], true);

        var sugar = _dbContext.SqlSugar;
        var checkpoint = await sugar.Queryable<RetentionCheckpointTable>()
            .Where(item => item.TenantNId == tenantNId && item.Stage != "Complete")
            .OrderBy(item => item.UpdatedOn, OrderByType.Desc)
            .FirstAsync(cancellationToken);
        if (checkpoint is not null && ReadUtcTimestamp(checkpoint.LeaseExpiresOn) > now && !string.Equals(checkpoint.LeaseOwner, workerNId, StringComparison.Ordinal))
            return new RetentionSweepResult(0, [], false);

        var cutoff = now.AddDays(-policy.MessageRetentionDays);
        if (checkpoint is null || ReadUtcTimestamp(checkpoint.CutoffOn) != cutoff)
        {
            checkpoint = new RetentionCheckpointTable
            {
                Id = Guid.NewGuid(),
                TenantNId = tenantNId,
                OperationNId = "RET-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant(),
                CutoffOn = cutoff,
                Stage = "Inspect",
                UpdatedOn = now,
            };
            await sugar.Insertable(checkpoint).ExecuteCommandAsync(cancellationToken);
        }

        var claimed = await sugar.Updateable<RetentionCheckpointTable>()
            .SetColumns(item => new RetentionCheckpointTable
            {
                LeaseOwner = workerNId,
                LeaseExpiresOn = now.AddMinutes(2),
                UpdatedOn = now,
                ErrorCode = null,
            })
            .Where(item => item.Id == checkpoint.Id
                && (item.LeaseOwner == null || item.LeaseExpiresOn == null || item.LeaseExpiresOn <= now || item.LeaseOwner == workerNId))
            .ExecuteCommandAsync(cancellationToken);
        if (claimed != 1)
            return new RetentionSweepResult(0, [], false);

        try
        {
            var holds = (await sugar.Queryable<LegalHoldTable>()
                .Where(item => item.TenantNId == tenantNId && !item.IsDeleted && item.State != "Released")
                .ToListAsync(cancellationToken)).Select(ToRecord).ToArray();
            var candidates = await sugar.Queryable<MessageTable>()
                .Where(item => item.TenantNId == tenantNId && !item.IsDeleted && item.AcceptedOn < cutoff)
                .OrderBy(item => item.AcceptedOn, OrderByType.Asc)
                .OrderBy(item => item.Id, OrderByType.Asc)
                .Take(100)
                .ToListAsync(cancellationToken);
            var files = new List<RetentionFileReference>();
            var purgedConversations = new Dictionary<string, long>(StringComparer.Ordinal);
            var purged = 0;
            foreach (var message in candidates)
            {
                var record = ToRecord(message);
                if (IsCoveredByLegalHold(record, holds))
                    continue;

                if (message.AttachmentNId is { Length: > 0 } attachmentNId)
                {
                    var attachment = await sugar.Queryable<AttachmentTable>()
                        .Where(item => item.TenantNId == tenantNId && item.AttachmentNId == attachmentNId && !item.IsDeleted)
                        .FirstAsync(cancellationToken);
                    if (attachment?.FileNId is { Length: > 0 } fileNId && attachment.ReferenceNId is { Length: > 0 } referenceNId && attachment.RetentionState != "Released")
                    {
                        attachment.RetentionState = "ReleasePending";
                        attachment.LastUpdatedOn = now;
                        await sugar.Updateable(attachment).ExecuteCommandAsync(cancellationToken);
                        files.Add(new RetentionFileReference(attachment.AttachmentNId, fileNId, referenceNId, attachment.UploaderUserNId, checkpoint.OperationNId));
                        continue;
                    }
                    if (attachment is not null && attachment.RetentionState != "Released")
                        continue;
                }

                message.IsDeleted = true;
                message.TextContent = null;
                message.LastUpdatedOn = now;
                await sugar.Updateable(message).ExecuteCommandAsync(cancellationToken);
                purgedConversations[message.ConversationNId] = Math.Max(
                    purgedConversations.GetValueOrDefault(message.ConversationNId),
                    message.Sequence);
                purged++;
            }

            foreach (var group in purgedConversations)
                await AdvanceRetentionFloorAsync(sugar, tenantNId, group.Key, group.Value, cancellationToken);

            var complete = candidates.Count < 100;
            var lastCandidate = candidates.Count == 0 ? null : candidates[candidates.Count - 1];
            await sugar.Updateable<RetentionCheckpointTable>()
                .SetColumns(item => new RetentionCheckpointTable
                {
                    LastAcceptedOn = lastCandidate == null ? item.LastAcceptedOn : lastCandidate.AcceptedOn,
                    LastMessageId = lastCandidate == null ? item.LastMessageId : lastCandidate.Id,
                    Stage = files.Count > 0 ? "ReleaseFile" : complete ? "Complete" : "Inspect",
                    LeaseOwner = null,
                    LeaseExpiresOn = null,
                    UpdatedOn = now,
                    ErrorCode = null,
                })
                .Where(item => item.Id == checkpoint.Id)
                .ExecuteCommandAsync(cancellationToken);
            return new RetentionSweepResult(purged, files, complete && files.Count == 0);
        }
        catch
        {
            await sugar.Updateable<RetentionCheckpointTable>()
                .SetColumns(item => new RetentionCheckpointTable
                {
                    Stage = "Failed",
                    LeaseOwner = null,
                    LeaseExpiresOn = null,
                    UpdatedOn = DateTimeOffset.UtcNow,
                    ErrorCode = "COLLAB_RETENTION_SWEEP_FAILED",
                })
                .Where(item => item.Id == checkpoint.Id)
                .ExecuteCommandAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task MarkRetentionAttachmentReleasedAsync(string tenantNId, string attachmentNId, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Updateable<AttachmentTable>()
            .SetColumns(item => new AttachmentTable
            {
                RetentionState = "Released",
                ReferenceState = "Released",
                LastUpdatedOn = DateTimeOffset.UtcNow,
            })
            .Where(item => item.TenantNId == tenantNId && item.AttachmentNId == attachmentNId && !item.IsDeleted)
            .ExecuteCommandAsync(cancellationToken);
    }

    private static bool IsCoveredByLegalHold(MessageRecord message, IReadOnlyList<LegalHoldRecord> holds)
    {
        foreach (var hold in holds)
        {
            ComplianceScopeDto? scope;
            try { scope = JsonSerializer.Deserialize<ComplianceScopeDto>(hold.ScopeJson); }
            catch (JsonException) { continue; }
            if (scope is null) continue;
            if (scope.MessageNIds?.Contains(message.MessageNId, StringComparer.Ordinal) == true
                || string.Equals(scope.ConversationNId, message.ConversationNId, StringComparison.Ordinal)
                || scope.ConversationNIds?.Contains(message.ConversationNId, StringComparer.Ordinal) == true
                || scope.UserNIds?.Contains(message.SenderUserNId, StringComparer.Ordinal) == true)
                return true;
            var from = scope.FromOn ?? scope.From;
            var until = scope.ToOn ?? scope.Until;
            if ((from is null || message.AcceptedOn >= from.Value) && (until is null || message.AcceptedOn < until.Value)
                && (from is not null || until is not null))
                return true;
        }
        return false;
    }

    private static async Task AdvanceRetentionFloorAsync(ISqlSugarClient sugar, string tenantNId, string conversationNId, long maximumSequence, CancellationToken cancellationToken)
    {
        var conversation = await sugar.Queryable<ConversationTable>()
            .Where(item => item.TenantNId == tenantNId && item.NId == conversationNId && !item.IsDeleted)
            .FirstAsync(cancellationToken);
        if (conversation is null || conversation.RetentionFloorSequence >= maximumSequence)
            return;
        var rows = await sugar.Queryable<MessageTable>()
            .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && item.Sequence > conversation.RetentionFloorSequence && item.Sequence <= maximumSequence)
            .OrderBy(item => item.Sequence, OrderByType.Asc)
            .ToListAsync(cancellationToken);
        var floor = conversation.RetentionFloorSequence;
        foreach (var row in rows)
        {
            if (!row.IsDeleted) break;
            floor = row.Sequence;
        }
        if (floor == conversation.RetentionFloorSequence)
            return;
        conversation.RetentionFloorSequence = floor;
        conversation.LastUpdatedOn = DateTimeOffset.UtcNow;
        await sugar.Updateable(conversation).ExecuteCommandAsync(cancellationToken);
    }

    private async Task<ConversationMemberTable> RequireMemberRowAsync(string tenantNId, string conversationNId, string userNId, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Queryable<ConversationMemberTable>()
            .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && item.UserNId == userNId && !item.IsDeleted)
            .FirstAsync(cancellationToken) ?? throw new CollaborationException(404, "COLLAB_CONVERSATION_MEMBER_NOT_FOUND", "会话成员不存在。");

    private static ConversationTable ToRow(ConversationRecord item) => new()
    {
        Id = StableGuid(item.ConversationNId), IsFrozen = false, IsLocked = false, IsDeleted = false, EntityType = "collaboration.conversation", CreatedOn = item.LastMessageOn ?? DateTimeOffset.UtcNow, LastUpdatedOn = DateTimeOffset.UtcNow, OptimisticVersion = item.OptimisticVersion, ConcurrencyVersion = item.ConcurrencyVersion, TenantNId = item.TenantNId, NId = item.ConversationNId, ParticipantLowUserNId = item.ParticipantLowUserNId, ParticipantHighUserNId = item.ParticipantHighUserNId, Status = item.Status, LastMessageSequence = item.LastMessageSequence, LastMessageNId = item.LastMessageNId, LastMessageOn = item.LastMessageOn, RetentionFloorSequence = item.RetentionFloorSequence,
    };

    private static ConversationMemberTable ToRow(ConversationMemberRecord item) => new()
    {
        Id = StableGuid($"{item.ConversationNId}:{item.UserNId}"), IsFrozen = false, IsLocked = false, IsDeleted = false, EntityType = "collaboration.conversation_member", CreatedOn = item.JoinedOn, LastUpdatedOn = item.JoinedOn, OptimisticVersion = item.ProjectionVersion, ConcurrencyVersion = item.ConcurrencyVersion == Guid.Empty ? Guid.NewGuid() : item.ConcurrencyVersion, TenantNId = item.TenantNId, ConversationNId = item.ConversationNId, UserNId = item.UserNId, DisplayNameSnapshot = item.DisplayNameSnapshot, JoinedOn = item.JoinedOn, VisibilityState = item.VisibilityState, HiddenOn = item.HiddenOn, HiddenThroughSequence = item.HiddenThroughSequence, LastReadSequence = item.LastReadSequence, LastReadOn = item.LastReadOn, UnreadCount = item.UnreadCount, ProjectionVersion = item.ProjectionVersion,
    };

    private static MessageTable ToRow(MessageRecord item) => new()
    {
        Id = StableGuid(item.MessageNId), IsFrozen = false, IsLocked = false, IsDeleted = false, EntityType = "collaboration.message", CreatedOn = item.AcceptedOn, LastUpdatedOn = item.AcceptedOn, OptimisticVersion = item.MessageStateVersion, ConcurrencyVersion = item.ConcurrencyVersion, TenantNId = item.TenantNId, ConversationNId = item.ConversationNId, MessageNId = item.MessageNId, Sequence = item.Sequence, SenderUserNId = item.SenderUserNId, ClientMessageNId = item.ClientMessageNId, RequestHash = item.RequestHash, MessageType = item.MessageType, TextContent = item.TextContent, ReplyToMessageNId = item.ReplyToMessageNId, AttachmentNId = item.AttachmentNId, AcceptedOn = item.AcceptedOn, RetractedOn = item.RetractedOn, RetractedByUserNId = item.RetractedByUserNId, RetractionReason = item.RetractionReason, MessageStateVersion = item.MessageStateVersion,
    };

    private static AttachmentTable ToRow(AttachmentRecord item) => new()
    {
        Id = StableGuid(item.AttachmentNId), IsFrozen = false, IsLocked = false, IsDeleted = false, EntityType = "collaboration.chat_attachment", CreatedOn = item.CreatedOn, LastUpdatedOn = item.LastUpdatedOn, OptimisticVersion = 1, ConcurrencyVersion = Guid.NewGuid(), TenantNId = item.TenantNId, ConversationNId = item.ConversationNId, AttachmentNId = item.AttachmentNId, UploaderUserNId = item.UploaderUserNId, FileNId = item.FileNId, FileNameSnapshot = item.FileNameSnapshot, ContentTypeSnapshot = item.ContentTypeSnapshot, SizeSnapshot = item.SizeSnapshot, Purpose = item.Purpose, FileStateProjection = item.FileStateProjection, FileStateVersion = item.FileStateVersion, FileObservedOn = item.FileObservedOn, ReferenceState = item.ReferenceState, RetentionState = item.RetentionState, BoundMessageNId = item.BoundMessageNId, IntentRequestNId = item.IntentRequestNId, IntentRequestHash = item.IntentRequestHash, ReferenceNId = item.ReferenceNId,
    };

    private static DispositionTable ToRow(ComplianceDispositionRecord item) => new()
    {
        Id = StableGuid(item.DispositionNId), IsFrozen = false, IsLocked = false, IsDeleted = false, EntityType = "collaboration.compliance_disposition", CreatedOn = item.CreatedOn, LastUpdatedOn = item.CreatedOn, OptimisticVersion = item.OptimisticVersion, ConcurrencyVersion = item.ConcurrencyVersion, TenantNId = item.TenantNId, DispositionNId = item.DispositionNId, SubjectType = item.SubjectType, SubjectNId = item.SubjectNId, State = item.State, Reason = item.Reason, CreatedByUserNId = item.CreatedByUserNId, ExpiresOn = item.ExpiresOn, RequestNId = item.RequestNId, RequestHash = item.RequestHash,
    };

    private static LegalHoldTable ToRow(LegalHoldRecord item) => new()
    {
        Id = StableGuid(item.HoldCaseNId), IsFrozen = false, IsLocked = false, IsDeleted = false, EntityType = "collaboration.legal_hold_case", CreatedOn = item.CreatedOn, LastUpdatedOn = item.CreatedOn, OptimisticVersion = item.OptimisticVersion, ConcurrencyVersion = item.ConcurrencyVersion, TenantNId = item.TenantNId, HoldCaseNId = item.HoldCaseNId, State = item.State, ScopeJson = item.ScopeJson, ScopeChecksum = item.ScopeChecksum, Reason = item.Reason, CreatedByUserNId = item.CreatedByUserNId, ReleasedOn = item.ReleasedOn, RequestNId = item.RequestNId, RequestHash = item.RequestHash, OperationId = item.OperationId, ReleaseRequestedByUserNId = item.ReleaseRequestedByUserNId, ReleaseRequestNId = item.ReleaseRequestNId, ReleaseApprovalExpiresOn = item.ReleaseApprovalExpiresOn, ReleaseApprovedByUserNId = item.ReleaseApprovedByUserNId, ReviewedByUserNId = item.ReviewedByUserNId, ReviewedOn = item.ReviewedOn, ExternalCaseReference = item.ExternalCaseReference, FileSyncState = item.FileSyncState,
    };

    private static ExportTable ToRow(ComplianceExportRecord item) => new()
    {
        Id = StableGuid(item.ExportNId), IsFrozen = false, IsLocked = false, IsDeleted = false, EntityType = "collaboration.compliance_export", CreatedOn = item.CreatedOn, LastUpdatedOn = item.CreatedOn, OptimisticVersion = item.OptimisticVersion, ConcurrencyVersion = item.ConcurrencyVersion, TenantNId = item.TenantNId, ExportNId = item.ExportNId, State = item.State, ScopeJson = item.ScopeJson, ScopeChecksum = item.ScopeChecksum, Reason = item.Reason, CreatedByUserNId = item.CreatedByUserNId, ExpiresOn = item.ExpiresOn, ArtifactReference = item.ArtifactReference, RequestNId = item.RequestNId, RequestHash = item.RequestHash, CaseReference = item.CaseReference, ApprovedByUserNId = item.ApprovedByUserNId, ApprovedOn = item.ApprovedOn, ApprovalExpiresOn = item.ApprovalExpiresOn, ApprovalConsumedOn = item.ApprovalConsumedOn, RunDeadlineOn = item.RunDeadlineOn, CompletedOn = item.CompletedOn, ErrorCode = item.ErrorCode, FieldsJson = item.FieldsJson, OperationId = item.OperationId, ExportRetentionHours = item.ExportRetentionHours, WorkerLeaseNId = item.WorkerLeaseNId, WorkerLeaseUntil = item.WorkerLeaseUntil,
    };

    private static RetentionPolicyTable ToRow(RetentionPolicyRecord item, DateTimeOffset? createdOn = null) => new()
    {
        Id = StableGuid($"{item.TenantNId}:{item.PolicyNId}"), IsFrozen = false, IsLocked = false, IsDeleted = false, EntityType = "collaboration.retention_policy", CreatedOn = createdOn ?? DateTimeOffset.UtcNow, LastUpdatedOn = DateTimeOffset.UtcNow, OptimisticVersion = item.OptimisticVersion, ConcurrencyVersion = item.ConcurrencyVersion, TenantNId = item.TenantNId, PolicyNId = item.PolicyNId, MessageRetentionDays = item.MessageRetentionDays, AttachmentRetentionDays = item.AttachmentRetentionDays, AuditRetentionDays = item.AuditRetentionDays, Enabled = item.Enabled, ExportRetentionHours = item.ExportRetentionHours, ReviewDueHours = item.ReviewDueHours, Status = item.Status,
    };

    private static CompliancePreparationTable ToRow(CompliancePreparationRecord item) => new()
    {
        TenantNId = item.TenantNId,
        ActorUserNId = item.ActorUserNId,
        RequestNId = item.RequestNId,
        ActorSessionNId = item.ActorSessionNId,
        Action = item.Action,
        TargetNId = item.TargetNId,
        RequestHash = item.RequestHash,
        ScopeChecksum = item.ScopeChecksum,
        CommandJson = item.CommandJson,
        SnapshotJson = item.SnapshotJson,
        PreparedOn = item.PreparedOn,
        ExpiresOn = item.ExpiresOn,
    };

    private static ComplianceCommandTable ToRow(ComplianceCommandRecord item) => new()
    {
        TenantNId = item.TenantNId,
        ActorUserNId = item.ActorUserNId,
        RequestNId = item.RequestNId,
        Action = item.Action,
        TargetNId = item.TargetNId,
        RequestHash = item.RequestHash,
        ScopeChecksum = item.ScopeChecksum,
        CommandJson = item.CommandJson,
        Status = item.Status,
        ResultJson = item.ResultJson,
        CompletedOn = item.CompletedOn,
    };

    private ConversationRecord ToRecord(ConversationTable item) => new(item.TenantNId, item.NId, item.ParticipantLowUserNId, item.ParticipantHighUserNId, item.Status, item.LastMessageSequence, item.LastMessageNId, ReadUtcTimestamp(item.LastMessageOn), item.RetentionFloorSequence, item.OptimisticVersion, item.ConcurrencyVersion);
    private ConversationMemberRecord ToRecord(ConversationMemberTable item) => new(item.TenantNId, item.ConversationNId, item.UserNId, item.DisplayNameSnapshot, ReadUtcTimestamp(item.JoinedOn), item.VisibilityState, ReadUtcTimestamp(item.HiddenOn), item.HiddenThroughSequence, item.LastReadSequence, ReadUtcTimestamp(item.LastReadOn), item.UnreadCount, item.ProjectionVersion, item.ConcurrencyVersion);
    private MessageRecord ToRecord(MessageTable item) => new(item.TenantNId, item.ConversationNId, item.MessageNId, item.Sequence, item.SenderUserNId, item.ClientMessageNId, item.RequestHash, item.MessageType, item.TextContent, item.ReplyToMessageNId, item.AttachmentNId, ReadUtcTimestamp(item.AcceptedOn), item.RetractedOn is null ? null : ReadUtcTimestamp(item.RetractedOn.Value), item.RetractedByUserNId, item.RetractionReason, item.MessageStateVersion, item.ConcurrencyVersion);
    private AttachmentRecord ToRecord(AttachmentTable item) => new(item.TenantNId, item.ConversationNId, item.AttachmentNId, item.UploaderUserNId, item.FileNId, item.FileNameSnapshot, item.ContentTypeSnapshot, item.SizeSnapshot, item.Purpose, item.FileStateProjection, item.FileStateVersion, ReadUtcTimestamp(item.FileObservedOn), item.ReferenceState, item.RetentionState, item.BoundMessageNId, item.IntentRequestNId, item.IntentRequestHash, ReadUtcTimestamp(item.CreatedOn), ReadUtcTimestamp(item.LastUpdatedOn), item.ReferenceNId);
    private ComplianceDispositionRecord ToRecord(DispositionTable item) => new(item.TenantNId, item.DispositionNId, item.SubjectType, item.SubjectNId, item.State, item.Reason, item.CreatedByUserNId, ReadUtcTimestamp(item.CreatedOn), ReadUtcTimestamp(item.ExpiresOn), item.OptimisticVersion, item.ConcurrencyVersion, item.RequestNId, item.RequestHash);
    private LegalHoldRecord ToRecord(LegalHoldTable item) => new(item.TenantNId, item.HoldCaseNId, item.State, item.ScopeJson, item.ScopeChecksum, item.Reason, item.CreatedByUserNId, ReadUtcTimestamp(item.CreatedOn), ReadUtcTimestamp(item.ReleasedOn), item.OptimisticVersion, item.ConcurrencyVersion, item.RequestNId, item.RequestHash, item.OperationId, item.ReleaseRequestedByUserNId, item.ReleaseRequestNId, ReadUtcTimestamp(item.ReleaseApprovalExpiresOn), item.ReleaseApprovedByUserNId, item.ReviewedByUserNId, ReadUtcTimestamp(item.ReviewedOn), item.ExternalCaseReference, item.FileSyncState);
    private ComplianceExportRecord ToRecord(ExportTable item) => new(item.TenantNId, item.ExportNId, item.State, item.ScopeJson, item.ScopeChecksum, item.Reason, item.CreatedByUserNId, ReadUtcTimestamp(item.CreatedOn), ReadUtcTimestamp(item.ExpiresOn), item.ArtifactReference, item.OptimisticVersion, item.ConcurrencyVersion, item.RequestNId, item.RequestHash, item.CaseReference, item.ApprovedByUserNId, ReadUtcTimestamp(item.ApprovedOn), ReadUtcTimestamp(item.ApprovalExpiresOn), ReadUtcTimestamp(item.ApprovalConsumedOn), ReadUtcTimestamp(item.RunDeadlineOn), ReadUtcTimestamp(item.CompletedOn), item.ErrorCode, item.FieldsJson, item.OperationId, item.ExportRetentionHours, item.WorkerLeaseNId, ReadUtcTimestamp(item.WorkerLeaseUntil));
    private static RetentionPolicyRecord ToRecord(RetentionPolicyTable item) => new(item.TenantNId, item.PolicyNId, item.MessageRetentionDays, item.AttachmentRetentionDays, item.AuditRetentionDays, item.Enabled, item.OptimisticVersion, item.ConcurrencyVersion, item.ExportRetentionHours, item.ReviewDueHours, item.Status);
    private CompliancePreparationRecord ToRecord(CompliancePreparationTable item) => new(item.TenantNId, item.ActorUserNId, item.RequestNId, item.ActorSessionNId, item.Action, item.TargetNId, item.RequestHash, item.ScopeChecksum, item.CommandJson, item.SnapshotJson, ReadUtcTimestamp(item.PreparedOn), ReadUtcTimestamp(item.ExpiresOn));
    private ComplianceCommandRecord ToRecord(ComplianceCommandTable item) => new(item.TenantNId, item.ActorUserNId, item.RequestNId, item.Action, item.TargetNId, item.RequestHash, item.ScopeChecksum, item.Status, item.ResultJson, ReadUtcTimestamp(item.CompletedOn), item.CommandJson);
    private CollaborationOutboxRecord ToRecord(CollaborationOutboxTable item) => new(item.EventId, item.TenantNId, item.EventType, item.Payload, ReadUtcTimestamp(item.CreatedOn), ReadUtcTimestamp(item.PublishedOn), item.RetryCount, item.LastError, item.LeaseNId, ReadUtcTimestamp(item.LeaseUntil), ReadUtcTimestamp(item.DeadLetteredOn));
    private CollaborationEventInboxRecord ToRecord(CollaborationEventInboxTable item) => new(item.EventId, item.TenantNId, item.EventType, item.Status, ReadUtcTimestamp(item.ReceivedOn), ReadUtcTimestamp(item.ProcessedOn), item.RetryCount, item.LastError);

    private DateTimeOffset ReadUtcTimestamp(DateTimeOffset value) =>
        _dbContext.SqlSugar.CurrentConnectionConfig.DbType == DbType.Sqlite
            ? new DateTimeOffset(value.DateTime, TimeSpan.Zero)
            : value;

    private DateTimeOffset? ReadUtcTimestamp(DateTimeOffset? value) => value is null ? null : ReadUtcTimestamp(value.Value);

    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                || message.Contains("23505", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Task<int> InsertOutboxAsync(
        ISqlSugarClient sugar,
        string tenantNId,
        string eventType,
        object payload,
        DateTimeOffset createdOn,
        CancellationToken cancellationToken) =>
        sugar.Insertable(new CollaborationOutboxTable
        {
            EventId = Guid.NewGuid(),
            TenantNId = tenantNId,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
            CreatedOn = createdOn,
            RetryCount = 0,
        }).ExecuteCommandAsync(cancellationToken);

    private static void EnsureMemberVersion(ConversationMemberTable row, long? expectedOptimisticVersion, Guid? expectedConcurrencyVersion)
    {
        if ((expectedOptimisticVersion is not null && expectedOptimisticVersion.Value != row.OptimisticVersion)
            || (expectedConcurrencyVersion is not null && expectedConcurrencyVersion.Value != row.ConcurrencyVersion))
            throw new CollaborationException(409, "COLLAB_CONVERSATION_CONCURRENCY_CONFLICT", "会话成员状态已发生变化，请刷新后重试。");
    }

    private static void EnsureMessageVersion(MessageTable row, long? expectedOptimisticVersion, Guid? expectedConcurrencyVersion)
    {
        if ((expectedOptimisticVersion is not null && expectedOptimisticVersion.Value != row.OptimisticVersion)
            || (expectedConcurrencyVersion is not null && expectedConcurrencyVersion.Value != row.ConcurrencyVersion))
            throw new CollaborationException(409, "COLLAB_MESSAGE_CONCURRENCY_CONFLICT", "消息状态已发生变化，请刷新后重试。");
    }

    private static Guid StableGuid(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes[..16]);
    }
}
