using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Events;

namespace IndustrialPlatform.EventBus.Subscriptions;

/// <summary>
/// 事件订阅管理器,以事件类型名为键维护订阅关系。线程安全。
/// </summary>
public sealed class EventBusSubscriptionsManager : IEventBusSubscriptionsManager
{
    private readonly Dictionary<string, List<Type>> _consumers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _eventTypes = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();

    /// <inheritdoc/>
    public bool IsEmpty
    {
        get
        {
            lock (_syncRoot)
            {
                return _consumers.Count == 0;
            }
        }
    }

    /// <inheritdoc/>
    public void AddSubscription<TEvent, TConsumer>()
        where TEvent : IntegrationEvent
        where TConsumer : IIntegrationEventConsumer<TEvent>
    {
        var eventName = typeof(TEvent).Name;
        var consumerType = typeof(TConsumer);

        lock (_syncRoot)
        {
            if (!_consumers.TryGetValue(eventName, out var consumers))
            {
                consumers = [];
                _consumers.Add(eventName, consumers);
                _eventTypes.Add(eventName, typeof(TEvent));
            }

            if (!consumers.Contains(consumerType))
            {
                consumers.Add(consumerType);
            }
        }
    }

    /// <inheritdoc/>
    public bool HasSubscriptionsForEvent<TEvent>()
        where TEvent : IntegrationEvent
        => HasSubscriptionsForEvent(typeof(TEvent).Name);

    /// <inheritdoc/>
    public bool HasSubscriptionsForEvent(string eventName)
    {
        lock (_syncRoot)
        {
            return _consumers.ContainsKey(eventName);
        }
    }

    /// <inheritdoc/>
    public Type GetEventTypeByName(string eventName)
    {
        lock (_syncRoot)
        {
            return _eventTypes[eventName];
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<Type> GetConsumersForEvent<TEvent>()
        where TEvent : IntegrationEvent
        => GetConsumersForEvent(typeof(TEvent).Name);

    /// <inheritdoc/>
    public IReadOnlyCollection<Type> GetConsumersForEvent(string eventName)
    {
        lock (_syncRoot)
        {
            return _consumers.TryGetValue(eventName, out var consumers)
                ? [.. consumers]
                : [];
        }
    }
}
