using IndustrialPlatform.EventBus.Events;

namespace IndustrialPlatform.EventBus.Abstractions;

/// <summary>
/// 集成事件消费者接口,由各服务实现并注册到事件总线。
/// </summary>
/// <typeparam name="TEvent">事件类型。</typeparam>
public interface IIntegrationEventConsumer<in TEvent>
    where TEvent : IntegrationEvent
{
    /// <summary>
    /// 处理集成事件。消费者完成业务后返回,由事件总线确认消息。
    /// </summary>
    /// <param name="integrationEvent">事件实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}
