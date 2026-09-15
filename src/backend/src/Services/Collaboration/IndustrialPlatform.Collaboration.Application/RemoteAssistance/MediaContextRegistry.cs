using System.Collections.Concurrent;

namespace IndustrialPlatform.Collaboration.Application.RemoteAssistance;

public sealed record MediaEndpointSnapshot(
    string ConnectionId,
    string SessionNId,
    int AuthVersion,
    DateTimeOffset TokenExpiresOn,
    string? ScreenSessionNId,
    string? VoiceCallNId,
    long LastSignalSequence);

public sealed record MediaContextSnapshot(
    string MediaContextNId,
    string TenantNId,
    string ConversationNId,
    string LowUserNId,
    string HighUserNId,
    MediaEndpointSnapshot? Low,
    MediaEndpointSnapshot? High,
    string? ScreenSessionNId,
    string? VoiceCallNId,
    long Revision,
    bool Disposed);

public sealed record MediaBindingState(
    MediaContextSnapshot Context,
    string EndpointRole,
    bool Polite,
    string? TargetConnectionId);

public sealed class MediaContextRegistry
{
    private readonly ConcurrentDictionary<string, Context> _contexts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _connectionContexts = new(StringComparer.Ordinal);

    public MediaContextSnapshot GetOrCreate(string tenantNId, string conversationNId, string firstUserNId, string peerUserNId)
    {
        var (low, high) = string.CompareOrdinal(firstUserNId, peerUserNId) < 0
            ? (firstUserNId, peerUserNId)
            : (peerUserNId, firstUserNId);
        var key = Key(tenantNId, conversationNId);
        var context = _contexts.GetOrAdd(key, _ => new Context(tenantNId, conversationNId, low, high));
        lock (context.Gate)
            return context.Snapshot();
    }

    public MediaBindingState Bind(
        string tenantNId,
        string conversationNId,
        string actorUserNId,
        string connectionId,
        string sessionNId,
        int authVersion,
        DateTimeOffset tokenExpiresOn,
        string? screenSessionNId,
        string? voiceCallNId,
        string peerUserNId)
    {
        var context = _contexts.GetOrAdd(
            Key(tenantNId, conversationNId),
            _ =>
            {
                var (low, high) = string.CompareOrdinal(actorUserNId, peerUserNId) < 0
                    ? (actorUserNId, peerUserNId)
                    : (peerUserNId, actorUserNId);
                return new Context(tenantNId, conversationNId, low, high);
            });

        lock (context.Gate)
        {
            if (context.Disposed)
                throw new MediaContextException("MEDIA_CONTEXT_STALE");

            if (_connectionContexts.TryGetValue(connectionId, out var boundKey)
                && !string.Equals(boundKey, Key(tenantNId, conversationNId), StringComparison.Ordinal))
                throw new MediaContextException("MEDIA_BUSY");

            var role = actorUserNId == context.LowUserNId ? "Low" : actorUserNId == context.HighUserNId ? "High" : null;
            if (role is null)
                throw new MediaContextException("MEDIA_NOT_FOUND");

            var current = role == "Low" ? context.Low : context.High;
            if (current is not null && !string.Equals(current.ConnectionId, connectionId, StringComparison.Ordinal))
                throw new MediaContextException("MEDIA_ENDPOINT_BOUND");

            if (current is not null
                && string.Equals(current.SessionNId, sessionNId, StringComparison.Ordinal)
                && current.AuthVersion == authVersion
                && current.TokenExpiresOn == tokenExpiresOn
                && string.Equals(current.ScreenSessionNId, screenSessionNId, StringComparison.Ordinal)
                && string.Equals(current.VoiceCallNId, voiceCallNId, StringComparison.Ordinal))
            {
                return new MediaBindingState(context.Snapshot(), role, role == "Low", role == "Low" ? context.High?.ConnectionId : context.Low?.ConnectionId);
            }

            var next = new MediaEndpointSnapshot(connectionId, sessionNId, authVersion, tokenExpiresOn, screenSessionNId, voiceCallNId, current?.LastSignalSequence ?? 0);
            if (role == "Low") context.Low = next;
            else context.High = next;
            context.ScreenSessionNId = screenSessionNId ?? context.ScreenSessionNId;
            context.VoiceCallNId = voiceCallNId ?? context.VoiceCallNId;
            context.Revision++;
            _connectionContexts[connectionId] = Key(tenantNId, conversationNId);
            return new MediaBindingState(context.Snapshot(), role, role == "Low", role == "Low" ? context.High?.ConnectionId : context.Low?.ConnectionId);
        }
    }

