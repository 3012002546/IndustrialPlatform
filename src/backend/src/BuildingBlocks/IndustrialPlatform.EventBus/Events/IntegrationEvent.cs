using System.Diagnostics;

namespace IndustrialPlatform.EventBus.Events;

/// <summary>
/// 集成事件基类。事件命名统一使用过去式,如 WorkOrderCreatedEvent。
/// </summary>
public abstract class IntegrationEvent : IIntegrationEvent
{
    protected IntegrationEvent()
    {
        EventId = Guid.NewGuid();
        CreatedTime = DateTimeOffset.UtcNow;
        TraceId = Activity.Current?.TraceId.ToString();
    }

    /// <inheritdoc/>
    public Guid EventId { get; init; }

    /// <inheritdoc/>
    public DateTimeOffset CreatedTime { get; init; }

    /// <inheritdoc/>
    public string EventType => GetType().Name;

    /// <inheritdoc/>
    public string? TraceId { get; init; }
}
