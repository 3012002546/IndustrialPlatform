using IndustrialPlatform.Collaboration.Application.RemoteAssistance;
using IndustrialPlatform.Collaboration.Api.Hubs;
using IndustrialPlatform.Collaboration.Domain.RemoteAssistance;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Application_RemoteAssistanceTests
{
    [Fact]
    public void Request_hashes_are_stable_and_include_the_declared_semantics()
    {
        var screen = RemoteAssistanceRules.ComputeScreenRequestHash("conversation-1", ScreenShareDirection.ShareMine);
        var same = RemoteAssistanceRules.ComputeScreenRequestHash("conversation-1", ScreenShareDirection.ShareMine);
        var otherDirection = RemoteAssistanceRules.ComputeScreenRequestHash("conversation-1", ScreenShareDirection.RequestPeer);
        var voice = RemoteAssistanceRules.ComputeVoiceRequestHash("conversation-1");

        Assert.Equal(screen, same);
        Assert.NotEqual(screen, otherDirection);
        Assert.NotEqual(screen, voice);
        Assert.Equal(64, screen.Length);
        Assert.All(screen, character => Assert.True(Uri.IsHexDigit(character)));
    }

    [Fact]
    public void Context_binds_one_endpoint_per_user_and_rejects_stale_or_duplicate_signals()
    {
        var registry = new MediaContextRegistry();
        var initial = registry.GetOrCreate("tenant-1", "conversation-1", "user-b", "user-a");

        var low = registry.Bind(
            "tenant-1", "conversation-1", "user-a", "connection-a", "sid-a", 7,
            DateTimeOffset.UtcNow.AddMinutes(5), "screen-1", null, "user-b");
        var high = registry.Bind(
            "tenant-1", "conversation-1", "user-b", "connection-b", "sid-b", 8,
            DateTimeOffset.UtcNow.AddMinutes(5), null, "voice-1", "user-a");

        Assert.Equal(initial.MediaContextNId, low.Context.MediaContextNId);
        Assert.Equal("Low", low.EndpointRole);
        Assert.True(low.Polite);
        Assert.Equal("High", high.EndpointRole);
        Assert.False(high.Polite);
        Assert.Equal("connection-a", high.TargetConnectionId);
        var bound = registry.Find("tenant-1", "conversation-1");
        Assert.NotNull(bound);
        Assert.Equal("connection-b", bound!.High?.ConnectionId);

        var accepted = registry.AcceptSignal(
            initial.MediaContextNId, "tenant-1", "conversation-1", "user-a", "connection-a", high.Context.Revision, 1);
        Assert.Equal("connection-b", accepted.TargetConnectionId);

        var duplicate = Assert.Throws<MediaContextException>(() => registry.AcceptSignal(
            initial.MediaContextNId, "tenant-1", "conversation-1", "user-a", "connection-a", high.Context.Revision, 1));
        Assert.Equal("MEDIA_NEGOTIATION_INVALID", duplicate.Code);

        var stale = Assert.Throws<MediaContextException>(() => registry.AcceptSignal(
            initial.MediaContextNId, "tenant-1", "conversation-1", "user-a", "connection-a", high.Context.Revision - 1, 2));
        Assert.Equal("MEDIA_CONTEXT_STALE", stale.Code);

        var endpointBound = Assert.Throws<MediaContextException>(() => registry.Bind(
            "tenant-1", "conversation-1", "user-a", "connection-other", "sid-other", 9,
            DateTimeOffset.UtcNow.AddMinutes(5), "screen-1", null, "user-b"));
        Assert.Equal("MEDIA_ENDPOINT_BOUND", endpointBound.Code);
    }

    [Fact]
    public void Exactly_repeated_binding_is_idempotent_and_does_not_advance_revision()
    {
        var registry = new MediaContextRegistry();
        var first = registry.Bind(
            "tenant-1", "conversation-1", "user-a", "connection-a", "sid-a", 7,
            DateTimeOffset.UtcNow.AddMinutes(5), "screen-1", null, "user-b");

        var repeated = registry.Bind(
            "tenant-1", "conversation-1", "user-a", "connection-a", "sid-a", 7,
            first.Context.Low!.TokenExpiresOn, "screen-1", null, "user-b");

        Assert.Equal(first.Context.Revision, repeated.Context.Revision);
        Assert.Equal(first.Context, repeated.Context);
    }

    [Fact]
    public void Removing_screen_capability_preserves_active_voice_endpoints()
    {
        var registry = new MediaContextRegistry();
        var first = registry.Bind(
            "tenant-1", "conversation-1", "user-a", "connection-a", "sid-a", 7,
            DateTimeOffset.UtcNow.AddMinutes(5), "screen-1", "voice-1", "user-b");
        registry.Bind(
            "tenant-1", "conversation-1", "user-b", "connection-b", "sid-b", 8,
            DateTimeOffset.UtcNow.AddMinutes(5), "screen-1", "voice-1", "user-a");

        registry.UpdateCapability(first.Context.MediaContextNId, "tenant-1", "conversation-1", null, null, true, false);

        var remaining = registry.Find("tenant-1", "conversation-1");
        Assert.NotNull(remaining);
        Assert.Null(remaining!.ScreenSessionNId);
        Assert.Equal("voice-1", remaining.VoiceCallNId);
        Assert.Null(remaining.Low?.ScreenSessionNId);
        Assert.Null(remaining.High?.ScreenSessionNId);
        Assert.Equal("voice-1", remaining.Low?.VoiceCallNId);
        Assert.Equal("voice-1", remaining.High?.VoiceCallNId);

        registry.UpdateCapability(first.Context.MediaContextNId, "tenant-1", "conversation-1", null, null, false, true);
        Assert.Null(registry.Find("tenant-1", "conversation-1"));
    }

    [Fact]
    public void Refreshing_a_connection_lease_updates_only_that_endpoint_and_never_shortens_it()
    {
        var registry = new MediaContextRegistry();
        var originalExpiry = DateTimeOffset.UtcNow.AddSeconds(1);
        var peerExpiry = DateTimeOffset.UtcNow.AddMinutes(5);
        registry.Bind(
            "tenant-1", "conversation-1", "user-a", "connection-a", "sid-a", 7,
            originalExpiry, "screen-1", null, "user-b");
        registry.Bind(
            "tenant-1", "conversation-1", "user-b", "connection-b", "sid-b", 8,
            peerExpiry, null, "voice-1", "user-a");

        var renewedExpiry = DateTimeOffset.UtcNow.AddMinutes(2);
        Assert.True(registry.RefreshConnectionLease("connection-a", renewedExpiry));

        var refreshed = registry.Find("tenant-1", "conversation-1");
        Assert.NotNull(refreshed);
        Assert.Equal(renewedExpiry, refreshed!.Low!.TokenExpiresOn);
        Assert.Equal(peerExpiry, refreshed.High!.TokenExpiresOn);

        Assert.False(registry.RefreshConnectionLease("connection-a", originalExpiry));
        Assert.Equal(renewedExpiry, registry.Find("tenant-1", "conversation-1")!.Low!.TokenExpiresOn);
        Assert.False(registry.RefreshConnectionLease("other-connection", renewedExpiry));
    }

    [Fact]
    public async Task Media_context_coordinator_serializes_same_context_and_reclaims_capacity()
    {
        var coordinator = new MediaContextCoordinator();
        using var first = await coordinator.AcquireAsync("tenant-1", "conversation-1", CancellationToken.None);
        var secondTask = coordinator.AcquireAsync("tenant-1", "conversation-1", CancellationToken.None);

        await Task.Delay(25);
        Assert.False(secondTask.IsCompleted);

        first.Dispose();
        using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(1));
        second.Dispose();

        using var queueHolder = await coordinator.AcquireAsync("tenant-queue", "conversation-1", CancellationToken.None);
        var waiters = Enumerable.Range(0, 31)
            .Select(async _ =>
            {
                using var lease = await coordinator.AcquireAsync("tenant-queue", "conversation-1", CancellationToken.None);
            })
            .ToArray();
        var queueBusy = await Assert.ThrowsAsync<MediaContextException>(() =>
            coordinator.AcquireAsync("tenant-queue", "conversation-1", CancellationToken.None));
        Assert.Equal("MEDIA_BUSY", queueBusy.Code);
        queueHolder.Dispose();
        await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(1));

        var held = new List<MediaContextCoordinator.Lease>();
        try
        {
            for (var index = 0; index < 256; index++)
                held.Add(await coordinator.AcquireAsync("tenant-capacity", $"conversation-{index}", CancellationToken.None));

            var busy = await Assert.ThrowsAsync<MediaContextException>(() =>
                coordinator.AcquireAsync("tenant-capacity", "conversation-overflow", CancellationToken.None));
            Assert.Equal("MEDIA_BUSY", busy.Code);
        }
        finally
        {
            foreach (var lease in held)
                lease.Dispose();
        }

        using var reclaimed = await coordinator.AcquireAsync("tenant-capacity", "conversation-reclaimed", CancellationToken.None);
    }

    [Theory]
    [InlineData(ScreenShareState.Pending, false)]
    [InlineData(ScreenShareState.Accepted, true)]
    [InlineData(ScreenShareState.Connecting, true)]
    [InlineData(ScreenShareState.Sharing, true)]
    [InlineData(ScreenShareState.Ended, false)]
    public void Screen_bind_requires_an_accepted_or_live_session(ScreenShareState state, bool expected)
    {
        Assert.Equal(expected, RemoteAssistanceRules.CanBind(state));
    }

    [Theory]
    [InlineData(VoiceCallState.Ringing, false)]
    [InlineData(VoiceCallState.Accepted, true)]
    [InlineData(VoiceCallState.Connecting, true)]
    [InlineData(VoiceCallState.Active, true)]
    [InlineData(VoiceCallState.Ended, false)]
    public void Voice_bind_requires_an_accepted_or_live_call(VoiceCallState state, bool expected)
    {
        Assert.Equal(expected, RemoteAssistanceRules.CanBind(state));
    }

    [Theory]
    [InlineData(ScreenShareState.Pending, true, "InvitationTimeout")]
    [InlineData(ScreenShareState.Accepted, true, "MaxDuration")]
    [InlineData(ScreenShareState.Connecting, false, "HeartbeatTimeout")]
    public void Screen_lifecycle_timeout_reason_preserves_invitation_semantics(ScreenShareState state, bool deadlineExpired, string expected)
    {
        Assert.Equal(expected, RemoteAssistanceRules.ScreenLifecycleReason(state, deadlineExpired));
    }

    [Theory]
    [InlineData(VoiceCallState.Ringing, true, "RingTimeout")]
    [InlineData(VoiceCallState.Accepted, true, "MaxDuration")]
    [InlineData(VoiceCallState.Active, false, "HeartbeatTimeout")]
    public void Voice_lifecycle_timeout_reason_preserves_ring_semantics(VoiceCallState state, bool deadlineExpired, string expected)
    {
        Assert.Equal(expected, RemoteAssistanceRules.VoiceLifecycleReason(state, deadlineExpired));
    }

    [Fact]
    public void Collaboration_hub_exposes_the_complete_media_contract()
    {
        var expected = new[]
        {
            "InviteScreenShare", "RespondScreenShare", "EndScreenShare",
            "InviteVoiceCall", "RespondVoiceCall", "EndVoiceCall",
            "GetConversationMedia", "GetMyActiveMedia", "BindMedia", "SignalMedia",
            "MediaReady", "KeepAliveMedia", "SetVoiceMuted", "ReportMediaStopped", "EndAllMedia",
        };
        var actual = typeof(CollaborationHub).GetMethods()
            .Where(method => method.DeclaringType == typeof(CollaborationHub))
            .Select(method => method.Name)
            .Where(name => expected.Contains(name, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
    }
}
