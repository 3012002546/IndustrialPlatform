using IndustrialPlatform.Collaboration.Contracts;

namespace IndustrialPlatform.Collaboration.Application.RemoteAssistance;

public sealed record ScreenShareSessionRecord
{
    public Guid Id { get; init; }
    public string TenantNId { get; init; } = string.Empty;
    public string SessionNId { get; init; } = string.Empty;
    public string ConversationNId { get; init; } = string.Empty;
    public string InitiatorUserNId { get; init; } = string.Empty;
    public string InviteeUserNId { get; init; } = string.Empty;
    public string Direction { get; init; } = "ShareMine";
    public string SharerUserNId { get; init; } = string.Empty;
    public string ViewerUserNId { get; init; } = string.Empty;
    public string State { get; init; } = "Pending";
    public string? Answer { get; init; }
    public string RequestNId { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public DateTimeOffset DeadlineOn { get; init; }
    public DateTimeOffset? AcceptedOn { get; init; }
    public DateTimeOffset? StartedOn { get; init; }
    public DateTimeOffset? EndedOn { get; init; }
    public string? EndReason { get; init; }
    public string InitiatorConnectionId { get; init; } = string.Empty;
    public string? InviteeConnectionId { get; init; }
    public DateTimeOffset? InitiatorAliveUntil { get; init; }
    public DateTimeOffset? InviteeAliveUntil { get; init; }
    public DateTimeOffset? InitiatorStoppedReportedOn { get; init; }
    public DateTimeOffset? InviteeStoppedReportedOn { get; init; }
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset LastUpdatedOn { get; init; }
    public long Version { get; init; } = 1;
    public Guid ConcurrencyVersion { get; init; }
}

public sealed record VoiceCallSessionRecord
{
    public Guid Id { get; init; }
    public string TenantNId { get; init; } = string.Empty;
    public string CallNId { get; init; } = string.Empty;
    public string ConversationNId { get; init; } = string.Empty;
    public string CallerUserNId { get; init; } = string.Empty;
    public string CalleeUserNId { get; init; } = string.Empty;
    public string State { get; init; } = "Ringing";
    public string? Answer { get; init; }
    public string RequestNId { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public DateTimeOffset DeadlineOn { get; init; }
    public DateTimeOffset? AcceptedOn { get; init; }
    public DateTimeOffset? StartedOn { get; init; }
    public DateTimeOffset? EndedOn { get; init; }
    public string? EndReason { get; init; }
    public string CallerConnectionId { get; init; } = string.Empty;
    public string? CalleeConnectionId { get; init; }
    public DateTimeOffset? CallerAliveUntil { get; init; }
    public DateTimeOffset? CalleeAliveUntil { get; init; }
    public DateTimeOffset? CallerReadyOn { get; init; }
    public DateTimeOffset? CalleeReadyOn { get; init; }
    public DateTimeOffset? CallerStoppedReportedOn { get; init; }
    public DateTimeOffset? CalleeStoppedReportedOn { get; init; }
    public DateTimeOffset CreatedOn { get; init; }
    public DateTimeOffset LastUpdatedOn { get; init; }
    public long Version { get; init; } = 1;
    public Guid ConcurrencyVersion { get; init; }
}

public sealed record VoiceUserSlotRecord(
    string TenantNId,
    string UserNId,
    string VoiceCallNId,
    DateTimeOffset CreatedOn,
    DateTimeOffset ExpiresOn);

public sealed record EndAllMediaRecords(
    ScreenShareSessionRecord? Screen,
    VoiceCallSessionRecord? Voice);

public sealed record RemoteAssistanceEventPayload(
    int SchemaVersion,
    string Capability,
    string? SessionNId,
    string? CallNId,
    string ConversationNId,
    string? ActorUserNId,
    string ActorKind,
    string Action,
    string State,
    string Version,
    DateTimeOffset OccurredOn,
    string AuditEventNId,
    string AuditAction,
    string AuditObjectType,
    string AuditObjectNId,
    object AuditPayload);

public interface IRemoteAssistanceRepository
{
    Task<ScreenShareSessionRecord?> GetScreenAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken);
    Task<ScreenShareSessionRecord?> GetScreenByRequestAsync(string tenantNId, string initiatorUserNId, string requestNId, CancellationToken cancellationToken);
    Task<ScreenShareSessionRecord?> GetActiveScreenByConversationAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken);
    Task<ScreenShareSessionRecord?> GetLatestScreenAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken);
    Task<ScreenShareSessionRecord> CreateScreenAsync(ScreenShareSessionRecord session, object eventPayload, CancellationToken cancellationToken);
    Task<ScreenShareSessionRecord?> UpdateScreenAsync(ScreenShareSessionRecord session, long expectedVersion, object? eventPayload, CancellationToken cancellationToken);

    Task<VoiceCallSessionRecord?> GetVoiceAsync(string tenantNId, string callNId, CancellationToken cancellationToken);
    Task<VoiceCallSessionRecord?> GetVoiceByRequestAsync(string tenantNId, string callerUserNId, string requestNId, CancellationToken cancellationToken);
    Task<VoiceCallSessionRecord?> GetActiveVoiceByConversationAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken);
    Task<VoiceCallSessionRecord?> GetLatestVoiceAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken);
    Task<VoiceCallSessionRecord> CreateVoiceAsync(VoiceCallSessionRecord voiceCall, IReadOnlyList<VoiceUserSlotRecord> slots, object eventPayload, CancellationToken cancellationToken);
    Task<VoiceCallSessionRecord?> UpdateVoiceAsync(VoiceCallSessionRecord voiceCall, long expectedVersion, bool releaseSlots, object? eventPayload, CancellationToken cancellationToken);
    Task<EndAllMediaRecords?> EndAllAsync(
        ScreenShareSessionRecord? screen,
        long? screenExpectedVersion,
        object? screenEventPayload,
        VoiceCallSessionRecord? voice,
        long? voiceExpectedVersion,
        object? voiceEventPayload,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ScreenShareSessionRecord>> ListActiveScreensForUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VoiceCallSessionRecord>> ListActiveVoicesForUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScreenShareSessionRecord>> ListActiveScreensForLifecycleAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<VoiceCallSessionRecord>> ListActiveVoicesForLifecycleAsync(CancellationToken cancellationToken);
    Task UpdateVoiceSlotsExpiryAsync(string tenantNId, string callNId, DateTimeOffset expiresOn, CancellationToken cancellationToken);
    Task<int> DeleteExpiredVoiceSlotsAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IRemoteAssistancePermissionEvaluator
{
    Task<bool> HasPermissionAsync(
        string permission,
        string tenantNId,
        string userNId,
        string sessionNId,
        string securityVersion,
        CancellationToken cancellationToken);
}

public interface IRemoteAssistanceIceServerProvider
{
    Task<IReadOnlyList<IceServerDto>> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken);
}

public interface IRemoteAssistanceAuditPort
{
    Task WriteAsync(string tenantNId, string? actorUserNId, string action, string objectType, string objectNId, object payload, DateTimeOffset occurredOn, CancellationToken cancellationToken);
}
