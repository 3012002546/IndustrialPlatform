using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Collaboration.Domain.RemoteAssistance;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Collaboration.Application.RemoteAssistance;

public sealed class RemoteAssistanceException : Exception
{
    public RemoteAssistanceException(string code, string messageKey)
        : base(messageKey)
    {
        Code = code;
        MessageKey = messageKey;
    }

    public string Code { get; }
    public string MessageKey { get; }
}

public sealed class RemoteAssistanceService
{
    private readonly CollaborationService _collaboration;
    private readonly IRemoteAssistanceRepository _repository;
    private readonly ICollaborationIdentityDirectory _directory;
    private readonly IRemoteAssistancePermissionEvaluator _permissions;
    private readonly MediaContextRegistry _contexts;
    private readonly MediaContextCoordinator _coordinator;
    private readonly IRemoteAssistanceIceServerProvider _iceServers;
    private readonly RemoteAssistanceOptions _options;

    public RemoteAssistanceService(
        CollaborationService collaboration,
        IRemoteAssistanceRepository repository,
        ICollaborationIdentityDirectory directory,
        IRemoteAssistancePermissionEvaluator permissions,
        MediaContextRegistry contexts,
        MediaContextCoordinator coordinator,
        IRemoteAssistanceIceServerProvider iceServers,
        IOptions<RemoteAssistanceOptions> options)
    {
        _collaboration = collaboration;
        _repository = repository;
        _directory = directory;
        _permissions = permissions;
        _contexts = contexts;
        _coordinator = coordinator;
        _iceServers = iceServers;
        _options = options.Value;
    }

    public async Task<int> SweepLifecycleAsync(bool hostRestarted, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var ended = 0;
        foreach (var candidate in await _repository.ListActiveScreensForLifecycleAsync(cancellationToken))
        {
            using var contextLease = await _coordinator.AcquireAsync(candidate.TenantNId, candidate.ConversationNId, cancellationToken);
            var screen = await _repository.GetScreenAsync(candidate.TenantNId, candidate.SessionNId, cancellationToken);
            if (screen is null || RemoteAssistanceRules.IsTerminal(ParseScreenState(screen.State))) continue;
            var timedOut = screen.DeadlineOn <= now
                || screen.InitiatorAliveUntil is { } initiatorAlive && initiatorAlive <= now
                || screen.InviteeAliveUntil is { } inviteeAlive && inviteeAlive <= now;
            if (!hostRestarted && !timedOut) continue;
            var state = hostRestarted ? ScreenShareState.Failed : screen.State == ScreenShareState.Pending.ToString() ? ScreenShareState.Expired : ScreenShareState.Ended;
            var reason = hostRestarted
                ? "HostRestarted"
                : RemoteAssistanceRules.ScreenLifecycleReason(ParseScreenState(screen.State), screen.DeadlineOn <= now);
            try
            {
                var saved = await EndScreenRecordAsync(screen.TenantNId, screen, state.ToString(), reason, now, cancellationToken);
                RemoveScreenContext(saved.TenantNId, saved.ConversationNId, saved.SessionNId);
                ended++;
            }
            catch (RemoteAssistanceException exception) when (exception.Code == "MEDIA_CONFLICT") { }
        }
        foreach (var candidate in await _repository.ListActiveVoicesForLifecycleAsync(cancellationToken))
        {
            using var contextLease = await _coordinator.AcquireAsync(candidate.TenantNId, candidate.ConversationNId, cancellationToken);
            var voice = await _repository.GetVoiceAsync(candidate.TenantNId, candidate.CallNId, cancellationToken);
            if (voice is null || RemoteAssistanceRules.IsTerminal(ParseVoiceState(voice.State))) continue;
            var timedOut = voice.DeadlineOn <= now
                || voice.CallerAliveUntil is { } callerAlive && callerAlive <= now
                || voice.CalleeAliveUntil is { } calleeAlive && calleeAlive <= now;
            if (!hostRestarted && !timedOut) continue;
            var state = hostRestarted ? VoiceCallState.Failed : voice.State == VoiceCallState.Ringing.ToString() ? VoiceCallState.Missed : VoiceCallState.Ended;
            var reason = hostRestarted
                ? "HostRestarted"
                : RemoteAssistanceRules.VoiceLifecycleReason(ParseVoiceState(voice.State), voice.DeadlineOn <= now);
            try
            {
                var saved = await EndVoiceRecordAsync(voice, state.ToString(), reason, now, true, cancellationToken);
                RemoveVoiceContext(saved.TenantNId, saved.ConversationNId, saved.CallNId);
                ended++;
            }
            catch (RemoteAssistanceException exception) when (exception.Code == "MEDIA_CONFLICT") { }
        }
        await _repository.DeleteExpiredVoiceSlotsAsync(now, cancellationToken);
        return ended;
    }

    public async Task<ScreenDto> InviteScreenShareAsync(
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        DateTimeOffset tokenExpiresOn,
        string connectionId,
        InviteScreenShareRequest request,
        CancellationToken cancellationToken)
    {
        EnsureFeature(_options.ScreenEnabled, "MEDIA_DISABLED");
        var conversation = await RequireConversationAsync(tenantNId, actorUserNId, request.ConversationNId, cancellationToken);
        await RequirePermissionAsync(CollaborationPermissions.MessagingWrite, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, cancellationToken);
        var direction = ParseScreenDirection(request.Direction);
        var peerUserNId = conversation.PeerMember.UserNId;
        var sharer = direction == ScreenShareDirection.ShareMine ? actorUserNId : peerUserNId;
        var viewer = direction == ScreenShareDirection.ShareMine ? peerUserNId : actorUserNId;
        await RequirePermissionAsync(
            sharer == actorUserNId ? CollaborationPermissions.RemoteAssistanceSessionShare : CollaborationPermissions.RemoteAssistanceSessionJoin,
            tenantNId,
            actorUserNId,
            actorSessionNId,
            actorSecurityVersion,
            cancellationToken);
        await RequireActivePeerAsync(tenantNId, peerUserNId, cancellationToken);
        using var contextLease = await _coordinator.AcquireAsync(tenantNId, request.ConversationNId, cancellationToken);
        var requestNId = RequireNId(request.RequestNId, "MEDIA_INVALID_REQUEST");
        var requestHash = RemoteAssistanceRules.ComputeScreenRequestHash(request.ConversationNId, direction);
        var existing = await _repository.GetScreenByRequestAsync(tenantNId, actorUserNId, requestNId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.requestConflict");
            return ToScreenDto(existing);
        }
        if (await _repository.GetActiveScreenByConversationAsync(tenantNId, request.ConversationNId, cancellationToken) is not null)
            throw new RemoteAssistanceException("MEDIA_BUSY", "collaboration.media.busy");

        var now = DateTimeOffset.UtcNow;
        var session = new ScreenShareSessionRecord
        {
            Id = Guid.NewGuid(),
            TenantNId = tenantNId,
            SessionNId = NewNId("SCR"),
            ConversationNId = request.ConversationNId,
            InitiatorUserNId = actorUserNId,
            InviteeUserNId = peerUserNId,
            Direction = direction.ToString(),
            SharerUserNId = sharer,
            ViewerUserNId = viewer,
            State = ScreenShareState.Pending.ToString(),
            RequestNId = requestNId,
            RequestHash = requestHash,
            DeadlineOn = now.AddSeconds(Math.Max(1, _options.ScreenInvitationSeconds)),
            InitiatorConnectionId = RequireConnectionId(connectionId),
            CreatedOn = now,
            LastUpdatedOn = now,
            Version = 1,
            ConcurrencyVersion = Guid.NewGuid(),
        };
        var created = await _repository.CreateScreenAsync(session, EventPayload("Screen", "Invite", actorUserNId, session), cancellationToken);
        return ToScreenDto(created);
    }

