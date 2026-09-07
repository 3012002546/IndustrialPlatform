using IndustrialPlatform.SystemData.Application.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace IndustrialPlatform.SystemData.Api.Notifications;

public sealed class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenant = Context.User?.FindFirst("tenant_n_id")?.Value;
        var user = Context.User?.FindFirst("sub")?.Value ?? Context.User?.FindFirst("user_n_id")?.Value;
        if (!string.IsNullOrWhiteSpace(tenant) && !string.IsNullOrWhiteSpace(user))
            await Groups.AddToGroupAsync(Context.ConnectionId, SignalRNotificationNotifier.Group(tenant, user));
        await base.OnConnectedAsync();
    }
}

public sealed class SignalRNotificationNotifier : INotificationNotifier
{
    private readonly IHubContext<NotificationHub> _hub;

    public SignalRNotificationNotifier(IHubContext<NotificationHub> hub) => _hub = hub;

    public Task NotifyAsync(string tenantNId, IReadOnlyCollection<string> recipientUserNIds, CancellationToken cancellationToken) =>
        Task.WhenAll(recipientUserNIds.Select(user => _hub.Clients.Group(Group(tenantNId, user)).SendAsync("notification.changed", cancellationToken)));

    public static string Group(string tenantNId, string userNId) => $"systemdata:notification:{tenantNId}:{userNId}";
}
