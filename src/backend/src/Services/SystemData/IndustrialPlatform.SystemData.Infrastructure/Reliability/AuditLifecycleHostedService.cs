using IndustrialPlatform.SystemData.Application.Auditing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

/// <summary>周期性清理已删除或已到保留期限的审计事实。</summary>
public sealed partial class AuditLifecycleHostedService : BackgroundService
{
    private readonly IAuditStore _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuditLifecycleHostedService> _logger;

    public AuditLifecycleHostedService(IAuditStore store, TimeProvider clock, ILogger<AuditLifecycleHostedService> logger)
    {
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await _store.CleanupAsync(_clock.GetUtcNow(), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { LogCleanupFailed(_logger, exception); }
        }
    }

    [LoggerMessage(EventId = 402, Level = LogLevel.Warning, Message = "审计生命周期清理失败。")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
