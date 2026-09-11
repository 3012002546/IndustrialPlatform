namespace IndustrialPlatform.EventBus.Abstractions;

/// <summary>
/// Marks a consumer failure that must use the dedicated delayed retry queue instead
/// of consuming the bounded business-failure retry budget.
/// </summary>
public interface IEventBusDeferredRetryFailure
{
}