    public async Task<ScreenDto> RespondScreenShareAsync(
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        DateTimeOffset tokenExpiresOn,
        string connectionId,
        RespondScreenShareRequest request,
        CancellationToken cancellationToken)
    {
        var current = await RequireScreenAsync(tenantNId, request.SessionNId, cancellationToken);
        if (!string.Equals(current.InviteeUserNId, actorUserNId, StringComparison.Ordinal))
            throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");
        await RequirePermissionAsync(
            current.SharerUserNId == actorUserNId ? CollaborationPermissions.RemoteAssistanceSessionShare : CollaborationPermissions.RemoteAssistanceSessionJoin,
            tenantNId,
            actorUserNId,
            actorSessionNId,
            actorSecurityVersion,
            cancellationToken);
        using var contextLease = await _coordinator.AcquireAsync(tenantNId, current.ConversationNId, cancellationToken);
        current = await RequireScreenAsync(tenantNId, request.SessionNId, cancellationToken);
        var answer = ParseAnswer(request.Answer);
        if (current.Answer is not null)
        {
            if (current.InviteeConnectionId is not null && !string.Equals(current.InviteeConnectionId, RequireConnectionId(connectionId), StringComparison.Ordinal))
                throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
            if (string.Equals(current.Answer, answer.ToString(), StringComparison.Ordinal))
                return ToScreenDto(current);
            throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.answerConflict");
        }
        EnsureExpectedVersion(current.Version, request.ExpectedVersion);
        var now = DateTimeOffset.UtcNow;
        if (current.State != ScreenShareState.Pending.ToString())
            throw new RemoteAssistanceException("MEDIA_ENDED", "collaboration.media.ended");
        if (current.DeadlineOn <= now || tokenExpiresOn <= now)
        {
            var expired = await EndScreenRecordAsync(tenantNId, current, ScreenShareState.Expired.ToString(), "InvitationTimeout", now, cancellationToken);
            return ToScreenDto(expired);
        }

        var updated = current with
        {
            Answer = answer.ToString(),
            State = answer == MediaAnswer.Accept ? ScreenShareState.Accepted.ToString() : ScreenShareState.Declined.ToString(),
            AcceptedOn = answer == MediaAnswer.Accept ? now : null,
            EndedOn = answer == MediaAnswer.Accept ? null : now,
            EndReason = answer == MediaAnswer.Accept ? null : "Declined",
            InviteeConnectionId = answer == MediaAnswer.Accept ? RequireConnectionId(connectionId) : current.InviteeConnectionId,
            InitiatorAliveUntil = answer == MediaAnswer.Accept ? AliveUntil(now, now.AddSeconds(_options.AliveSeconds), current.DeadlineOn) : current.InitiatorAliveUntil,
            InviteeAliveUntil = answer == MediaAnswer.Accept ? AliveUntil(now, now.AddSeconds(_options.AliveSeconds), current.DeadlineOn) : current.InviteeAliveUntil,
            DeadlineOn = answer == MediaAnswer.Accept ? now.AddSeconds(Math.Max(1, _options.SetupSeconds)) : current.DeadlineOn,
            LastUpdatedOn = now,
            Version = current.Version + 1,
            ConcurrencyVersion = Guid.NewGuid(),
        };
        var saved = await _repository.UpdateScreenAsync(updated, current.Version, EventPayload("Screen", answer == MediaAnswer.Accept ? "Accept" : "Decline", actorUserNId, updated), cancellationToken)
            ?? throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.concurrencyConflict");
        return ToScreenDto(saved);
    }

    public async Task<ScreenDto> EndScreenShareAsync(
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        string sessionNId,
        string reason,
        CancellationToken cancellationToken)
    {
        var current = await RequireScreenAsync(tenantNId, sessionNId, cancellationToken);
        EnsureParticipant(current.InitiatorUserNId, current.InviteeUserNId, actorUserNId);
        using var contextLease = await _coordinator.AcquireAsync(tenantNId, current.ConversationNId, cancellationToken);
        current = await RequireScreenAsync(tenantNId, sessionNId, cancellationToken);
        if (RemoteAssistanceRules.IsTerminal(ParseScreenState(current.State)))
        {
            RemoveScreenContext(tenantNId, current.ConversationNId, current.SessionNId);
            return ToScreenDto(current);
        }
        var now = DateTimeOffset.UtcNow;
        var ended = await EndScreenRecordAsync(tenantNId, current, ScreenShareState.Ended.ToString(), NormalizeEndReason(reason, "BrowserStopped"), now, cancellationToken);
        RemoveScreenContext(tenantNId, ended.ConversationNId, ended.SessionNId);
        return ToScreenDto(ended);
    }

    public async Task<VoiceDto> InviteVoiceCallAsync(
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        DateTimeOffset tokenExpiresOn,
        string connectionId,
        InviteVoiceCallRequest request,
        CancellationToken cancellationToken)
    {
        EnsureFeature(_options.VoiceEnabled, "MEDIA_DISABLED");
        var conversation = await RequireConversationAsync(tenantNId, actorUserNId, request.ConversationNId, cancellationToken);
        await RequirePermissionAsync(CollaborationPermissions.MessagingWrite, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, cancellationToken);
        await RequirePermissionAsync(CollaborationPermissions.RemoteAssistanceVoiceCall, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, cancellationToken);
        var peerUserNId = conversation.PeerMember.UserNId;
        await RequireActivePeerAsync(tenantNId, peerUserNId, cancellationToken);
        using var contextLease = await _coordinator.AcquireAsync(tenantNId, request.ConversationNId, cancellationToken);
        var requestNId = RequireNId(request.RequestNId, "MEDIA_INVALID_REQUEST");
        var requestHash = RemoteAssistanceRules.ComputeVoiceRequestHash(request.ConversationNId);
        var existing = await _repository.GetVoiceByRequestAsync(tenantNId, actorUserNId, requestNId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.requestConflict");
            return ToVoiceDto(existing);
        }
        if (await _repository.GetActiveVoiceByConversationAsync(tenantNId, request.ConversationNId, cancellationToken) is not null)
            throw new RemoteAssistanceException("MEDIA_BUSY", "collaboration.media.busy");

        var now = DateTimeOffset.UtcNow;
        var call = new VoiceCallSessionRecord
        {
            Id = Guid.NewGuid(),
            TenantNId = tenantNId,
            CallNId = NewNId("VOC"),
            ConversationNId = request.ConversationNId,
            CallerUserNId = actorUserNId,
            CalleeUserNId = peerUserNId,
            State = VoiceCallState.Ringing.ToString(),
            RequestNId = requestNId,
            RequestHash = requestHash,
            DeadlineOn = now.AddSeconds(Math.Max(1, _options.VoiceRingSeconds)),
            CallerConnectionId = RequireConnectionId(connectionId),
            CreatedOn = now,
            LastUpdatedOn = now,
            Version = 1,
            ConcurrencyVersion = Guid.NewGuid(),
        };
        var slots = new[]
        {
            new VoiceUserSlotRecord(tenantNId, actorUserNId, call.CallNId, now, call.DeadlineOn),
            new VoiceUserSlotRecord(tenantNId, peerUserNId, call.CallNId, now, call.DeadlineOn),
        };
        var created = await _repository.CreateVoiceAsync(call, slots, EventPayload("Voice", "Invite", actorUserNId, call), cancellationToken);
        return ToVoiceDto(created);
    }

    public async Task<VoiceDto> RespondVoiceCallAsync(
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        DateTimeOffset tokenExpiresOn,
        string connectionId,
        RespondVoiceCallRequest request,
        CancellationToken cancellationToken)
    {
        var current = await RequireVoiceAsync(tenantNId, request.CallNId, cancellationToken);
        if (current.CalleeUserNId != actorUserNId)
            throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");
        await RequirePermissionAsync(CollaborationPermissions.RemoteAssistanceVoiceCall, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, cancellationToken);
        using var contextLease = await _coordinator.AcquireAsync(tenantNId, current.ConversationNId, cancellationToken);
        current = await RequireVoiceAsync(tenantNId, request.CallNId, cancellationToken);
        var answer = ParseAnswer(request.Answer);
        if (current.Answer is not null)
        {
            if (current.CalleeConnectionId is not null && !string.Equals(current.CalleeConnectionId, RequireConnectionId(connectionId), StringComparison.Ordinal))
                throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
            if (current.Answer == answer.ToString()) return ToVoiceDto(current);
            throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.answerConflict");
        }
        EnsureExpectedVersion(current.Version, request.ExpectedVersion);
        var now = DateTimeOffset.UtcNow;
        if (current.State != VoiceCallState.Ringing.ToString())
            throw new RemoteAssistanceException("MEDIA_ENDED", "collaboration.media.ended");
        if (current.DeadlineOn <= now || tokenExpiresOn <= now)
        {
            var expired = await EndVoiceRecordAsync(current, VoiceCallState.Missed.ToString(), "RingTimeout", now, true, cancellationToken);
            return ToVoiceDto(expired);
        }
        var updated = current with
        {
            Answer = answer.ToString(),
            State = answer == MediaAnswer.Accept ? VoiceCallState.Accepted.ToString() : VoiceCallState.Declined.ToString(),
            AcceptedOn = answer == MediaAnswer.Accept ? now : null,
            EndedOn = answer == MediaAnswer.Accept ? null : now,
            EndReason = answer == MediaAnswer.Accept ? null : "Declined",
            CalleeConnectionId = answer == MediaAnswer.Accept ? RequireConnectionId(connectionId) : current.CalleeConnectionId,
            CallerAliveUntil = answer == MediaAnswer.Accept ? AliveUntil(now, now.AddSeconds(_options.AliveSeconds), current.DeadlineOn) : current.CallerAliveUntil,
            CalleeAliveUntil = answer == MediaAnswer.Accept ? AliveUntil(now, now.AddSeconds(_options.AliveSeconds), current.DeadlineOn) : current.CalleeAliveUntil,
            DeadlineOn = answer == MediaAnswer.Accept ? now.AddSeconds(Math.Max(1, _options.SetupSeconds)) : current.DeadlineOn,
            LastUpdatedOn = now,
            Version = current.Version + 1,
            ConcurrencyVersion = Guid.NewGuid(),
        };
        var saved = await _repository.UpdateVoiceAsync(updated, current.Version, answer != MediaAnswer.Accept, EventPayload("Voice", answer == MediaAnswer.Accept ? "Accept" : "Decline", actorUserNId, updated), cancellationToken)
            ?? throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.concurrencyConflict");
        if (answer == MediaAnswer.Accept)
            await _repository.UpdateVoiceSlotsExpiryAsync(tenantNId, saved.CallNId, saved.DeadlineOn, cancellationToken);
        return ToVoiceDto(saved);
    }

