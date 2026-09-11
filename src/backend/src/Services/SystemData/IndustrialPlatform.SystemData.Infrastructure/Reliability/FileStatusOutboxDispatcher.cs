using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Events;
using IndustrialPlatform.SystemData.Application.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

/// <summary>
/// 可靠投递文件状态变化。文件数据库写入与 outbox 入队在同一事务内，只有 broker
/// publish 成功后才会标记 published；失败记录重试或 dead-letter 状态，避免丢失状态变化。
/// </summary>
public sealed partial class FileStatusOutboxDispatcher : BackgroundService
{
    private const int MaxRetries = 10;
    private readonly IFileStatusOutbox _outbox;
    private readonly IEventBus _eventBus;
    private readonly ILogger<FileStatusOutboxDispatcher> _logger;

    public FileStatusOutboxDispatcher(IFileStatusOutbox outbox, IEventBus eventBus, ILogger<FileStatusOutboxDispatcher> logger)
    {
        _outbox = outbox;
        _eventBus = eventBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { LogScanFailed(_logger, exception); }
        }
    }

    public async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in await _outbox.GetPendingAsync(now, 100, cancellationToken))
        {
            try
            {
                await _eventBus.PublishAsync(new FileStatusChangedIntegrationEvent
                {
                    EventId = item.EventId,
                    CreatedTime = item.ObservedOn,
                    TenantNId = item.TenantNId,
                    FileNId = item.FileNId,
                    ScanStatus = item.ScanStatus,
                    Restricted = item.Restricted,
                    DeletionStatus = item.DeletionStatus,
                    StateVersion = null,
                    ObservedOn = item.ObservedOn,
                }, "systemdata.file.status-changed.v1", cancellationToken);
                await _outbox.MarkPublishedAsync(item.EventId, DateTimeOffset.UtcNow, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var retry = item.RetryCount + 1;
                var deadLetter = retry >= MaxRetries;
                var nextAttemptOn = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(retry, 8))));
                await _outbox.RecordFailureAsync(item.EventId, retry, exception.Message, deadLetter, deadLetter ? DateTimeOffset.UtcNow : nextAttemptOn, cancellationToken);
                LogPublishFailed(_logger, exception, item.EventId, retry, deadLetter);
            }
        }
    }

    [LoggerMessage(EventId = 405, Level = LogLevel.Warning, Message = "文件状态 outbox 扫描失败。")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 406, Level = LogLevel.Warning, Message = "文件状态 outbox 事件 {EventId} 投递失败，第 {Retry} 次；dead={Dead}。")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, Guid eventId, int retry, bool dead);
}
