using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Events;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

/// <summary>把与本地审计事实同事务写入的 outbox 可靠投递到集成事件总线。</summary>
public sealed partial class AuditOutboxDispatcher : BackgroundService
{
    private const int MaxRetries = 10;
    private readonly SqlSugarDbContext _dbContext;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AuditOutboxDispatcher> _logger;

    public AuditOutboxDispatcher(SqlSugarDbContext dbContext, IEventBus eventBus, ILogger<AuditOutboxDispatcher> logger)
    {
        _dbContext = dbContext;
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
                var now = DateTimeOffset.UtcNow;
                var rows = await _dbContext.SqlSugar.Queryable<AuditOutboxTable>()
                    .Where(t => t.PublishedOn == null && t.DeadOn == null && t.RetryCount < MaxRetries && (t.NextAttemptOn == null || t.NextAttemptOn <= now))
                    .OrderBy(t => t.CreatedOn, OrderByType.Asc)
                    .Take(50)
                    .ToListAsync(stoppingToken);
                foreach (var row in rows) await DispatchAsync(row, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { LogScanFailed(_logger, exception); }
        }
    }

    private async Task DispatchAsync(AuditOutboxTable row, CancellationToken cancellationToken)
    {
        try
        {
            await _eventBus.PublishAsync(new AuditOutboxIntegrationEvent(row.EventId, row.TenantNId, row.Payload, row.CreatedOn), "SystemData.AuditFactRecorded.v1", cancellationToken);
            await _dbContext.SqlSugar.Updateable<AuditOutboxTable>()
                .SetColumns(t => new AuditOutboxTable { PublishedOn = DateTimeOffset.UtcNow, LastError = null })
                .Where(t => t.Id == row.Id && t.PublishedOn == null && t.DeadOn == null)
                .ExecuteCommandAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var retry = row.RetryCount + 1;
            var dead = retry >= MaxRetries;
            var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Min(retry, 8))));
            await _dbContext.SqlSugar.Updateable<AuditOutboxTable>()
                .SetColumns(t => new AuditOutboxTable
                {
                    RetryCount = retry,
                    LastError = exception.Message.Length > 1000 ? exception.Message.Substring(0, 1000) : exception.Message,
                    NextAttemptOn = dead ? null : DateTimeOffset.UtcNow.Add(delay),
                    DeadOn = dead ? DateTimeOffset.UtcNow : null,
                })
                .Where(t => t.Id == row.Id && t.PublishedOn == null && t.DeadOn == null)
                .ExecuteCommandAsync(cancellationToken);
            LogPublishFailed(_logger, exception, row.EventId, retry, dead);
        }
    }

    [LoggerMessage(EventId = 403, Level = LogLevel.Warning, Message = "审计 outbox 扫描失败。")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 404, Level = LogLevel.Warning, Message = "审计 outbox 事件 {EventId} 投递失败，第 {Retry} 次；dead={Dead}。")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, Guid eventId, int retry, bool dead);
}

public sealed class AuditOutboxIntegrationEvent : IntegrationEvent
{
    public AuditOutboxIntegrationEvent(Guid eventId, string tenantNId, string payload, DateTimeOffset createdOn)
    {
        EventId = eventId;
        CreatedTime = createdOn;
        TenantNId = tenantNId;
        Payload = payload;
    }

    public string TenantNId { get; }
    public string Payload { get; }
    public override string EventType => "SystemData.AuditFactRecorded.v1";
}
