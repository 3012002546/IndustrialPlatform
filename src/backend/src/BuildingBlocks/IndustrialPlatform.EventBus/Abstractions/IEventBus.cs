using IndustrialPlatform.EventBus.Events;

namespace IndustrialPlatform.EventBus.Abstractions;

/// <summary>
/// 事件总线接口,负责集成事件的发布。
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// 发布集成事件到消息总线。
    /// </summary>
    /// <typeparam name="TEvent">事件类型。</typeparam>
    /// <param name="integrationEvent">事件实例。</param>
    /// <param name="routingKey">路由键,默认使用事件类型名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PublishAsync<TEvent>(TEvent integrationEvent, string? routingKey = null, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent;
}
