using IndustrialPlatform.EventBus.Events;

namespace IndustrialPlatform.EventBus.Abstractions;

/// <summary>
/// 事件订阅管理接口,维护事件类型与消费者类型的映射。
/// </summary>
public interface IEventBusSubscriptionsManager
{
    /// <summary>是否没有任何订阅。</summary>
    bool IsEmpty { get; }

    /// <summary>
    /// 注册事件与消费者的订阅关系。
    /// </summary>
    /// <typeparam name="TEvent">事件类型。</typeparam>
    /// <typeparam name="TConsumer">消费者类型。</typeparam>
    void AddSubscription<TEvent, TConsumer>()
        where TEvent : IntegrationEvent
        where TConsumer : IIntegrationEventConsumer<TEvent>;

    /// <summary>是否存在指定事件的订阅。</summary>
    bool HasSubscriptionsForEvent<TEvent>()
        where TEvent : IntegrationEvent;

    /// <summary>是否存在指定事件名的订阅。</summary>
    bool HasSubscriptionsForEvent(string eventName);

    /// <summary>按事件名获取事件类型。</summary>
    Type GetEventTypeByName(string eventName);

    /// <summary>获取指定事件的全部消费者类型。</summary>
    IReadOnlyCollection<Type> GetConsumersForEvent<TEvent>()
        where TEvent : IntegrationEvent;

    /// <summary>按事件名获取全部消费者类型。</summary>
    IReadOnlyCollection<Type> GetConsumersForEvent(string eventName);
}
