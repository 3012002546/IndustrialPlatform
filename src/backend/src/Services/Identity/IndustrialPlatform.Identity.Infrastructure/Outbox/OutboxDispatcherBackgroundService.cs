using System.Text;
using IndustrialPlatform.EventBus.Connection;
using IndustrialPlatform.EventBus.Options;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace IndustrialPlatform.Identity.Infrastructure.Outbox;

/// <summary>
/// Outbox 后台发布器(§20):周期扫描未发布事件,把存储的 payload 原样转发到 RabbitMQ Topic 交换器。
/// 成功标记 <see cref="OutboxTable.PublishedOn"/>(发布至少一次);单条失败重试计数并记录错误,
/// 超过上限后不再扫描(运维排查);RabbitMQ 不可达时连接失败按指数退避等待,不消耗重试次数。
/// 消息以 event_type 作为路由键与 Type 头,event_id 作为 MessageId,消费者按 event_id 去重。
/// </summary>
public sealed partial class OutboxDispatcherBackgroundService : BackgroundService
{
    private readonly SqlSugarDbContext _dbContext;
    private readonly IRabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OutboxDispatcherBackgroundService> _logger;

    /// <summary>单批最多派发条数,防止长尾积压时单轮阻塞过久。</summary>
    private const int BatchSize = 50;

    /// <summary>单条事件最大发布尝试次数,超过后不再扫描(留待运维处置)。</summary>
    private const int MaxRetryCount = 10;

    /// <summary>轮询基础间隔。</summary>
    private static readonly TimeSpan BaseInterval = TimeSpan.FromSeconds(5);

    /// <summary>连接失败指数退避上限。</summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(1);

    /// <summary>初始化 Outbox 后台发布器。</summary>
    public OutboxDispatcherBackgroundService(
        SqlSugarDbContext dbContext,
        IRabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        ILogger<OutboxDispatcherBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _dbContext = dbContext;
        _connection = connection;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                consecutiveFailures++;
                LogDispatchBatchFailed(_logger, consecutiveFailures, exception);
            }

            try
            {
                await Task.Delay(BackoffDelay(consecutiveFailures), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>扫描一批待发布事件并逐条派发;连接级失败向上抛出由外层退避处理。</summary>
    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var pending = await _dbContext.SqlSugar.Queryable<OutboxTable>()
            .Where(o => o.PublishedOn == null && o.RetryCount < MaxRetryCount)
            .OrderBy(o => o.EventCreatedTime)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        var channel = await _connection.CreateChannelAsync(cancellationToken);
        try
        {
            await channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic, true, false, null, false, cancellationToken);
            foreach (var row in pending)
            {
                await DispatchRowAsync(channel, row, cancellationToken);
            }
        }
        finally
        {
            channel.Dispose();
        }
    }

    /// <summary>把单条事件原样发布;通道失效视为连接级失败向上抛出,其余按单条失败重试。</summary>
    private async Task DispatchRowAsync(IChannel channel, OutboxTable row, CancellationToken cancellationToken)
    {
        try
        {
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                Type = row.EventType,
                MessageId = row.EventId.ToString(),
                DeliveryMode = DeliveryModes.Persistent,
            };
            var body = Encoding.UTF8.GetBytes(row.Payload);
            await channel.BasicPublishAsync(_options.ExchangeName, row.EventType, false, properties, body, cancellationToken);

            await MarkPublishedAsync(row, cancellationToken);
            LogDispatched(_logger, row.EventType, row.EventId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (channel.IsOpen)
            {
                await RecordFailureAsync(row, exception, cancellationToken);
                LogDispatchRowFailed(_logger, row.EventType, row.EventId, exception);
            }
            else
            {
                // 通道已失效:视为连接级失败,本批中止,不消耗该事件的重试次数。
                throw;
            }
        }
    }

    /// <summary>发布成功后标记 PublishedOn(幂等,重复发布由消费者按 event_id 去重)。</summary>
    private async Task MarkPublishedAsync(OutboxTable row, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Updateable<OutboxTable>()
            .SetColumns(o => new OutboxTable { PublishedOn = DateTimeOffset.UtcNow })
            .Where(o => o.EventId == row.EventId)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>单条发布失败:重试计数 +1 并记录最后错误。</summary>
    private async Task RecordFailureAsync(OutboxTable row, Exception exception, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Updateable<OutboxTable>()
            .SetColumns(o => new OutboxTable
            {
                RetryCount = o.RetryCount + 1,
                LastError = exception.Message,
            })
            .Where(o => o.EventId == row.EventId)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>连接失败指数退避(2^min(连续失败,4) × 基础间隔,封顶 1 分钟)。</summary>
    private static TimeSpan BackoffDelay(int consecutiveFailures)
    {
        var multiplier = 1 << Math.Min(consecutiveFailures, 4);
        var delay = TimeSpan.FromMilliseconds(BaseInterval.TotalMilliseconds * multiplier);
        return delay > MaxBackoff ? MaxBackoff : delay;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Outbox 批次发布失败(连续 {ConsecutiveFailures} 次),事件保持待发布,指数退避后重试。")]
    private static partial void LogDispatchBatchFailed(ILogger logger, int consecutiveFailures, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Outbox 事件 {EventType}/{EventId} 发布失败,重试计数 +1。")]
    private static partial void LogDispatchRowFailed(ILogger logger, string eventType, Guid eventId, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "已发布 Outbox 事件 {EventType}/{EventId}。")]
    private static partial void LogDispatched(ILogger logger, string eventType, Guid eventId);
}
