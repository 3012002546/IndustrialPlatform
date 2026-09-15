using System.Text.Json;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Application.RemoteAssistance;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.Infrastructure.Persistence;

public sealed partial class SqlCollaborationRepository
{
    public async Task<ScreenShareSessionRecord?> GetScreenAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken)
        => ToScreenRecord(await _dbContext.SqlSugar.Queryable<RemoteAssistanceScreenTable>()
            .Where(item => item.TenantNId == tenantNId && item.NId == sessionNId && !item.IsDeleted)
            .FirstAsync(cancellationToken));

    public async Task<ScreenShareSessionRecord?> GetScreenByRequestAsync(string tenantNId, string initiatorUserNId, string requestNId, CancellationToken cancellationToken)
        => ToScreenRecord(await _dbContext.SqlSugar.Queryable<RemoteAssistanceScreenTable>()
            .Where(item => item.TenantNId == tenantNId && item.InitiatorUserNId == initiatorUserNId && item.RequestNId == requestNId && !item.IsDeleted)
            .FirstAsync(cancellationToken));

    public async Task<ScreenShareSessionRecord?> GetActiveScreenByConversationAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken)
        => ToScreenRecord(await _dbContext.SqlSugar.Queryable<RemoteAssistanceScreenTable>()
            .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && !item.IsDeleted
                && (item.State == "Pending" || item.State == "Accepted" || item.State == "Connecting" || item.State == "Sharing"))
            .OrderBy(item => item.CreatedOn, OrderByType.Desc)
            .FirstAsync(cancellationToken));

    public async Task<ScreenShareSessionRecord?> GetLatestScreenAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken)
        => ToScreenRecord(await _dbContext.SqlSugar.Queryable<RemoteAssistanceScreenTable>()
            .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && !item.IsDeleted)
            .OrderBy(item => item.CreatedOn, OrderByType.Desc)
            .OrderBy(item => item.NId, OrderByType.Desc)
            .FirstAsync(cancellationToken));

    public async Task<ScreenShareSessionRecord> CreateScreenAsync(ScreenShareSessionRecord session, object eventPayload, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await sugar.Insertable(ToScreenRow(session)).ExecuteCommandAsync(cancellationToken);
            await InsertRemoteAssistanceOutboxAsync(sugar, session.TenantNId, "collaboration.media.changed.v1", eventPayload, session.CreatedOn, cancellationToken);
            sugar.Ado.CommitTran();
            return session;
        }
        catch (Exception exception) when (IsUniqueConstraintViolation(exception))
        {
            sugar.Ado.RollbackTran();
            var existing = await GetScreenByRequestAsync(session.TenantNId, session.InitiatorUserNId, session.RequestNId, cancellationToken);
            if (existing is not null)
            {
                if (string.Equals(existing.RequestHash, session.RequestHash, StringComparison.Ordinal))
                    return existing;
                throw new CollaborationException(409, "MEDIA_CONFLICT", "媒体请求标识已用于其他请求。");
            }
            throw new CollaborationException(409, "MEDIA_BUSY", "当前聊天的媒体资源正忙，请稍后重试。");
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task<ScreenShareSessionRecord?> UpdateScreenAsync(ScreenShareSessionRecord session, long expectedVersion, object? eventPayload, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            var affected = await sugar.Updateable(ToScreenRow(session))
                .Where(item => item.TenantNId == session.TenantNId && item.NId == session.SessionNId && !item.IsDeleted && item.OptimisticVersion == expectedVersion)
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
            {
                sugar.Ado.RollbackTran();
                return null;
            }
            if (eventPayload is not null)
                await InsertRemoteAssistanceOutboxAsync(sugar, session.TenantNId, "collaboration.media.changed.v1", eventPayload, session.LastUpdatedOn, cancellationToken);
            sugar.Ado.CommitTran();
            return await GetScreenAsync(session.TenantNId, session.SessionNId, cancellationToken);
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task<VoiceCallSessionRecord?> GetVoiceAsync(string tenantNId, string callNId, CancellationToken cancellationToken)
        => ToVoiceRecord(await _dbContext.SqlSugar.Queryable<RemoteAssistanceVoiceTable>()
            .Where(item => item.TenantNId == tenantNId && item.NId == callNId && !item.IsDeleted)
            .FirstAsync(cancellationToken));

    public async Task<VoiceCallSessionRecord?> GetVoiceByRequestAsync(string tenantNId, string callerUserNId, string requestNId, CancellationToken cancellationToken)
        => ToVoiceRecord(await _dbContext.SqlSugar.Queryable<RemoteAssistanceVoiceTable>()
            .Where(item => item.TenantNId == tenantNId && item.CallerUserNId == callerUserNId && item.RequestNId == requestNId && !item.IsDeleted)
            .FirstAsync(cancellationToken));

    public async Task<VoiceCallSessionRecord?> GetActiveVoiceByConversationAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken)
        => ToVoiceRecord(await _dbContext.SqlSugar.Queryable<RemoteAssistanceVoiceTable>()
            .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && !item.IsDeleted
                && (item.State == "Ringing" || item.State == "Accepted" || item.State == "Connecting" || item.State == "Active"))
            .OrderBy(item => item.CreatedOn, OrderByType.Desc)
            .FirstAsync(cancellationToken));

    public async Task<VoiceCallSessionRecord?> GetLatestVoiceAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken)
        => ToVoiceRecord(await _dbContext.SqlSugar.Queryable<RemoteAssistanceVoiceTable>()
            .Where(item => item.TenantNId == tenantNId && item.ConversationNId == conversationNId && !item.IsDeleted)
            .OrderBy(item => item.CreatedOn, OrderByType.Desc)
            .OrderBy(item => item.NId, OrderByType.Desc)
            .FirstAsync(cancellationToken));

    public async Task<VoiceCallSessionRecord> CreateVoiceAsync(VoiceCallSessionRecord voiceCall, IReadOnlyList<VoiceUserSlotRecord> slots, object eventPayload, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            foreach (var slot in slots.OrderBy(item => item.UserNId, StringComparer.Ordinal))
                await sugar.Insertable(ToVoiceSlotRow(slot)).ExecuteCommandAsync(cancellationToken);
            await sugar.Insertable(ToVoiceRow(voiceCall)).ExecuteCommandAsync(cancellationToken);
            await InsertRemoteAssistanceOutboxAsync(sugar, voiceCall.TenantNId, "collaboration.media.changed.v1", eventPayload, voiceCall.CreatedOn, cancellationToken);
            sugar.Ado.CommitTran();
            return voiceCall;
        }
        catch (Exception exception) when (IsUniqueConstraintViolation(exception))
        {
            sugar.Ado.RollbackTran();
            var existing = await GetVoiceByRequestAsync(voiceCall.TenantNId, voiceCall.CallerUserNId, voiceCall.RequestNId, cancellationToken);
            if (existing is not null)
            {
                if (string.Equals(existing.RequestHash, voiceCall.RequestHash, StringComparison.Ordinal))
                    return existing;
                throw new CollaborationException(409, "MEDIA_CONFLICT", "媒体请求标识已用于其他请求。");
            }
            throw new CollaborationException(409, "MEDIA_BUSY", "当前用户正在进行其他语音通话。");
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task<VoiceCallSessionRecord?> UpdateVoiceAsync(VoiceCallSessionRecord voiceCall, long expectedVersion, bool releaseSlots, object? eventPayload, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            var affected = await sugar.Updateable(ToVoiceRow(voiceCall))
                .Where(item => item.TenantNId == voiceCall.TenantNId && item.NId == voiceCall.CallNId && !item.IsDeleted && item.OptimisticVersion == expectedVersion)
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
            {
                sugar.Ado.RollbackTran();
                return null;
            }
            if (releaseSlots)
                await sugar.Deleteable<RemoteAssistanceVoiceSlotTable>().Where(item => item.TenantNId == voiceCall.TenantNId && item.VoiceCallNId == voiceCall.CallNId).ExecuteCommandAsync(cancellationToken);
            if (eventPayload is not null)
                await InsertRemoteAssistanceOutboxAsync(sugar, voiceCall.TenantNId, "collaboration.media.changed.v1", eventPayload, voiceCall.LastUpdatedOn, cancellationToken);
            sugar.Ado.CommitTran();
            return await GetVoiceAsync(voiceCall.TenantNId, voiceCall.CallNId, cancellationToken);
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task<EndAllMediaRecords?> EndAllAsync(
        ScreenShareSessionRecord? screen,
        long? screenExpectedVersion,
        object? screenEventPayload,
        VoiceCallSessionRecord? voice,
        long? voiceExpectedVersion,
        object? voiceEventPayload,
        CancellationToken cancellationToken)
    {
        if (screen is null && voice is null) return new EndAllMediaRecords(null, null);
        if (screen is not null && voice is not null && !string.Equals(screen.TenantNId, voice.TenantNId, StringComparison.Ordinal))
            throw new CollaborationException(409, "MEDIA_CONFLICT", "媒体会话不属于同一租户。");

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            var currentScreen = screen is null
                ? null
                : ToScreenRecord(await sugar.Queryable<RemoteAssistanceScreenTable>()
                    .Where(item => item.TenantNId == screen.TenantNId && item.NId == screen.SessionNId && !item.IsDeleted)
                    .FirstAsync(cancellationToken));
            var currentVoice = voice is null
                ? null
                : ToVoiceRecord(await sugar.Queryable<RemoteAssistanceVoiceTable>()
                    .Where(item => item.TenantNId == voice.TenantNId && item.NId == voice.CallNId && !item.IsDeleted)
                    .FirstAsync(cancellationToken));
            if ((screen is not null && currentScreen is null) || (voice is not null && currentVoice is null))
            {
                sugar.Ado.RollbackTran();
                return null;
            }

            if (currentScreen is not null && IsActiveScreen(currentScreen.State) && currentScreen.Version != screenExpectedVersion)
            {
                sugar.Ado.RollbackTran();
                return null;
            }
            if (currentVoice is not null && IsActiveVoice(currentVoice.State) && currentVoice.Version != voiceExpectedVersion)
            {
                sugar.Ado.RollbackTran();
                return null;
            }

            if (currentScreen is not null && IsActiveScreen(currentScreen.State))
            {
                var affected = await sugar.Updateable(ToScreenRow(screen!))
                    .Where(item => item.TenantNId == screen!.TenantNId && item.NId == screen.SessionNId && !item.IsDeleted && item.OptimisticVersion == screenExpectedVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (affected != 1)
                {
                    sugar.Ado.RollbackTran();
                    return null;
                }
                if (screenEventPayload is not null)
                    await InsertRemoteAssistanceOutboxAsync(sugar, screen!.TenantNId, "collaboration.media.changed.v1", screenEventPayload, screen.LastUpdatedOn, cancellationToken);
            }

            if (currentVoice is not null && IsActiveVoice(currentVoice.State))
            {
                var affected = await sugar.Updateable(ToVoiceRow(voice!))
                    .Where(item => item.TenantNId == voice!.TenantNId && item.NId == voice.CallNId && !item.IsDeleted && item.OptimisticVersion == voiceExpectedVersion)
                    .ExecuteCommandAsync(cancellationToken);
                if (affected != 1)
                {
                    sugar.Ado.RollbackTran();
                    return null;
                }
                await sugar.Deleteable<RemoteAssistanceVoiceSlotTable>()
                    .Where(item => item.TenantNId == voice!.TenantNId && item.VoiceCallNId == voice.CallNId)
                    .ExecuteCommandAsync(cancellationToken);
                if (voiceEventPayload is not null)
                    await InsertRemoteAssistanceOutboxAsync(sugar, voice!.TenantNId, "collaboration.media.changed.v1", voiceEventPayload, voice.LastUpdatedOn, cancellationToken);
            }

            sugar.Ado.CommitTran();
            return new EndAllMediaRecords(
                currentScreen is not null && IsActiveScreen(currentScreen.State) ? screen : currentScreen,
                currentVoice is not null && IsActiveVoice(currentVoice.State) ? voice : currentVoice);
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task<IReadOnlyList<ScreenShareSessionRecord>> ListActiveScreensForUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<RemoteAssistanceScreenTable>()
            .Where(item => item.TenantNId == tenantNId && !item.IsDeleted && (item.InitiatorUserNId == userNId || item.InviteeUserNId == userNId)
                && (item.State == "Pending" || item.State == "Accepted" || item.State == "Connecting" || item.State == "Sharing"))
            .OrderBy(item => item.ConversationNId, OrderByType.Asc)
            .ToListAsync(cancellationToken);
        return rows.Select(item => ToScreenRecord(item)!).ToArray();
    }

    public async Task<IReadOnlyList<VoiceCallSessionRecord>> ListActiveVoicesForUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<RemoteAssistanceVoiceTable>()
            .Where(item => item.TenantNId == tenantNId && !item.IsDeleted && (item.CallerUserNId == userNId || item.CalleeUserNId == userNId)
                && (item.State == "Ringing" || item.State == "Accepted" || item.State == "Connecting" || item.State == "Active"))
            .OrderBy(item => item.ConversationNId, OrderByType.Asc)
            .ToListAsync(cancellationToken);
        return rows.Select(item => ToVoiceRecord(item)!).ToArray();
    }

    public async Task<IReadOnlyList<ScreenShareSessionRecord>> ListActiveScreensForLifecycleAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<RemoteAssistanceScreenTable>()
            .Where(item => !item.IsDeleted && (item.State == "Pending" || item.State == "Accepted" || item.State == "Connecting" || item.State == "Sharing"))
            .OrderBy(item => item.DeadlineOn, OrderByType.Asc)
            .ToListAsync(cancellationToken);
        return rows.Select(item => ToScreenRecord(item)!).ToArray();
    }

    public async Task<IReadOnlyList<VoiceCallSessionRecord>> ListActiveVoicesForLifecycleAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<RemoteAssistanceVoiceTable>()
            .Where(item => !item.IsDeleted && (item.State == "Ringing" || item.State == "Accepted" || item.State == "Connecting" || item.State == "Active"))
            .OrderBy(item => item.DeadlineOn, OrderByType.Asc)
            .ToListAsync(cancellationToken);
        return rows.Select(item => ToVoiceRecord(item)!).ToArray();
    }

    public Task UpdateVoiceSlotsExpiryAsync(string tenantNId, string callNId, DateTimeOffset expiresOn, CancellationToken cancellationToken)
        => _dbContext.SqlSugar.Updateable<RemoteAssistanceVoiceSlotTable>()
            .SetColumns(item => new RemoteAssistanceVoiceSlotTable { ExpiresOn = expiresOn })
            .Where(item => item.TenantNId == tenantNId && item.VoiceCallNId == callNId)
            .ExecuteCommandAsync(cancellationToken);

    public Task<int> DeleteExpiredVoiceSlotsAsync(DateTimeOffset now, CancellationToken cancellationToken)
        => _dbContext.SqlSugar.Deleteable<RemoteAssistanceVoiceSlotTable>()
            .Where(item => item.ExpiresOn <= now)
            .ExecuteCommandAsync(cancellationToken);

    private static RemoteAssistanceScreenTable ToScreenRow(ScreenShareSessionRecord item) => new()
    {
        Id = item.Id,
        TenantNId = item.TenantNId,
        NId = item.SessionNId,
        ConversationNId = item.ConversationNId,
        InitiatorUserNId = item.InitiatorUserNId,
        InviteeUserNId = item.InviteeUserNId,
        Direction = item.Direction,
        SharerUserNId = item.SharerUserNId,
        ViewerUserNId = item.ViewerUserNId,
        State = item.State,
        Answer = item.Answer,
        RequestNId = item.RequestNId,
        RequestHash = item.RequestHash,
        DeadlineOn = item.DeadlineOn,
        AcceptedOn = item.AcceptedOn,
        StartedOn = item.StartedOn,
        EndedOn = item.EndedOn,
        EndReason = item.EndReason,
        InitiatorConnectionId = item.InitiatorConnectionId,
        InviteeConnectionId = item.InviteeConnectionId,
        InitiatorAliveUntil = item.InitiatorAliveUntil,
        InviteeAliveUntil = item.InviteeAliveUntil,
        InitiatorStoppedReportedOn = item.InitiatorStoppedReportedOn,
        InviteeStoppedReportedOn = item.InviteeStoppedReportedOn,
        IsFrozen = false,
        IsLocked = false,
        IsDeleted = false,
        EntityType = "IndustrialPlatform.Collaboration.RemoteAssistance.ScreenShareSession",
        CreatedOn = item.CreatedOn,
        LastUpdatedOn = item.LastUpdatedOn,
        OptimisticVersion = item.Version,
        ConcurrencyVersion = item.ConcurrencyVersion == Guid.Empty ? Guid.NewGuid() : item.ConcurrencyVersion,
    };

    private static RemoteAssistanceVoiceTable ToVoiceRow(VoiceCallSessionRecord item) => new()
    {
        Id = item.Id,
        TenantNId = item.TenantNId,
        NId = item.CallNId,
        ConversationNId = item.ConversationNId,
        CallerUserNId = item.CallerUserNId,
        CalleeUserNId = item.CalleeUserNId,
        State = item.State,
        Answer = item.Answer,
        RequestNId = item.RequestNId,
        RequestHash = item.RequestHash,
        DeadlineOn = item.DeadlineOn,
        AcceptedOn = item.AcceptedOn,
        StartedOn = item.StartedOn,
        EndedOn = item.EndedOn,
        EndReason = item.EndReason,
        CallerConnectionId = item.CallerConnectionId,
        CalleeConnectionId = item.CalleeConnectionId,
        CallerAliveUntil = item.CallerAliveUntil,
        CalleeAliveUntil = item.CalleeAliveUntil,
        CallerReadyOn = item.CallerReadyOn,
        CalleeReadyOn = item.CalleeReadyOn,
        CallerStoppedReportedOn = item.CallerStoppedReportedOn,
        CalleeStoppedReportedOn = item.CalleeStoppedReportedOn,
        IsFrozen = false,
        IsLocked = false,
        IsDeleted = false,
        EntityType = "IndustrialPlatform.Collaboration.RemoteAssistance.VoiceCallSession",
        CreatedOn = item.CreatedOn,
        LastUpdatedOn = item.LastUpdatedOn,
        OptimisticVersion = item.Version,
        ConcurrencyVersion = item.ConcurrencyVersion == Guid.Empty ? Guid.NewGuid() : item.ConcurrencyVersion,
    };

    private static RemoteAssistanceVoiceSlotTable ToVoiceSlotRow(VoiceUserSlotRecord item) => new()
    {
        TenantNId = item.TenantNId,
        UserNId = item.UserNId,
        VoiceCallNId = item.VoiceCallNId,
        CreatedOn = item.CreatedOn,
        ExpiresOn = item.ExpiresOn,
    };

    private static Guid StableEventId(string eventNId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(eventNId));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes[..16]);
    }

    private ScreenShareSessionRecord? ToScreenRecord(RemoteAssistanceScreenTable? item) => item is null ? null : new()
    {
        Id = item.Id,
        TenantNId = item.TenantNId,
        SessionNId = item.NId,
        ConversationNId = item.ConversationNId,
        InitiatorUserNId = item.InitiatorUserNId,
        InviteeUserNId = item.InviteeUserNId,
        Direction = item.Direction,
        SharerUserNId = item.SharerUserNId,
        ViewerUserNId = item.ViewerUserNId,
        State = item.State,
        Answer = item.Answer,
        RequestNId = item.RequestNId,
        RequestHash = item.RequestHash,
        DeadlineOn = ReadUtcTimestamp(item.DeadlineOn),
        AcceptedOn = ReadUtcTimestamp(item.AcceptedOn),
        StartedOn = ReadUtcTimestamp(item.StartedOn),
        EndedOn = ReadUtcTimestamp(item.EndedOn),
        EndReason = item.EndReason,
        InitiatorConnectionId = item.InitiatorConnectionId,
        InviteeConnectionId = item.InviteeConnectionId,
        InitiatorAliveUntil = ReadUtcTimestamp(item.InitiatorAliveUntil),
        InviteeAliveUntil = ReadUtcTimestamp(item.InviteeAliveUntil),
        InitiatorStoppedReportedOn = ReadUtcTimestamp(item.InitiatorStoppedReportedOn),
        InviteeStoppedReportedOn = ReadUtcTimestamp(item.InviteeStoppedReportedOn),
        CreatedOn = ReadUtcTimestamp(item.CreatedOn),
        LastUpdatedOn = ReadUtcTimestamp(item.LastUpdatedOn),
        Version = item.OptimisticVersion,
        ConcurrencyVersion = item.ConcurrencyVersion,
    };

    private VoiceCallSessionRecord? ToVoiceRecord(RemoteAssistanceVoiceTable? item) => item is null ? null : new()
    {
        Id = item.Id,
        TenantNId = item.TenantNId,
        CallNId = item.NId,
        ConversationNId = item.ConversationNId,
        CallerUserNId = item.CallerUserNId,
        CalleeUserNId = item.CalleeUserNId,
        State = item.State,
        Answer = item.Answer,
        RequestNId = item.RequestNId,
        RequestHash = item.RequestHash,
        DeadlineOn = ReadUtcTimestamp(item.DeadlineOn),
        AcceptedOn = ReadUtcTimestamp(item.AcceptedOn),
        StartedOn = ReadUtcTimestamp(item.StartedOn),
        EndedOn = ReadUtcTimestamp(item.EndedOn),
        EndReason = item.EndReason,
        CallerConnectionId = item.CallerConnectionId,
        CalleeConnectionId = item.CalleeConnectionId,
        CallerAliveUntil = ReadUtcTimestamp(item.CallerAliveUntil),
        CalleeAliveUntil = ReadUtcTimestamp(item.CalleeAliveUntil),
        CallerReadyOn = ReadUtcTimestamp(item.CallerReadyOn),
        CalleeReadyOn = ReadUtcTimestamp(item.CalleeReadyOn),
        CallerStoppedReportedOn = ReadUtcTimestamp(item.CallerStoppedReportedOn),
        CalleeStoppedReportedOn = ReadUtcTimestamp(item.CalleeStoppedReportedOn),
        CreatedOn = ReadUtcTimestamp(item.CreatedOn),
        LastUpdatedOn = ReadUtcTimestamp(item.LastUpdatedOn),
        Version = item.OptimisticVersion,
        ConcurrencyVersion = item.ConcurrencyVersion,
    };

    private static async Task<int> InsertRemoteAssistanceOutboxAsync(ISqlSugarClient sugar, string tenantNId, string eventType, object payload, DateTimeOffset createdOn, CancellationToken cancellationToken)
    {
        var result = await sugar.Insertable(new CollaborationOutboxTable
        {
            EventId = Guid.NewGuid(),
            TenantNId = tenantNId,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
            CreatedOn = createdOn,
            RetryCount = 0,
        }).ExecuteCommandAsync(cancellationToken);
        if (payload is RemoteAssistanceEventPayload media)
        {
            var auditJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                eventNId = media.AuditEventNId,
                tenantNId,
                actorUserNId = media.ActorUserNId,
                actorKind = media.ActorKind,
                action = media.AuditAction,
                objectType = media.AuditObjectType,
                objectNId = media.AuditObjectNId,
                occurredOn = media.OccurredOn,
                payload = media.AuditPayload,
            });
            await sugar.Insertable(new CollaborationOutboxTable
            {
                EventId = StableEventId(media.AuditEventNId),
                TenantNId = tenantNId,
                EventType = "collaboration.audit.requested.v1",
                Payload = auditJson,
                CreatedOn = media.OccurredOn,
                RetryCount = 0,
            }).ExecuteCommandAsync(cancellationToken);
        }
        return result;
    }

    private static bool IsActiveScreen(string state) => state is "Pending" or "Accepted" or "Connecting" or "Sharing";
    private static bool IsActiveVoice(string state) => state is "Ringing" or "Accepted" or "Connecting" or "Active";
}
