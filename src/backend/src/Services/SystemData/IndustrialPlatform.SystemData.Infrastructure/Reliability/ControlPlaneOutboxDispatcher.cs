using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Events;
using IndustrialPlatform.SystemData.Application.Reliability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

public sealed partial class ControlPlaneOutboxDispatcher : BackgroundService
{
    private readonly IControlPlaneOutbox _outbox;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ControlPlaneOutboxDispatcher> _logger;
    public ControlPlaneOutboxDispatcher(IControlPlaneOutbox outbox, IEventBus eventBus, ILogger<ControlPlaneOutboxDispatcher> logger)
    {
        _outbox = outbox; _eventBus = eventBus; _logger = logger;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyCollection<ControlPlaneEvent> pending;
            try
            {
                pending = await _outbox.GetPendingAsync(50, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPendingReadFailed(_logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }
            foreach (var @event in pending)
            {
                try
                {
                    await _eventBus.PublishAsync(new ControlPlaneIntegrationEvent(@event), @event.EventType, stoppingToken);
                    await _outbox.MarkPublishedAsync(@event.EventId, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var dead = await _outbox.RecordFailureAsync(@event.EventId, ex.Message, stoppingToken);
                    SystemDataMetrics.OutboxRetries.Add(1);
                    if (dead) SystemDataMetrics.OutboxDeadLetters.Add(1);
                    LogPublishFailed(_logger, ex, @event.EventId);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "SystemData Outbox event {EventId} publish failed; retry will follow.")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, Guid eventId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "SystemData Outbox pending scan unavailable; retrying after initialization.")]
    private static partial void LogPendingReadFailed(ILogger logger, Exception exception);
}

public sealed class ControlPlaneIntegrationEvent : IntegrationEvent
{
    public ControlPlaneIntegrationEvent(ControlPlaneEvent source)
    {
        EventId = source.EventId; CreatedTime = source.CreatedOn; TenantNId = source.TenantNId; Payload = source.Payload; EventVersion = source.EventVersion; EventName = source.EventType;
    }
    public string TenantNId { get; }
    public string Payload { get; }
    public string EventVersion { get; }
    public string EventName { get; }
    public override string EventType => EventName;
}
