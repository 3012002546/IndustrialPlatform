namespace IndustrialPlatform.SharedKernel.Events;

/// <summary>
/// 领域事件标记接口,事件发生时点由实现方提供。
/// </summary>
public interface IDomainEvent
{
    /// <summary>事件发生时间。</summary>
    DateTimeOffset OccurredOn { get; }
}