    public MediaContextSnapshot Require(string mediaContextNId, string tenantNId, string conversationNId)
    {
        var context = _contexts.Values.FirstOrDefault(item => item.MediaContextNId == mediaContextNId);
        if (context is null)
            throw new MediaContextException("MEDIA_NOT_FOUND");
        lock (context.Gate)
        {
            if (context.TenantNId != tenantNId || context.ConversationNId != conversationNId || context.Disposed)
                throw new MediaContextException("MEDIA_CONTEXT_STALE");
            return context.Snapshot();
        }
    }

    public MediaContextSnapshot? Find(string tenantNId, string conversationNId)
    {
        if (!_contexts.TryGetValue(Key(tenantNId, conversationNId), out var context))
            return null;
        lock (context.Gate)
            return context.Disposed ? null : context.Snapshot();
    }

    public bool RefreshConnectionLease(string connectionId, DateTimeOffset tokenExpiresOn)
    {
        if (!_connectionContexts.TryGetValue(connectionId, out var key)
            || !_contexts.TryGetValue(key, out var context))
            return false;

        lock (context.Gate)
        {
            if (context.Disposed)
                return false;

            if (context.Low?.ConnectionId == connectionId)
            {
                if (tokenExpiresOn <= context.Low.TokenExpiresOn)
                    return false;
                context.Low = context.Low with { TokenExpiresOn = tokenExpiresOn };
                return true;
            }

            if (context.High?.ConnectionId == connectionId)
            {
                if (tokenExpiresOn <= context.High.TokenExpiresOn)
                    return false;
                context.High = context.High with { TokenExpiresOn = tokenExpiresOn };
                return true;
            }

            return false;
        }
    }

    public MediaContextSnapshot? FindById(string mediaContextNId)
    {
        var context = _contexts.Values.FirstOrDefault(item => item.MediaContextNId == mediaContextNId);
        if (context is null) return null;
        lock (context.Gate)
            return context.Disposed ? null : context.Snapshot();
    }

    public string? GetPeerConnectionId(string mediaContextNId, string actorUserNId)
    {
        var context = FindById(mediaContextNId);
        if (context is null) return null;
        return context.LowUserNId == actorUserNId ? context.High?.ConnectionId : context.Low?.ConnectionId;
    }

    public (MediaContextSnapshot Context, string TargetConnectionId) AcceptSignal(
        string mediaContextNId,
        string tenantNId,
        string conversationNId,
        string actorUserNId,
        string connectionId,
        long contextRevision,
        long sequence)
    {
        var context = _contexts.Values.FirstOrDefault(item => item.MediaContextNId == mediaContextNId);
        if (context is null)
            throw new MediaContextException("MEDIA_NOT_FOUND");
        lock (context.Gate)
        {
            if (context.TenantNId != tenantNId || context.ConversationNId != conversationNId || context.Disposed || context.Revision != contextRevision)
                throw new MediaContextException("MEDIA_CONTEXT_STALE");
            var endpoint = actorUserNId == context.LowUserNId ? context.Low : actorUserNId == context.HighUserNId ? context.High : null;
            if (endpoint is null || endpoint.ConnectionId != connectionId)
                throw new MediaContextException("MEDIA_ENDPOINT_BOUND");
            if (sequence <= endpoint.LastSignalSequence)
                throw new MediaContextException("MEDIA_NEGOTIATION_INVALID");
            var updated = endpoint with { LastSignalSequence = sequence };
            if (actorUserNId == context.LowUserNId) context.Low = updated;
            else context.High = updated;
            var target = actorUserNId == context.LowUserNId ? context.High?.ConnectionId : context.Low?.ConnectionId;
            if (string.IsNullOrWhiteSpace(target))
                throw new MediaContextException("MEDIA_BUSY");
            return (context.Snapshot(), target);
        }
    }

