using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Notifications;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;

public sealed class NotificationTargetValidator : INotificationTargetValidator
{
    private readonly IControlPlaneStore _store;

    public NotificationTargetValidator(IControlPlaneStore store) => _store = store;

    public async Task<bool> IsAllowedAsync(string tenantNId, string? resourceNId, string? targetRoute, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resourceNId)) return true;
        try
        {
            var snapshot = await _store.LoadAsync(tenantNId, cancellationToken);
            var resource = snapshot.Resources.FirstOrDefault(value => string.Equals(value.NId, resourceNId.Trim(), StringComparison.OrdinalIgnoreCase));
            return resource is not null
                && resource.Status == IndustrialPlatform.SystemData.Domain.ControlPlane.UiResourceStatus.Active;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }
}