    public async Task<VoiceDto> EndVoiceCallAsync(
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        string callNId,
        string reason,
        CancellationToken cancellationToken)
    {
        var current = await RequireVoiceAsync(tenantNId, callNId, cancellationToken);
        EnsureParticipant(current.CallerUserNId, current.CalleeUserNId, actorUserNId);
        using var contextLease = await _coordinator.AcquireAsync(tenantNId, current.ConversationNId, cancellationToken);
        current = await RequireVoiceAsync(tenantNId, callNId, cancellationToken);
        if (RemoteAssistanceRules.IsTerminal(ParseVoiceState(current.State)))
        {
            RemoveVoiceContext(tenantNId, current.ConversationNId, current.CallNId);
            return ToVoiceDto(current);
        }
        var ended = await EndVoiceRecordAsync(current, VoiceCallState.Ended.ToString(), NormalizeEndReason(reason, "HungUp"), DateTimeOffset.UtcNow, true, cancellationToken);
        RemoveVoiceContext(tenantNId, ended.ConversationNId, ended.CallNId);
        return ToVoiceDto(ended);
    }

    public async Task<ConversationMediaDto> GetConversationMediaAsync(string tenantNId, string actorUserNId, GetConversationMediaRequest request, CancellationToken cancellationToken)
    {
        var conversation = await RequireConversationAsync(tenantNId, actorUserNId, request.ConversationNId, cancellationToken);
        var screen = await _repository.GetActiveScreenByConversationAsync(tenantNId, request.ConversationNId, cancellationToken)
            ?? await _repository.GetLatestScreenAsync(tenantNId, request.ConversationNId, cancellationToken);
        var voice = await _repository.GetActiveVoiceByConversationAsync(tenantNId, request.ConversationNId, cancellationToken)
            ?? await _repository.GetLatestVoiceAsync(tenantNId, request.ConversationNId, cancellationToken);
        var context = _contexts.Find(tenantNId, request.ConversationNId);
        var selected = context is not null && ((context.LowUserNId == actorUserNId && context.Low?.ConnectionId is not null) || (context.HighUserNId == actorUserNId && context.High?.ConnectionId is not null));
        return new ConversationMediaDto { Screen = screen is null ? null : ToScreenDto(screen), Voice = voice is null ? null : ToVoiceDto(voice), MyEndpointSelected = selected, MediaContextNId = context?.MediaContextNId };
    }

    public async Task<ActiveMediaPageDto> GetMyActiveMediaAsync(string tenantNId, string actorUserNId, GetMyActiveMediaRequest request, CancellationToken cancellationToken)
    {
        var screens = await _repository.ListActiveScreensForUserAsync(tenantNId, actorUserNId, cancellationToken);
        var voices = await _repository.ListActiveVoicesForUserAsync(tenantNId, actorUserNId, cancellationToken);
        var conversations = screens.Select(item => item.ConversationNId).Concat(voices.Select(item => item.ConversationNId)).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var offset = int.TryParse(request.Cursor, out var parsed) ? Math.Max(parsed, 0) : 0;
        var items = conversations.Skip(offset).Take(20).Select(conversationNId =>
        {
            var screen = screens.LastOrDefault(item => item.ConversationNId == conversationNId);
            var voice = voices.LastOrDefault(item => item.ConversationNId == conversationNId);
            var context = _contexts.Find(tenantNId, conversationNId);
            MediaEndpointSnapshot? endpoint = context is null ? null : (context.LowUserNId == actorUserNId ? context.Low : context.High);
            return new ActiveMediaItemDto { ConversationNId = conversationNId, Screen = screen is null ? null : ToScreenDto(screen), Voice = voice is null ? null : ToVoiceDto(voice), MyEndpointSelected = endpoint is not null, MediaContextNId = context?.MediaContextNId };
        }).ToArray();
        return new ActiveMediaPageDto { Items = items, NextCursor = offset + items.Length < conversations.Length ? (offset + items.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) : null };
    }

