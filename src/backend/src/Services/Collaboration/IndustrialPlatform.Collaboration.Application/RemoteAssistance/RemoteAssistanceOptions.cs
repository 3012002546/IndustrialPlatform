namespace IndustrialPlatform.Collaboration.Application.RemoteAssistance;

public sealed class RemoteAssistanceOptions
{
    public bool ScreenEnabled { get; set; }
    public bool VoiceEnabled { get; set; }
    public int ScreenInvitationSeconds { get; set; } = 120;
    public int VoiceRingSeconds { get; set; } = 30;
    public int SetupSeconds { get; set; } = 60;
    public int AliveSeconds { get; set; } = 30;
    public int MaxDurationMinutes { get; set; } = 30;
    public int TurnCredentialTtlSeconds { get; set; } = 300;
    public string? TurnCredentialSecret { get; set; }
    public string IcePolicy { get; set; } = "All";
    public string[] IceServerUrls { get; set; } = [];

    public string GetNormalizedIcePolicy()
    {
        if (string.Equals(IcePolicy, "All", StringComparison.OrdinalIgnoreCase)) return "All";
        if (string.Equals(IcePolicy, "RelayOnly", StringComparison.OrdinalIgnoreCase)) return "RelayOnly";
        throw new RemoteAssistanceException("MEDIA_ICE_CONFIGURATION", "collaboration.media.iceConfiguration");
    }
}
