using IndustrialPlatform.SharedKernel.Events;

namespace IndustrialPlatform.SharedKernel.Entities;

/// <summary>
/// DDD 聚合根基类,持有领域事件集合。
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _events = [];

    /// <summary>已注册且尚未派发的领域事件。</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events;

    /// <summary>注册领域事件。</summary>
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _events.Add(domainEvent);
    }

    /// <summary>派发后清空领域事件。</summary>
    public void ClearDomainEvents()
    {
        _events.Clear();
    }
}
