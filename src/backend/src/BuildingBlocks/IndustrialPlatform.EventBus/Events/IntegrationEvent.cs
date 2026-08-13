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
    /// <remarks>虚成员:派生事件可 override 携带版本化线上名称(如 Identity.UserCreated.v1)。
    /// 经基类引用(发布路由/序列化)访问时按运行时类型派发到最新 override,避免非虚+new 遮蔽
    /// 在 STJ 序列化与路由键上回落到 <see cref="GetType()"/> 类名。</remarks>
    public virtual string EventType => GetType().Name;

    /// <inheritdoc/>
    public string? TraceId { get; init; }
}
