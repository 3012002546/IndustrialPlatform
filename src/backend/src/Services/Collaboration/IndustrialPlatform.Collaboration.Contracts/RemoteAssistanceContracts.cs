namespace IndustrialPlatform.Collaboration.Contracts;

using System.Text.Json.Serialization;

public static partial class CollaborationPermissions
{
    public const string RemoteAssistanceSessionShare = "remote-assistance.session.share";
    public const string RemoteAssistanceSessionJoin = "remote-assistance.session.join";
    public const string RemoteAssistanceVoiceCall = "remote-assistance.voice.call";
}

public sealed record MediaHubResult<T>
{
    public bool Ok { get; init; }
    public T? Data { get; init; }
    public MediaErrorDto? Error { get; init; }
    public string TraceId { get; init; } = string.Empty;
}

public sealed record MediaErrorDto
{
    public string Code { get; init; } = string.Empty;
    public string MessageKey { get; init; } = string.Empty;
}

public sealed record InviteScreenShareRequest
{
    public string ConversationNId { get; init; } = string.Empty;
    public string Direction { get; init; } = "ShareMine";
    public string RequestNId { get; init; } = string.Empty;
}

public sealed record RespondScreenShareRequest
{
    public string SessionNId { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long ExpectedVersion { get; init; }
}

public sealed record EndScreenShareRequest
{
    public string SessionNId { get; init; } = string.Empty;
    public string Reason { get; init; } = "BrowserStopped";
}

public sealed record InviteVoiceCallRequest
{
    public string ConversationNId { get; init; } = string.Empty;
    public string RequestNId { get; init; } = string.Empty;
}

public sealed record RespondVoiceCallRequest
{
    public string CallNId { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long ExpectedVersion { get; init; }
}

public sealed record EndVoiceCallRequest
{
    public string CallNId { get; init; } = string.Empty;
    public string Reason { get; init; } = "HungUp";
}

public sealed record GetConversationMediaRequest
{
    public string ConversationNId { get; init; } = string.Empty;
}

public sealed record GetMyActiveMediaRequest
{
    public string? Cursor { get; init; }
}

public sealed record BindMediaRequest
{
    public string ConversationNId { get; init; } = string.Empty;
    public string? ScreenSessionNId { get; init; }
    public string? VoiceCallNId { get; init; }
}

public sealed record SignalMediaRequest
{
    public string MediaContextNId { get; init; } = string.Empty;
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long ContextRevision { get; init; }
    public string NegotiationNId { get; init; } = string.Empty;
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Sequence { get; init; }
    public string Kind { get; init; } = string.Empty;
    public MediaDescriptionDto? Description { get; init; }
    public MediaCandidateDto? Candidate { get; init; }
}

public sealed record MediaDescriptionDto
{
    public string Type { get; init; } = string.Empty;
    public string Sdp { get; init; } = string.Empty;
}

public sealed record MediaCandidateDto
{
    public string Candidate { get; init; } = string.Empty;
    public string? SdpMid { get; init; }
    public int? SdpMLineIndex { get; init; }
    public string? UsernameFragment { get; init; }
}

public sealed record MediaReadyRequest
{
    public string MediaContextNId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string SessionNId { get; init; } = string.Empty;
}

public sealed record KeepAliveMediaRequest
{
    public string MediaContextNId { get; init; } = string.Empty;
    public string? ScreenSessionNId { get; init; }
    public string? VoiceCallNId { get; init; }
}

public sealed record SetVoiceMutedRequest
{
    public string CallNId { get; init; } = string.Empty;
    public bool Muted { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Sequence { get; init; }
}

public sealed record ReportMediaStoppedRequest
{
    public string ConversationNId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string SessionNId { get; init; } = string.Empty;
    public bool SenderDetached { get; init; }
    public bool CaptureTracksEnded { get; init; }
    public bool PlaybackDetached { get; init; }
}

public sealed record EndAllMediaRequest
{
    public string MediaContextNId { get; init; } = string.Empty;
    public string? ScreenSessionNId { get; init; }
    public string? VoiceCallNId { get; init; }
}

public sealed record ScreenDto
{
    public string SessionNId { get; init; } = string.Empty;
    public string ConversationNId { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string InitiatorUserNId { get; init; } = string.Empty;
    public string InviteeUserNId { get; init; } = string.Empty;
    public string SharerUserNId { get; init; } = string.Empty;
    public string ViewerUserNId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string? Answer { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Version { get; init; }
    public DateTimeOffset DeadlineOn { get; init; }
    public DateTimeOffset? StartedOn { get; init; }
    public DateTimeOffset? EndedOn { get; init; }
    public string? EndReason { get; init; }
    public bool InitiatorStoppedReported { get; init; }
    public bool InviteeStoppedReported { get; init; }
}

public sealed record VoiceDto
{
    public string CallNId { get; init; } = string.Empty;
    public string ConversationNId { get; init; } = string.Empty;
    public string CallerUserNId { get; init; } = string.Empty;
    public string CalleeUserNId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string? Answer { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Version { get; init; }
    public DateTimeOffset DeadlineOn { get; init; }
    public DateTimeOffset? StartedOn { get; init; }
    public DateTimeOffset? EndedOn { get; init; }
    public string? EndReason { get; init; }
    public bool CallerStoppedReported { get; init; }
    public bool CalleeStoppedReported { get; init; }
}

public sealed record ConversationMediaDto
{
    public ScreenDto? Screen { get; init; }
    public VoiceDto? Voice { get; init; }
    public bool MyEndpointSelected { get; init; }
    public string? MediaContextNId { get; init; }
}

public sealed record ActiveMediaItemDto
{
    public string ConversationNId { get; init; } = string.Empty;
    public ScreenDto? Screen { get; init; }
    public VoiceDto? Voice { get; init; }
    public bool MyEndpointSelected { get; init; }
    public string? MediaContextNId { get; init; }
}

public sealed record ActiveMediaPageDto
{
    public IReadOnlyList<ActiveMediaItemDto> Items { get; init; } = [];
    public string? NextCursor { get; init; }
}

public sealed record MediaSignalResultDto
{
    public string Status { get; init; } = "Accepted";
    [JsonIgnore]
    public string? TargetConnectionId { get; init; }
}

public sealed record MediaBindingDto
{
    public string MediaContextNId { get; init; } = string.Empty;
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long ContextRevision { get; init; }
    public string EndpointRole { get; init; } = string.Empty;
    public bool Polite { get; init; }
    public ScreenDto? Screen { get; init; }
    public VoiceDto? Voice { get; init; }
    public string IcePolicy { get; init; } = "All";
    public IReadOnlyList<IceServerDto> IceServers { get; init; } = [];
    public MediaConfirmationPairDto Confirm { get; init; } = new();
}

public sealed record IceServerDto
{
    public IReadOnlyList<string> Urls { get; init; } = [];
    public string? Username { get; init; }
    public string? Credential { get; init; }
}

public sealed record MediaConfirmationDto
{
    public string SessionNId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Version { get; init; }
    public int ValidForMs { get; init; }
    public bool Allowed { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record MediaConfirmationPairDto
{
    public MediaConfirmationDto? Screen { get; init; }
    public MediaConfirmationDto? Voice { get; init; }
}

public sealed record MediaContextChangedDto
{
    public string MediaContextNId { get; init; } = string.Empty;
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long ContextRevision { get; init; }
    public string? ScreenSessionNId { get; init; }
    public string? VoiceCallNId { get; init; }
}

public sealed record VoiceMutedDto
{
    public string CallNId { get; init; } = string.Empty;
    public string UserNId { get; init; } = string.Empty;
    public bool Muted { get; init; }
    [JsonConverter(typeof(CollaborationInt64StringConverter))]
    public long Sequence { get; init; }
    [JsonIgnore]
    public string? TargetConnectionId { get; init; }
}

public sealed record MediaEventEnvelope<T>
{
    public int ContractVersion { get; init; } = 1;
    public string EventNId { get; init; } = string.Empty;
    public DateTimeOffset OccurredOn { get; init; }
    public T Payload { get; init; } = default!;
}
