namespace IndustrialPlatform.SystemData.Application.Reliability;
public sealed record ControlPlaneEvent(Guid EventId, string EventType, string EventVersion, string TenantNId, string Payload, DateTimeOffset CreatedOn);
public sealed record OutboxEventStatus(Guid EventId, int RetryCount, DateTimeOffset? PublishedOn, string? LastError, DateTimeOffset? NextAttemptOn = null, bool DeadLetter = false);

public interface IControlPlaneOutbox
{
    Task EnqueueAsync(ControlPlaneEvent item, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ControlPlaneEvent>> GetPendingAsync(int limit, CancellationToken cancellationToken);
    Task MarkPublishedAsync(Guid eventId, CancellationToken cancellationToken);
    Task<bool> RecordFailureAsync(Guid eventId, string failureMessage, CancellationToken cancellationToken);
    Task<OutboxEventStatus?> GetStatusAsync(Guid eventId, CancellationToken cancellationToken);
}
