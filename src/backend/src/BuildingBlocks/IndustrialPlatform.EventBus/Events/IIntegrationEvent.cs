namespace IndustrialPlatform.EventBus.Events;

/// <summary>
/// 集成事件接口,跨微服务通过消息总线传播。
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>事件唯一标识,用于消费幂等。</summary>
    Guid EventId { get; }

    /// <summary>事件发生时间。</summary>
    DateTimeOffset CreatedTime { get; }

    /// <summary>事件类型名,反序列化时定位事件类型。</summary>
    string EventType { get; }

    /// <summary>链路追踪标识,贯穿发布与消费两侧。</summary>
    string? TraceId { get; }
}
