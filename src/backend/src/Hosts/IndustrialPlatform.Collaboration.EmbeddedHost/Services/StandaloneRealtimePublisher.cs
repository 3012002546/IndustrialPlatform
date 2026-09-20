using IndustrialPlatform.Collaboration.Api.Hubs;
using IndustrialPlatform.Collaboration.Application;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Services;

/// <summary>Delivers the durable collaboration outbox directly to this host's SignalR clients.</summary>
public sealed class StandaloneRealtimePublisher(CollaborationRealtimePublisher publisher) : ICollaborationRealtimePublisher
{
    public Task PublishAsync(CollaborationOutboxRecord message, CancellationToken cancellationToken) =>
        publisher.PublishLocalAsync(message, cancellationToken);
}
