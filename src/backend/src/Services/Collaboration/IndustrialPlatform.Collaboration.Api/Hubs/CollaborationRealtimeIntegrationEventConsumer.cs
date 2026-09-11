using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.EventBus.Abstractions;

namespace IndustrialPlatform.Collaboration.Api.Hubs;

/// <summary>Consumes cross-instance realtime events and broadcasts them through local SignalR.</summary>
public sealed class CollaborationRealtimeIntegrationEventConsumer : IIntegrationEventConsumer<CollaborationRealtimeIntegrationEvent>
{
    private readonly ICollaborationRepository _repository;
    private readonly CollaborationRealtimePublisher _publisher;

    public CollaborationRealtimeIntegrationEventConsumer(ICollaborationRepository repository, CollaborationRealtimePublisher publisher)
    {
        _repository = repository;
        _publisher = publisher;
    }

    public async Task HandleAsync(CollaborationRealtimeIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var claim = await _repository.TryClaimEventInboxAsync(integrationEvent.EventId, integrationEvent.TenantNId, integrationEvent.EventType, integrationEvent.CreatedTime, cancellationToken);
        if (claim.Status == EventInboxClaimStatus.AlreadyProcessed)
            return;
        if (claim.Status == EventInboxClaimStatus.Busy)
            throw new EventInboxBusyException("实时事件仍由其他 worker 处理，等待消息重投。");
        var leaseNId = claim.LeaseNId ?? throw new CollaborationException(500, "COLLAB_EVENT_INBOX_LEASE_MISSING", "实时事件处理租约缺失。");

        var message = new CollaborationOutboxRecord(
            integrationEvent.EventId,
            integrationEvent.TenantNId,
            integrationEvent.SourceEventType,
            integrationEvent.Payload,
            integrationEvent.CreatedTime,
            null,
            0,
            null);
        try
        {
            await _publisher.PublishLocalAsync(message, cancellationToken);
            if (!await _repository.MarkEventInboxProcessedAsync(integrationEvent.EventId, leaseNId, DateTimeOffset.UtcNow, cancellationToken))
                throw new CollaborationException(409, "COLLAB_EVENT_INBOX_LEASE_LOST", "实时事件处理租约已失效。");
        }
        catch (Exception exception)
        {
            await _repository.MarkEventInboxFailedAsync(integrationEvent.EventId, exception.Message, leaseNId, DateTimeOffset.UtcNow, cancellationToken);
            throw;
        }
    }
}
