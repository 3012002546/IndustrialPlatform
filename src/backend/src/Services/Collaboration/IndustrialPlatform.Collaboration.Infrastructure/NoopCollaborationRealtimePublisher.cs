using IndustrialPlatform.Collaboration.Application;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>
/// API replaces this registration with the SignalR publisher. Keeping a no-op
/// fallback lets embedded application tests exercise durable outbox handling.
/// </summary>
public sealed class NoopCollaborationRealtimePublisher : ICollaborationRealtimePublisher
{
    public Task PublishAsync(CollaborationOutboxRecord message, CancellationToken cancellationToken) => Task.CompletedTask;
}