    public MediaContextSnapshot UpdateCapability(
        string mediaContextNId,
        string tenantNId,
        string conversationNId,
        string? screenSessionNId,
        string? voiceCallNId,
        bool removeScreen,
        bool removeVoice)
    {
        var context = _contexts.Values.FirstOrDefault(item => item.MediaContextNId == mediaContextNId);
        if (context is null)
            throw new MediaContextException("MEDIA_NOT_FOUND");
        lock (context.Gate)
        {
            if (context.TenantNId != tenantNId || context.ConversationNId != conversationNId || context.Disposed)
                throw new MediaContextException("MEDIA_CONTEXT_STALE");
            if (removeScreen)
            {
                context.ScreenSessionNId = null;
                if (context.Low is not null) context.Low = context.Low with { ScreenSessionNId = null };
                if (context.High is not null) context.High = context.High with { ScreenSessionNId = null };
            }
            else if (screenSessionNId is not null) context.ScreenSessionNId = screenSessionNId;
            if (removeVoice)
            {
                context.VoiceCallNId = null;
                if (context.Low is not null) context.Low = context.Low with { VoiceCallNId = null };
                if (context.High is not null) context.High = context.High with { VoiceCallNId = null };
            }
            else if (voiceCallNId is not null) context.VoiceCallNId = voiceCallNId;
            context.Revision++;
            if (context.ScreenSessionNId is null && context.VoiceCallNId is null)
                DisposeLocked(context);
            return context.Snapshot();
        }
    }

    public void RemoveConnection(string connectionId)
    {
        if (!_connectionContexts.TryRemove(connectionId, out var key) || !_contexts.TryGetValue(key, out var context))
            return;
        lock (context.Gate)
        {
            if (context.Low?.ConnectionId == connectionId) context.Low = null;
            if (context.High?.ConnectionId == connectionId) context.High = null;
            if (context.ScreenSessionNId is null && context.VoiceCallNId is null)
                DisposeLocked(context);
        }
    }

    private void DisposeLocked(Context context)
    {
        context.Disposed = true;
        if (context.Low is not null) _connectionContexts.TryRemove(context.Low.ConnectionId, out _);
        if (context.High is not null) _connectionContexts.TryRemove(context.High.ConnectionId, out _);
        _contexts.TryRemove(Key(context.TenantNId, context.ConversationNId), out _);
    }

    private static string Key(string tenantNId, string conversationNId) => $"{tenantNId}\n{conversationNId}";

    private sealed class Context
    {
        public Context(string tenantNId, string conversationNId, string lowUserNId, string highUserNId)
        {
            TenantNId = tenantNId;
            ConversationNId = conversationNId;
            LowUserNId = lowUserNId;
            HighUserNId = highUserNId;
            MediaContextNId = "MC-" + Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
        }

        public object Gate { get; } = new();
        public string MediaContextNId { get; }
        public string TenantNId { get; }
        public string ConversationNId { get; }
        public string LowUserNId { get; }
        public string HighUserNId { get; }
        public MediaEndpointSnapshot? Low { get; set; }
        public MediaEndpointSnapshot? High { get; set; }
        public string? ScreenSessionNId { get; set; }
        public string? VoiceCallNId { get; set; }
        public long Revision { get; set; }
        public bool Disposed { get; set; }

        public MediaContextSnapshot Snapshot() => new(MediaContextNId, TenantNId, ConversationNId, LowUserNId, HighUserNId, Low, High, ScreenSessionNId, VoiceCallNId, Revision, Disposed);
    }
}

public sealed class MediaContextException : Exception
{
    public MediaContextException(string code) : base(code) => Code = code;

    public string Code { get; }
}