    public async Task<ScreenDto?> GetScreenForRealtimeAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken)
    {
        var item = await _repository.GetScreenAsync(tenantNId, sessionNId, cancellationToken);
        return item is null ? null : ToScreenDto(item);
    }

    public async Task<VoiceDto?> GetVoiceForRealtimeAsync(string tenantNId, string callNId, CancellationToken cancellationToken)
    {
        var item = await _repository.GetVoiceAsync(tenantNId, callNId, cancellationToken);
        return item is null ? null : ToVoiceDto(item);
    }

    public async Task<MediaBindingDto> BindMediaAsync(
        string tenantNId,
        string actorUserNId,
        string actorSessionNId,
        string actorSecurityVersion,
        DateTimeOffset tokenExpiresOn,
        string connectionId,
        BindMediaRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ScreenSessionNId is null && request.VoiceCallNId is null)
            throw new RemoteAssistanceException("MEDIA_INVALID_REQUEST", "collaboration.media.capabilityRequired");
        var conversation = await RequireConversationAsync(tenantNId, actorUserNId, request.ConversationNId, cancellationToken);
        var screen = request.ScreenSessionNId is null ? null : await RequireScreenAsync(tenantNId, request.ScreenSessionNId, cancellationToken);
        var voice = request.VoiceCallNId is null ? null : await RequireVoiceAsync(tenantNId, request.VoiceCallNId, cancellationToken);
        if (screen is not null && screen.ConversationNId != request.ConversationNId || voice is not null && voice.ConversationNId != request.ConversationNId)
            throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");
        if (screen is not null)
        {
            EnsureScreenBindable(screen, actorUserNId);
            EnsureScreenConnection(screen, actorUserNId, connectionId);
            await RequirePermissionAsync(actorUserNId == screen.SharerUserNId ? CollaborationPermissions.RemoteAssistanceSessionShare : CollaborationPermissions.RemoteAssistanceSessionJoin, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, cancellationToken);
        }
        if (voice is not null)
        {
            EnsureVoiceBindable(voice, actorUserNId);
            EnsureVoiceConnection(voice, actorUserNId, connectionId);
            await RequirePermissionAsync(CollaborationPermissions.RemoteAssistanceVoiceCall, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, cancellationToken);
        }
        using var contextLease = await _coordinator.AcquireAsync(tenantNId, request.ConversationNId, cancellationToken);
        screen = request.ScreenSessionNId is null ? null : await RequireScreenAsync(tenantNId, request.ScreenSessionNId, cancellationToken);
        voice = request.VoiceCallNId is null ? null : await RequireVoiceAsync(tenantNId, request.VoiceCallNId, cancellationToken);
        if (screen is not null && screen.ConversationNId != request.ConversationNId || voice is not null && voice.ConversationNId != request.ConversationNId)
            throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");
        if (screen is not null)
        {
            EnsureScreenBindable(screen, actorUserNId);
            EnsureScreenConnection(screen, actorUserNId, connectionId);
        }
        if (voice is not null)
        {
            EnsureVoiceBindable(voice, actorUserNId);
            EnsureVoiceConnection(voice, actorUserNId, connectionId);
        }
        var icePolicy = _options.GetNormalizedIcePolicy();
        var iceServers = await _iceServers.GetAsync(tenantNId, actorUserNId, cancellationToken);
        var context = _contexts.Bind(tenantNId, request.ConversationNId, actorUserNId, RequireConnectionId(connectionId), actorSessionNId, ParseAuthVersion(actorSecurityVersion), tokenExpiresOn, screen?.SessionNId, voice?.CallNId, conversation.PeerMember.UserNId);
        if (screen is not null && context.Context.Low?.ScreenSessionNId == screen.SessionNId && context.Context.High?.ScreenSessionNId == screen.SessionNId)
            screen = await MoveScreenToConnectingAsync(screen, cancellationToken);
        if (voice is not null && context.Context.Low?.VoiceCallNId == voice.CallNId && context.Context.High?.VoiceCallNId == voice.CallNId)
            voice = await MoveVoiceToConnectingAsync(voice, cancellationToken);
        var confirm = new MediaConfirmationPairDto
        {
            Screen = screen is null ? null : Confirmation(screen.SessionNId, screen.State, screen.Version, IsScreenAllowed(screen)),
            Voice = voice is null ? null : Confirmation(voice.CallNId, voice.State, voice.Version, IsVoiceAllowed(voice)),
        };
        return new MediaBindingDto
        {
            MediaContextNId = context.Context.MediaContextNId,
            ContextRevision = context.Context.Revision,
            EndpointRole = context.EndpointRole,
            Polite = context.Polite,
            Screen = screen is null ? null : ToScreenDto(screen),
            Voice = voice is null ? null : ToVoiceDto(voice),
            IcePolicy = icePolicy,
            IceServers = iceServers,
            Confirm = confirm,
        };
    }

    public async Task<MediaSignalResultDto> SignalMediaAsync(string tenantNId, string actorUserNId, string connectionId, SignalMediaRequest request, CancellationToken cancellationToken)
    {
        ValidateSignal(request);
        var context = _contexts.FindById(request.MediaContextNId) ?? throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");
        var endpoint = context.LowUserNId == actorUserNId ? context.Low : context.HighUserNId == actorUserNId ? context.High : null;
        if (endpoint is null || endpoint.ConnectionId != RequireConnectionId(connectionId))
            throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
        if (endpoint.TokenExpiresOn <= DateTimeOffset.UtcNow)
            throw new RemoteAssistanceException("MEDIA_EXPIRED", "collaboration.media.expired");
        await EnsureContextSignalsAllowedAsync(tenantNId, context, endpoint, cancellationToken);
        using var contextLease = await _coordinator.AcquireAsync(tenantNId, context.ConversationNId, cancellationToken);
        var accepted = _contexts.AcceptSignal(request.MediaContextNId, tenantNId, context.ConversationNId, actorUserNId, RequireConnectionId(connectionId), request.ContextRevision, request.Sequence);
        return new MediaSignalResultDto { Status = "Accepted", TargetConnectionId = accepted.TargetConnectionId };
    }

    public async Task<object> MediaReadyAsync(string tenantNId, string actorUserNId, string connectionId, MediaReadyRequest request, CancellationToken cancellationToken)
    {
        var context = _contexts.FindById(request.MediaContextNId) ?? throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");
        var endpoint = context.LowUserNId == actorUserNId ? context.Low : context.HighUserNId == actorUserNId ? context.High : null;
        if (endpoint is null || endpoint.ConnectionId != connectionId)
            throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
        if (endpoint.TokenExpiresOn <= DateTimeOffset.UtcNow)
            throw new RemoteAssistanceException("MEDIA_EXPIRED", "collaboration.media.expired");
        using var contextLease = await _coordinator.AcquireAsync(tenantNId, context.ConversationNId, cancellationToken);
        var kind = request.Kind.Trim();
        if (kind.Equals("Screen", StringComparison.OrdinalIgnoreCase))
        {
            var screen = await RequireScreenAsync(tenantNId, request.SessionNId, cancellationToken);
            if (endpoint.ScreenSessionNId != screen.SessionNId)
                throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
            if (screen.ViewerUserNId != actorUserNId) throw new RemoteAssistanceException("MEDIA_FORBIDDEN", "collaboration.media.forbidden");
            await RequirePermissionAsync(CollaborationPermissions.RemoteAssistanceSessionJoin, tenantNId, actorUserNId, endpoint.SessionNId, endpoint.AuthVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
            if (!IsScreenAllowed(screen)) throw new RemoteAssistanceException("MEDIA_ENDED", "collaboration.media.ended");
            if (screen.StartedOn is not null) return ToScreenDto(screen);
            var now = DateTimeOffset.UtcNow;
            var updated = screen with { State = ScreenShareState.Sharing.ToString(), StartedOn = now, DeadlineOn = now.AddMinutes(Math.Max(1, _options.MaxDurationMinutes)), LastUpdatedOn = now, Version = screen.Version + 1, ConcurrencyVersion = Guid.NewGuid() };
            var saved = await _repository.UpdateScreenAsync(updated, screen.Version, EventPayload("Screen", "Ready", actorUserNId, updated), cancellationToken) ?? throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.concurrencyConflict");
            return ToScreenDto(saved);
        }
        if (kind.Equals("Voice", StringComparison.OrdinalIgnoreCase))
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var voice = await RequireVoiceAsync(tenantNId, request.SessionNId, cancellationToken);
                if (endpoint.VoiceCallNId != voice.CallNId)
                    throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
                EnsureParticipant(voice.CallerUserNId, voice.CalleeUserNId, actorUserNId);
                await RequirePermissionAsync(CollaborationPermissions.RemoteAssistanceVoiceCall, tenantNId, actorUserNId, endpoint.SessionNId, endpoint.AuthVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
                if (!IsVoiceAllowed(voice)) throw new RemoteAssistanceException("MEDIA_ENDED", "collaboration.media.ended");
                var now = DateTimeOffset.UtcNow;
                var updated = actorUserNId == voice.CallerUserNId
                    ? voice with { CallerReadyOn = voice.CallerReadyOn ?? now }
                    : voice with { CalleeReadyOn = voice.CalleeReadyOn ?? now };
                if (updated.CallerReadyOn is not null && updated.CalleeReadyOn is not null)
                    updated = updated with { State = VoiceCallState.Active.ToString(), StartedOn = updated.StartedOn ?? now, DeadlineOn = (updated.StartedOn ?? now).AddMinutes(Math.Max(1, _options.MaxDurationMinutes)) };
                else if (updated.State == VoiceCallState.Accepted.ToString())
                    updated = updated with { State = VoiceCallState.Connecting.ToString() };
                if (updated == voice) return ToVoiceDto(voice);
                updated = updated with { LastUpdatedOn = now, Version = voice.Version + 1, ConcurrencyVersion = Guid.NewGuid() };
                var saved = await _repository.UpdateVoiceAsync(updated, voice.Version, false, EventPayload("Voice", "Ready", actorUserNId, updated), cancellationToken);
                if (saved is not null)
                {
                    await _repository.UpdateVoiceSlotsExpiryAsync(tenantNId, saved.CallNId, saved.DeadlineOn, cancellationToken);
                    return ToVoiceDto(saved);
                }
            }
            throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.concurrencyConflict");
        }
        throw new RemoteAssistanceException("MEDIA_INVALID_REQUEST", "collaboration.media.kindInvalid");
    }

    public async Task<KeepAliveMediaDto> KeepAliveMediaAsync(string tenantNId, string actorUserNId, string connectionId, KeepAliveMediaRequest request, CancellationToken cancellationToken)
    {
        var context = _contexts.FindById(request.MediaContextNId) ?? throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");
        var endpoint = context.LowUserNId == actorUserNId ? context.Low : context.HighUserNId == actorUserNId ? context.High : null;
        if (endpoint is null || endpoint.ConnectionId != connectionId)
            throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
        if (request.ScreenSessionNId is null && request.VoiceCallNId is null)
            throw new RemoteAssistanceException("MEDIA_INVALID_REQUEST", "collaboration.media.capabilityRequired");
        if (endpoint.TokenExpiresOn <= DateTimeOffset.UtcNow)
            throw new RemoteAssistanceException("MEDIA_EXPIRED", "collaboration.media.expired");
        using var contextLease = await _coordinator.AcquireAsync(tenantNId, context.ConversationNId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var result = new KeepAliveMediaDto { ContextRevision = context.Revision };
        if (request.ScreenSessionNId is not null)
        {
            try
            {
                if (endpoint.ScreenSessionNId != request.ScreenSessionNId)
                    throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
                var screen = await RequireScreenAsync(tenantNId, request.ScreenSessionNId, cancellationToken);
                await RequirePermissionAsync(actorUserNId == screen.SharerUserNId ? CollaborationPermissions.RemoteAssistanceSessionShare : CollaborationPermissions.RemoteAssistanceSessionJoin, tenantNId, actorUserNId, endpoint.SessionNId, endpoint.AuthVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
                result = result with { Screen = await KeepScreenAliveAsync(screen, actorUserNId, now, cancellationToken) };
            }
            catch (RemoteAssistanceException exception)
            {
                result = result with { Screen = FailureConfirmation(request.ScreenSessionNId, exception.Code) };
            }
        }
        if (request.VoiceCallNId is not null)
        {
            try
            {
                if (endpoint.VoiceCallNId != request.VoiceCallNId)
                    throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
                var voice = await RequireVoiceAsync(tenantNId, request.VoiceCallNId, cancellationToken);
                await RequirePermissionAsync(CollaborationPermissions.RemoteAssistanceVoiceCall, tenantNId, actorUserNId, endpoint.SessionNId, endpoint.AuthVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
                result = result with { Voice = await KeepVoiceAliveAsync(voice, actorUserNId, now, cancellationToken) };
            }
            catch (RemoteAssistanceException exception)
            {
                result = result with { Voice = FailureConfirmation(request.VoiceCallNId, exception.Code) };
            }
        }
        return result;
    }

    public async Task<VoiceMutedDto> SetVoiceMutedAsync(string tenantNId, string actorUserNId, string connectionId, SetVoiceMutedRequest request, CancellationToken cancellationToken)
    {
        var voice = await RequireVoiceAsync(tenantNId, request.CallNId, cancellationToken);
        EnsureParticipant(voice.CallerUserNId, voice.CalleeUserNId, actorUserNId);
        var context = _contexts.Find(tenantNId, voice.ConversationNId) ?? throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");
        var endpoint = context.LowUserNId == actorUserNId ? context.Low : context.High;
        if (endpoint is null || endpoint.ConnectionId != connectionId || endpoint.VoiceCallNId != voice.CallNId)
            throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
        if (request.Sequence < 1)
            throw new RemoteAssistanceException("MEDIA_INVALID_REQUEST", "collaboration.media.invalidRequest");
        if (endpoint.TokenExpiresOn <= DateTimeOffset.UtcNow)
            throw new RemoteAssistanceException("MEDIA_EXPIRED", "collaboration.media.expired");
        if (!IsVoiceAllowed(voice))
            throw new RemoteAssistanceException("MEDIA_ENDED", "collaboration.media.ended");
        await RequirePermissionAsync(CollaborationPermissions.RemoteAssistanceVoiceCall, tenantNId, actorUserNId, endpoint.SessionNId, endpoint.AuthVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        using var contextLease = await _coordinator.AcquireAsync(tenantNId, voice.ConversationNId, cancellationToken);
        var target = _contexts.GetPeerConnectionId(context.MediaContextNId, actorUserNId);
        return new VoiceMutedDto { CallNId = voice.CallNId, UserNId = actorUserNId, Muted = request.Muted, Sequence = request.Sequence, TargetConnectionId = target };
    }

    public async Task<object> ReportMediaStoppedAsync(string tenantNId, string actorUserNId, ReportMediaStoppedRequest request, CancellationToken cancellationToken)
    {
        if (request.Kind.Equals("Screen", StringComparison.OrdinalIgnoreCase))
        {
            var screen = await RequireScreenAsync(tenantNId, request.SessionNId, cancellationToken);
            EnsureParticipant(screen.InitiatorUserNId, screen.InviteeUserNId, actorUserNId);
            using var contextLease = await _coordinator.AcquireAsync(tenantNId, screen.ConversationNId, cancellationToken);
            screen = await RequireScreenAsync(tenantNId, request.SessionNId, cancellationToken);
            var valid = actorUserNId == screen.SharerUserNId ? request.SenderDetached && request.CaptureTracksEnded : request.PlaybackDetached;
            if (!valid || !RemoteAssistanceRules.IsTerminal(ParseScreenState(screen.State))) return ToScreenDto(screen);
            var now = DateTimeOffset.UtcNow;
            var updated = actorUserNId == screen.InitiatorUserNId
                ? screen with { InitiatorStoppedReportedOn = screen.InitiatorStoppedReportedOn ?? now }
                : screen with { InviteeStoppedReportedOn = screen.InviteeStoppedReportedOn ?? now };
            if (updated == screen) return ToScreenDto(screen);
            updated = updated with { LastUpdatedOn = now, ConcurrencyVersion = Guid.NewGuid() };
            return ToScreenDto(await _repository.UpdateScreenAsync(updated, screen.Version, null, cancellationToken) ?? screen);
        }
        if (request.Kind.Equals("Voice", StringComparison.OrdinalIgnoreCase))
        {
            var voice = await RequireVoiceAsync(tenantNId, request.SessionNId, cancellationToken);
            EnsureParticipant(voice.CallerUserNId, voice.CalleeUserNId, actorUserNId);
            using var contextLease = await _coordinator.AcquireAsync(tenantNId, voice.ConversationNId, cancellationToken);
            voice = await RequireVoiceAsync(tenantNId, request.SessionNId, cancellationToken);
            if (!request.SenderDetached || !request.CaptureTracksEnded || !request.PlaybackDetached || !RemoteAssistanceRules.IsTerminal(ParseVoiceState(voice.State))) return ToVoiceDto(voice);
            var now = DateTimeOffset.UtcNow;
            var updated = actorUserNId == voice.CallerUserNId
                ? voice with { CallerStoppedReportedOn = voice.CallerStoppedReportedOn ?? now }
                : voice with { CalleeStoppedReportedOn = voice.CalleeStoppedReportedOn ?? now };
            if (updated == voice) return ToVoiceDto(voice);
            updated = updated with { LastUpdatedOn = now, ConcurrencyVersion = Guid.NewGuid() };
            return ToVoiceDto(await _repository.UpdateVoiceAsync(updated, voice.Version, false, null, cancellationToken) ?? voice);
        }
        throw new RemoteAssistanceException("MEDIA_INVALID_REQUEST", "collaboration.media.kindInvalid");
    }

    public async Task<EndAllMediaDto> EndAllMediaAsync(string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, EndAllMediaRequest request, CancellationToken cancellationToken)
    {
        var context = _contexts.FindById(request.MediaContextNId) ?? throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");
        if (context.LowUserNId != actorUserNId && context.HighUserNId != actorUserNId)
            throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");
        if (request.ScreenSessionNId is not null && context.ScreenSessionNId != request.ScreenSessionNId)
            throw new RemoteAssistanceException("MEDIA_CONTEXT_STALE", "collaboration.media.contextError");
        if (request.VoiceCallNId is not null && context.VoiceCallNId != request.VoiceCallNId)
            throw new RemoteAssistanceException("MEDIA_CONTEXT_STALE", "collaboration.media.contextError");
        using var contextLease = await _coordinator.AcquireAsync(tenantNId, context.ConversationNId, cancellationToken);
        ScreenShareSessionRecord? currentScreen = request.ScreenSessionNId is null ? null : await RequireScreenAsync(tenantNId, request.ScreenSessionNId, cancellationToken);
        VoiceCallSessionRecord? currentVoice = request.VoiceCallNId is null ? null : await RequireVoiceAsync(tenantNId, request.VoiceCallNId, cancellationToken);
        if (currentScreen is not null) EnsureParticipant(currentScreen.InitiatorUserNId, currentScreen.InviteeUserNId, actorUserNId);
        if (currentVoice is not null) EnsureParticipant(currentVoice.CallerUserNId, currentVoice.CalleeUserNId, actorUserNId);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var screenActive = currentScreen is not null && !RemoteAssistanceRules.IsTerminal(ParseScreenState(currentScreen.State));
            var voiceActive = currentVoice is not null && !RemoteAssistanceRules.IsTerminal(ParseVoiceState(currentVoice.State));
            var screen = screenActive
                ? currentScreen! with { State = ScreenShareState.Ended.ToString(), EndedOn = currentScreen!.EndedOn ?? now, EndReason = currentScreen.EndReason ?? "EndedAll", LastUpdatedOn = now, Version = currentScreen.Version + 1, ConcurrencyVersion = Guid.NewGuid() }
                : currentScreen;
            var voice = voiceActive
                ? currentVoice! with { State = VoiceCallState.Ended.ToString(), EndedOn = currentVoice!.EndedOn ?? now, EndReason = currentVoice.EndReason ?? "EndedAll", LastUpdatedOn = now, Version = currentVoice.Version + 1, ConcurrencyVersion = Guid.NewGuid() }
                : currentVoice;
            var saved = await _repository.EndAllAsync(
                screen,
                currentScreen?.Version,
                screenActive ? EventPayload("Screen", "End", null, screen!) : null,
                voice,
                currentVoice?.Version,
                voiceActive ? EventPayload("Voice", "End", null, voice!) : null,
                cancellationToken);
            if (saved is not null)
            {
                if (saved.Screen is not null) RemoveScreenContext(saved.Screen.TenantNId, saved.Screen.ConversationNId, saved.Screen.SessionNId);
                if (saved.Voice is not null) RemoveVoiceContext(saved.Voice.TenantNId, saved.Voice.ConversationNId, saved.Voice.CallNId);
                return new EndAllMediaDto
                {
                    Screen = saved.Screen is null ? null : ToScreenDto(saved.Screen),
                    Voice = saved.Voice is null ? null : ToVoiceDto(saved.Voice),
                };
            }
            currentScreen = request.ScreenSessionNId is null ? null : await RequireScreenAsync(tenantNId, request.ScreenSessionNId, cancellationToken);
            currentVoice = request.VoiceCallNId is null ? null : await RequireVoiceAsync(tenantNId, request.VoiceCallNId, cancellationToken);
        }
        throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.concurrencyConflict");
    }

    /// <summary>
    /// Ends only media bound to the disconnected SignalR page. A user may have
    /// several browser pages, so a disconnect must never tear down another
    /// page's accepted media session.
    /// </summary>
    public async Task EndMediaForConnectionAsync(
        string tenantNId,
        string actorUserNId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var requiredConnectionId = RequireConnectionId(connectionId);
        foreach (var candidate in await _repository.ListActiveScreensForUserAsync(tenantNId, actorUserNId, cancellationToken))
        {
            if (!IsConnectionParticipant(candidate, requiredConnectionId)) continue;
            using var contextLease = await _coordinator.AcquireAsync(tenantNId, candidate.ConversationNId, cancellationToken);
            var current = await _repository.GetScreenAsync(tenantNId, candidate.SessionNId, cancellationToken);
            if (current is null || !IsConnectionParticipant(current, requiredConnectionId)
                || RemoteAssistanceRules.IsTerminal(ParseScreenState(current.State))) continue;
            try
            {
                var saved = await EndScreenRecordAsync(tenantNId, current, ScreenShareState.Ended.ToString(), "ConnectionClosed", DateTimeOffset.UtcNow, cancellationToken);
                RemoveScreenContext(saved.TenantNId, saved.ConversationNId, saved.SessionNId);
            }
            catch (RemoteAssistanceException exception) when (exception.Code == "MEDIA_CONFLICT") { }
        }

        foreach (var candidate in await _repository.ListActiveVoicesForUserAsync(tenantNId, actorUserNId, cancellationToken))
        {
            if (!IsConnectionParticipant(candidate, requiredConnectionId)) continue;
            using var contextLease = await _coordinator.AcquireAsync(tenantNId, candidate.ConversationNId, cancellationToken);
            var current = await _repository.GetVoiceAsync(tenantNId, candidate.CallNId, cancellationToken);
            if (current is null || !IsConnectionParticipant(current, requiredConnectionId)
                || RemoteAssistanceRules.IsTerminal(ParseVoiceState(current.State))) continue;
            try
            {
                var saved = await EndVoiceRecordAsync(current, VoiceCallState.Ended.ToString(), "ConnectionClosed", DateTimeOffset.UtcNow, true, cancellationToken);
                RemoveVoiceContext(saved.TenantNId, saved.ConversationNId, saved.CallNId);
            }
            catch (RemoteAssistanceException exception) when (exception.Code == "MEDIA_CONFLICT") { }
        }
    }

    private async Task<ScreenShareSessionRecord> EndScreenRecordAsync(string tenantNId, ScreenShareSessionRecord current, string state, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (RemoteAssistanceRules.IsTerminal(ParseScreenState(current.State))) return current;
            var updated = current with
            {
                State = state,
                EndedOn = current.EndedOn ?? now,
                EndReason = current.EndReason ?? reason,
                LastUpdatedOn = now,
                Version = current.Version + 1,
                ConcurrencyVersion = Guid.NewGuid(),
            };
            var saved = await _repository.UpdateScreenAsync(updated, current.Version, EventPayload("Screen", "End", null, updated), cancellationToken);
            if (saved is not null) return saved;
            current = await RequireScreenAsync(tenantNId, current.SessionNId, cancellationToken);
        }
        throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.concurrencyConflict");
    }

    private async Task<VoiceCallSessionRecord> EndVoiceRecordAsync(VoiceCallSessionRecord current, string state, string reason, DateTimeOffset now, bool releaseSlots, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (RemoteAssistanceRules.IsTerminal(ParseVoiceState(current.State))) return current;
            var updated = current with
            {
                State = state,
                EndedOn = current.EndedOn ?? now,
                EndReason = current.EndReason ?? reason,
                LastUpdatedOn = now,
                Version = current.Version + 1,
                ConcurrencyVersion = Guid.NewGuid(),
            };
            var saved = await _repository.UpdateVoiceAsync(updated, current.Version, releaseSlots, EventPayload("Voice", "End", null, updated), cancellationToken);
            if (saved is not null) return saved;
            current = await RequireVoiceAsync(current.TenantNId, current.CallNId, cancellationToken);
        }
        throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.concurrencyConflict");
    }

    private async Task<ScreenShareSessionRecord> MoveScreenToConnectingAsync(ScreenShareSessionRecord screen, CancellationToken cancellationToken)
    {
        if (screen.State != ScreenShareState.Accepted.ToString()) return screen;
        var updated = screen with { State = ScreenShareState.Connecting.ToString(), LastUpdatedOn = DateTimeOffset.UtcNow, Version = screen.Version + 1, ConcurrencyVersion = Guid.NewGuid() };
        return await _repository.UpdateScreenAsync(updated, screen.Version, EventPayload("Screen", "Connecting", null, updated), cancellationToken) ?? screen;
    }

    private async Task<VoiceCallSessionRecord> MoveVoiceToConnectingAsync(VoiceCallSessionRecord voice, CancellationToken cancellationToken)
    {
        if (voice.State != VoiceCallState.Accepted.ToString()) return voice;
        var updated = voice with { State = VoiceCallState.Connecting.ToString(), LastUpdatedOn = DateTimeOffset.UtcNow, Version = voice.Version + 1, ConcurrencyVersion = Guid.NewGuid() };
        return await _repository.UpdateVoiceAsync(updated, voice.Version, false, EventPayload("Voice", "Connecting", null, updated), cancellationToken) ?? voice;
    }

    private void RemoveScreenContext(string tenantNId, string conversationNId, string sessionNId)
    {
        var context = _contexts.Find(tenantNId, conversationNId);
        if (context?.ScreenSessionNId == sessionNId)
            _contexts.UpdateCapability(context.MediaContextNId, tenantNId, conversationNId, null, null, true, false);
    }

    private void RemoveVoiceContext(string tenantNId, string conversationNId, string callNId)
    {
        var context = _contexts.Find(tenantNId, conversationNId);
        if (context?.VoiceCallNId == callNId)
            _contexts.UpdateCapability(context.MediaContextNId, tenantNId, conversationNId, null, null, false, true);
    }

    private static bool IsConnectionParticipant(ScreenShareSessionRecord record, string connectionId) =>
        string.Equals(record.InitiatorConnectionId, connectionId, StringComparison.Ordinal)
        || string.Equals(record.InviteeConnectionId, connectionId, StringComparison.Ordinal);

    private static bool IsConnectionParticipant(VoiceCallSessionRecord record, string connectionId) =>
        string.Equals(record.CallerConnectionId, connectionId, StringComparison.Ordinal)
        || string.Equals(record.CalleeConnectionId, connectionId, StringComparison.Ordinal);

    private async Task<MediaConfirmationDto> KeepScreenAliveAsync(ScreenShareSessionRecord screen, string actorUserNId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var actorAliveUntil = actorUserNId == screen.InitiatorUserNId ? screen.InitiatorAliveUntil : screen.InviteeAliveUntil;
        var peerAliveUntil = actorUserNId == screen.InitiatorUserNId ? screen.InviteeAliveUntil : screen.InitiatorAliveUntil;
        if (RemoteAssistanceRules.IsTerminal(ParseScreenState(screen.State)))
            return new MediaConfirmationDto { SessionNId = screen.SessionNId, State = screen.State, Version = screen.Version, Allowed = false, ErrorCode = "MEDIA_ENDED" };
        if (screen.DeadlineOn <= now || actorAliveUntil is null || peerAliveUntil is null || actorAliveUntil <= now || peerAliveUntil <= now)
        {
            var reason = screen.DeadlineOn <= now ? "SessionTimeout" : "HeartbeatTimeout";
            var ended = await EndScreenRecordAsync(screen.TenantNId, screen, ScreenShareState.Ended.ToString(), reason, now, cancellationToken);
            RemoveScreenContext(screen.TenantNId, screen.ConversationNId, screen.SessionNId);
            return new MediaConfirmationDto { SessionNId = ended.SessionNId, State = ended.State, Version = ended.Version, Allowed = false, ErrorCode = "MEDIA_EXPIRED" };
        }
        var alive = AliveUntil(now, now.AddSeconds(_options.AliveSeconds), screen.DeadlineOn);
        var updated = actorUserNId == screen.InitiatorUserNId ? screen with { InitiatorAliveUntil = alive } : screen with { InviteeAliveUntil = alive };
        updated = updated with { LastUpdatedOn = now, ConcurrencyVersion = Guid.NewGuid() };
        var saved = await _repository.UpdateScreenAsync(updated, screen.Version, null, cancellationToken);
        if (saved is null)
        {
            var latest = await RequireScreenAsync(screen.TenantNId, screen.SessionNId, cancellationToken);
            if (RemoteAssistanceRules.IsTerminal(ParseScreenState(latest.State)))
                return new MediaConfirmationDto { SessionNId = latest.SessionNId, State = latest.State, Version = latest.Version, Allowed = false, ErrorCode = "MEDIA_ENDED" };
            throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.concurrencyConflict");
        }
        return Confirmation(saved.SessionNId, saved.State, saved.Version, true);
    }

    private async Task<MediaConfirmationDto> KeepVoiceAliveAsync(VoiceCallSessionRecord voice, string actorUserNId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var actorAliveUntil = actorUserNId == voice.CallerUserNId ? voice.CallerAliveUntil : voice.CalleeAliveUntil;
        var peerAliveUntil = actorUserNId == voice.CallerUserNId ? voice.CalleeAliveUntil : voice.CallerAliveUntil;
        if (RemoteAssistanceRules.IsTerminal(ParseVoiceState(voice.State)))
            return new MediaConfirmationDto { SessionNId = voice.CallNId, State = voice.State, Version = voice.Version, Allowed = false, ErrorCode = "MEDIA_ENDED" };
        if (voice.DeadlineOn <= now || actorAliveUntil is null || peerAliveUntil is null || actorAliveUntil <= now || peerAliveUntil <= now)
        {
            var reason = voice.DeadlineOn <= now ? "SessionTimeout" : "HeartbeatTimeout";
            var ended = await EndVoiceRecordAsync(voice, VoiceCallState.Ended.ToString(), reason, now, true, cancellationToken);
            RemoveVoiceContext(voice.TenantNId, voice.ConversationNId, voice.CallNId);
            return new MediaConfirmationDto { SessionNId = ended.CallNId, State = ended.State, Version = ended.Version, Allowed = false, ErrorCode = "MEDIA_EXPIRED" };
        }
        var alive = AliveUntil(now, now.AddSeconds(_options.AliveSeconds), voice.DeadlineOn);
        var updated = actorUserNId == voice.CallerUserNId ? voice with { CallerAliveUntil = alive } : voice with { CalleeAliveUntil = alive };
        updated = updated with { LastUpdatedOn = now, ConcurrencyVersion = Guid.NewGuid() };
        var saved = await _repository.UpdateVoiceAsync(updated, voice.Version, false, null, cancellationToken);
        if (saved is null)
        {
            var latest = await RequireVoiceAsync(voice.TenantNId, voice.CallNId, cancellationToken);
            if (RemoteAssistanceRules.IsTerminal(ParseVoiceState(latest.State)))
                return new MediaConfirmationDto { SessionNId = latest.CallNId, State = latest.State, Version = latest.Version, Allowed = false, ErrorCode = "MEDIA_ENDED" };
            throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.concurrencyConflict");
        }
        return Confirmation(saved.CallNId, saved.State, saved.Version, true);
    }

    private async Task<ConversationDetailDto> RequireConversationAsync(string tenantNId, string actorUserNId, string conversationNId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(conversationNId)) throw new RemoteAssistanceException("MEDIA_INVALID_REQUEST", "collaboration.media.conversationRequired");
        try { return await _collaboration.GetConversationAsync(tenantNId, actorUserNId, conversationNId.Trim(), cancellationToken); }
        catch (CollaborationException exception) when (exception.StatusCode is 400 or 404 or 403) { throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound"); }
    }

    private async Task RequireActivePeerAsync(string tenantNId, string peerUserNId, CancellationToken cancellationToken)
    {
        if (await _directory.GetAsync(tenantNId, peerUserNId, cancellationToken) is not { Status: "Active" })
            throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.peerUnavailable");

        var presence = _collaboration.GetPresence(tenantNId, peerUserNId);
        if (!string.Equals(presence.State, "Online", StringComparison.OrdinalIgnoreCase)
            || presence.ExpiresOn <= DateTimeOffset.UtcNow)
            throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.peerUnavailable");
    }

    private async Task RequirePermissionAsync(string permission, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorSessionNId) || !int.TryParse(actorSecurityVersion, out _)
            || !await _permissions.HasPermissionAsync(permission, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, cancellationToken))
            throw new RemoteAssistanceException("MEDIA_FORBIDDEN", "collaboration.media.forbidden");
    }

    private async Task<ScreenShareSessionRecord> RequireScreenAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken)
        => await _repository.GetScreenAsync(tenantNId, RequireNId(sessionNId, "MEDIA_INVALID_REQUEST"), cancellationToken)
            ?? throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");

    private async Task<VoiceCallSessionRecord> RequireVoiceAsync(string tenantNId, string callNId, CancellationToken cancellationToken)
        => await _repository.GetVoiceAsync(tenantNId, RequireNId(callNId, "MEDIA_INVALID_REQUEST"), cancellationToken)
            ?? throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");

    private static void EnsureScreenBindable(ScreenShareSessionRecord item, string actorUserNId)
    {
        EnsureParticipant(item.InitiatorUserNId, item.InviteeUserNId, actorUserNId);
        var state = ParseScreenState(item.State);
        if (!RemoteAssistanceRules.CanBind(state))
            throw new RemoteAssistanceException("MEDIA_NOT_ACCEPTED", "collaboration.media.notAccepted");
        if (item.DeadlineOn <= DateTimeOffset.UtcNow)
            throw new RemoteAssistanceException("MEDIA_EXPIRED", "collaboration.media.expired");
    }

    private static void EnsureVoiceBindable(VoiceCallSessionRecord item, string actorUserNId)
    {
        EnsureParticipant(item.CallerUserNId, item.CalleeUserNId, actorUserNId);
        var state = ParseVoiceState(item.State);
        if (!RemoteAssistanceRules.CanBind(state))
            throw new RemoteAssistanceException("MEDIA_NOT_ACCEPTED", "collaboration.media.notAccepted");
        if (item.DeadlineOn <= DateTimeOffset.UtcNow)
            throw new RemoteAssistanceException("MEDIA_EXPIRED", "collaboration.media.expired");
    }

    private static void EnsureScreenConnection(ScreenShareSessionRecord item, string actorUserNId, string connectionId)
    {
        var expected = actorUserNId == item.InitiatorUserNId ? item.InitiatorConnectionId : item.InviteeConnectionId;
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(expected, RequireConnectionId(connectionId), StringComparison.Ordinal))
            throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
    }

    private static void EnsureVoiceConnection(VoiceCallSessionRecord item, string actorUserNId, string connectionId)
    {
        var expected = actorUserNId == item.CallerUserNId ? item.CallerConnectionId : item.CalleeConnectionId;
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(expected, RequireConnectionId(connectionId), StringComparison.Ordinal))
            throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
    }

    private async Task EnsureContextSignalsAllowedAsync(string tenantNId, MediaContextSnapshot context, MediaEndpointSnapshot endpoint, CancellationToken cancellationToken)
    {
        if (endpoint.ScreenSessionNId is null && endpoint.VoiceCallNId is null)
            throw new RemoteAssistanceException("MEDIA_ENDPOINT_BOUND", "collaboration.media.endpointBound");
        var screenAllowed = context.ScreenSessionNId is not null
            && await _repository.GetScreenAsync(tenantNId, context.ScreenSessionNId, cancellationToken) is { } screen
            && IsScreenAllowed(screen);
        var voiceAllowed = context.VoiceCallNId is not null
            && await _repository.GetVoiceAsync(tenantNId, context.VoiceCallNId, cancellationToken) is { } voice
            && IsVoiceAllowed(voice);
        if (!screenAllowed && !voiceAllowed)
            throw new RemoteAssistanceException("MEDIA_NOT_ACCEPTED", "collaboration.media.notAccepted");
    }

    private static void EnsureParticipant(string first, string second, string actor)
    {
        if (actor != first && actor != second) throw new RemoteAssistanceException("MEDIA_NOT_FOUND", "collaboration.media.notFound");
    }

    private static void EnsureExpectedVersion(long current, long expected)
    {
        if (current != expected) throw new RemoteAssistanceException("MEDIA_CONFLICT", "collaboration.media.concurrencyConflict");
    }

    private static void EnsureFeature(bool enabled, string code) { if (!enabled) throw new RemoteAssistanceException(code, "collaboration.media.disabled"); }

    private static string RequireNId(string? value, string code) { var trimmed = value?.Trim(); if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 128) throw new RemoteAssistanceException(code, "collaboration.media.invalidRequest"); return trimmed; }
    private static string RequireConnectionId(string? value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw new RemoteAssistanceException("MEDIA_INVALID_REQUEST", "collaboration.media.invalidRequest"); return value; }
    private static int ParseAuthVersion(string value) => int.TryParse(value, out var parsed) ? parsed : throw new RemoteAssistanceException("MEDIA_FORBIDDEN", "collaboration.media.forbidden");
    private static string NewNId(string prefix) => prefix + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
    private static DateTimeOffset AliveUntil(DateTimeOffset now, DateTimeOffset requested, DateTimeOffset deadline) => requested <= deadline ? requested : deadline;
    private static ScreenShareDirection ParseScreenDirection(string? value) => Enum.TryParse<ScreenShareDirection>(value, true, out var parsed) ? parsed : throw new RemoteAssistanceException("MEDIA_INVALID_REQUEST", "collaboration.media.directionInvalid");
    private static MediaAnswer ParseAnswer(string? value) => Enum.TryParse<MediaAnswer>(value, true, out var parsed) ? parsed : throw new RemoteAssistanceException("MEDIA_INVALID_REQUEST", "collaboration.media.answerInvalid");
    private static ScreenShareState ParseScreenState(string value) => Enum.TryParse<ScreenShareState>(value, true, out var parsed) ? parsed : ScreenShareState.Failed;
    private static VoiceCallState ParseVoiceState(string value) => Enum.TryParse<VoiceCallState>(value, true, out var parsed) ? parsed : VoiceCallState.Failed;
    private static string NormalizeEndReason(string? reason, string fallback) => string.IsNullOrWhiteSpace(reason) ? fallback : reason.Trim()[..Math.Min(32, reason.Trim().Length)];
    private static void ValidateSignal(SignalMediaRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MediaContextNId) || string.IsNullOrWhiteSpace(request.NegotiationNId) || request.Sequence < 1)
            throw new RemoteAssistanceException("MEDIA_INVALID_REQUEST", "collaboration.media.invalidRequest");
        if (!Enum.TryParse<MediaSignalKind>(request.Kind, true, out var kind)) throw new RemoteAssistanceException("MEDIA_NEGOTIATION_INVALID", "collaboration.media.signalInvalid");
        var description = request.Description is not null;
        var candidate = request.Candidate is not null;
        if ((kind == MediaSignalKind.Offer || kind == MediaSignalKind.Answer) != description || (kind == MediaSignalKind.IceCandidate) != candidate)
            throw new RemoteAssistanceException("MEDIA_NEGOTIATION_INVALID", "collaboration.media.signalInvalid");
        if (request.Description?.Sdp.Length > 64 * 1024 || request.Candidate?.Candidate.Length > 4096 || request.Candidate?.UsernameFragment?.Length > 256)
            throw new RemoteAssistanceException("MEDIA_INVALID_REQUEST", "collaboration.media.signalTooLarge");
    }

    private static bool IsScreenAllowed(ScreenShareSessionRecord item) => item.State is "Accepted" or "Connecting" or "Sharing";
    private static bool IsVoiceAllowed(VoiceCallSessionRecord item) => item.State is "Accepted" or "Connecting" or "Active";
    private static MediaConfirmationDto Confirmation(string sessionNId, string state, long version, bool allowed) => new() { SessionNId = sessionNId, State = state, Version = version, ValidForMs = allowed ? 30000 : 0, Allowed = allowed, ErrorCode = allowed ? null : "MEDIA_ENDED" };
    private static MediaConfirmationDto FailureConfirmation(string sessionNId, string code) => new() { SessionNId = sessionNId, State = "Unknown", Version = 0, Allowed = false, ErrorCode = code };

    private static ScreenDto ToScreenDto(ScreenShareSessionRecord item) => new()
    {
        SessionNId = item.SessionNId, ConversationNId = item.ConversationNId, Direction = item.Direction, InitiatorUserNId = item.InitiatorUserNId, InviteeUserNId = item.InviteeUserNId, SharerUserNId = item.SharerUserNId, ViewerUserNId = item.ViewerUserNId, State = item.State, Answer = item.Answer, Version = item.Version, DeadlineOn = item.DeadlineOn, StartedOn = item.StartedOn, EndedOn = item.EndedOn, EndReason = item.EndReason, InitiatorStoppedReported = item.InitiatorStoppedReportedOn is not null, InviteeStoppedReported = item.InviteeStoppedReportedOn is not null,
    };
    private static VoiceDto ToVoiceDto(VoiceCallSessionRecord item) => new()
    {
        CallNId = item.CallNId, ConversationNId = item.ConversationNId, CallerUserNId = item.CallerUserNId, CalleeUserNId = item.CalleeUserNId, State = item.State, Answer = item.Answer, Version = item.Version, DeadlineOn = item.DeadlineOn, StartedOn = item.StartedOn, EndedOn = item.EndedOn, EndReason = item.EndReason, CallerStoppedReported = item.CallerStoppedReportedOn is not null, CalleeStoppedReported = item.CalleeStoppedReportedOn is not null,
    };

    private static RemoteAssistanceEventPayload EventPayload(string capability, string action, string? actorUserNId, ScreenShareSessionRecord item)
        => new(1, capability, item.SessionNId, null, item.ConversationNId, actorUserNId, actorUserNId is null ? "system" : "user", action, item.State, item.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), item.LastUpdatedOn, AuditEventNId(item.TenantNId, "screen-session", item.SessionNId, action, item.Version), $"remote-assistance.screen.{action.ToLowerInvariant()}", "screen-session", item.SessionNId, new { contractVersion = 1, actorKind = actorUserNId is null ? "System" : "User" });

    private static RemoteAssistanceEventPayload EventPayload(string capability, string action, string? actorUserNId, VoiceCallSessionRecord item)
        => new(1, capability, null, item.CallNId, item.ConversationNId, actorUserNId, actorUserNId is null ? "system" : "user", action, item.State, item.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), item.LastUpdatedOn, AuditEventNId(item.TenantNId, "voice-call", item.CallNId, action, item.Version), $"remote-assistance.voice.{action.ToLowerInvariant()}", "voice-call", item.CallNId, new { contractVersion = 1, actorKind = actorUserNId is null ? "System" : "User" });

    private static string AuditEventNId(string tenantNId, string objectType, string objectNId, string action, long version)
        => "AUD-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{tenantNId}\n{objectType}\n{objectNId}\n{action}\n{version}"))).ToLowerInvariant();

}

public sealed record KeepAliveMediaDto
{
    public long ContextRevision { get; init; }
    public MediaConfirmationDto? Screen { get; init; }
    public MediaConfirmationDto? Voice { get; init; }
}

public sealed record EndAllMediaDto
{
    public ScreenDto? Screen { get; init; }
    public VoiceDto? Voice { get; init; }
}
