namespace IndustrialPlatform.Collaboration.Domain.RemoteAssistance;

public enum ScreenShareDirection
{
    ShareMine,
    RequestPeer,
}

public enum ScreenShareState
{
    Pending,
    Accepted,
    Connecting,
    Sharing,
    Declined,
    Cancelled,
    Expired,
    Failed,
    Ended,
}

public enum VoiceCallState
{
    Ringing,
    Accepted,
    Connecting,
    Active,
    Declined,
    Cancelled,
    Missed,
    Failed,
    Ended,
}

public enum MediaAnswer
{
    Accept,
    Decline,
}

public enum MediaCapability
{
    Screen,
    Voice,
}

public enum MediaSignalKind
{
    Offer,
    Answer,
    IceCandidate,
}

public static class RemoteAssistanceRules
{
    public static readonly IReadOnlySet<ScreenShareState> ActiveScreenStates =
        new HashSet<ScreenShareState>
        {
            ScreenShareState.Pending,
            ScreenShareState.Accepted,
            ScreenShareState.Connecting,
            ScreenShareState.Sharing,
        };

    public static readonly IReadOnlySet<VoiceCallState> ActiveVoiceStates =
        new HashSet<VoiceCallState>
        {
            VoiceCallState.Ringing,
            VoiceCallState.Accepted,
            VoiceCallState.Connecting,
            VoiceCallState.Active,
        };

    public static bool IsTerminal(ScreenShareState state) => !ActiveScreenStates.Contains(state);

    public static bool IsTerminal(VoiceCallState state) => !ActiveVoiceStates.Contains(state);

    public static bool CanBind(ScreenShareState state) => state is ScreenShareState.Accepted or ScreenShareState.Connecting or ScreenShareState.Sharing;

    public static bool CanBind(VoiceCallState state) => state is VoiceCallState.Accepted or VoiceCallState.Connecting or VoiceCallState.Active;

    public static string ScreenLifecycleReason(ScreenShareState state, bool deadlineExpired)
        => state == ScreenShareState.Pending ? "InvitationTimeout" : deadlineExpired ? "MaxDuration" : "HeartbeatTimeout";

    public static string VoiceLifecycleReason(VoiceCallState state, bool deadlineExpired)
        => state == VoiceCallState.Ringing ? "RingTimeout" : deadlineExpired ? "MaxDuration" : "HeartbeatTimeout";

    public static string ComputeScreenRequestHash(string conversationNId, ScreenShareDirection direction)
        => RemoteAssistanceHash.Compute($"[\"{Escape(conversationNId)}\",\"{direction}\"]");

    public static string ComputeVoiceRequestHash(string conversationNId)
        => RemoteAssistanceHash.Compute($"[\"{Escape(conversationNId)}\"]");

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

public static class RemoteAssistanceHash
{
    public static string Compute(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
