using IndustrialPlatform.SystemData.Application.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

public sealed partial class NotificationExpiryHostedService : BackgroundService
{
    private readonly INotificationStore _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<NotificationExpiryHostedService> _logger;

    public NotificationExpiryHostedService(INotificationStore store, TimeProvider clock, ILogger<NotificationExpiryHostedService> logger)
    {
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await _store.ExpireAsync(_clock.GetUtcNow(), stoppingToken); }
            catch (Exception exception) when (exception is not OperationCanceledException) { LogExpiryFailed(_logger, exception); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    [LoggerMessage(EventId = 405, Level = LogLevel.Warning, Message = "Notification expiry sweep failed.")]
    private static partial void LogExpiryFailed(ILogger logger, Exception exception);
}
