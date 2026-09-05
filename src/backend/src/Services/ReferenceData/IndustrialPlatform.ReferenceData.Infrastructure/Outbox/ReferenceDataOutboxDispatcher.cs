using System.Text;
using IndustrialPlatform.EventBus.Connection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Outbox;

public sealed partial class ReferenceDataOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IRabbitMqConnection connection,
    TimeProvider timeProvider,
    ILogger<ReferenceDataOutboxDispatcher> logger) : BackgroundService
{
    private const string ExchangeName = "industrial.system";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogScanFailed(logger, exception);
            }

            try
            {
                await Task.Delay(PollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ReferenceDataOutboxStore>();
        var now = timeProvider.GetUtcNow();
        var pending = await store.GetPendingAsync(now, 100, cancellationToken);
        ReferenceDataMetrics.SetPending(await store.CountPendingByModuleAsync(cancellationToken));
        if (pending.Count == 0)
            return;

        IChannel channel;
        try
        {
            channel = await CreatePublisherChannelAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            foreach (var item in pending)
                await RecordFailureAsync(store, item, exception, cancellationToken);
            ReferenceDataMetrics.SetPending(await store.CountPendingByModuleAsync(cancellationToken));
            return;
        }

        try
        {
            foreach (var item in pending)
                await DispatchAsync(store, channel, item, cancellationToken);
        }
        finally
        {
            channel.Dispose();
        }
        ReferenceDataMetrics.SetPending(await store.CountPendingByModuleAsync(cancellationToken));
    }

    private async Task<IChannel> CreatePublisherChannelAsync(CancellationToken cancellationToken)
    {
        var channel = await connection.CreateChannelAsync(
            publisherConfirmationsEnabled: true, cancellationToken);
        try
        {
            await channel.ExchangeDeclareAsync(
                ExchangeName, ExchangeType.Topic, true, false, null, false, cancellationToken);
            return channel;
        }
        catch
        {
            channel.Dispose();
            throw;
        }
    }

    private async Task DispatchAsync(ReferenceDataOutboxStore store, IChannel channel,
        ReferenceDataPendingEvent item,
        CancellationToken cancellationToken)
    {
        try
        {
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                Type = item.EventName,
                MessageId = item.EventId.ToString(),
                DeliveryMode = DeliveryModes.Persistent,
            };
            await channel.BasicPublishAsync(
                ExchangeName, item.EventName, mandatory: false, properties,
                Encoding.UTF8.GetBytes(item.Payload), cancellationToken);
            if (await store.MarkPublishedAsync(item.EventId, timeProvider.GetUtcNow(), cancellationToken))
                ReferenceDataMetrics.OutboxPublished.Add(1,
                    new KeyValuePair<string, object?>("module", item.ModuleKey));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(store, item, exception, cancellationToken);
        }
    }

    private async Task RecordFailureAsync(ReferenceDataOutboxStore store, ReferenceDataPendingEvent item,
        Exception exception, CancellationToken cancellationToken)
    {
        var outcome = await store.RecordFailureAsync(item.EventId, item.AttemptCount,
            exception.Message, timeProvider.GetUtcNow(), cancellationToken);
        if (!outcome.Updated) return;
        ReferenceDataMetrics.OutboxRetries.Add(1,
            new KeyValuePair<string, object?>("module", item.ModuleKey));
        if (outcome.Terminal)
        {
            ReferenceDataMetrics.OutboxFailed.Add(1,
                new KeyValuePair<string, object?>("module", item.ModuleKey));
            LogTerminalFailure(logger, item.EventId, item.ModuleKey, exception);
        }
        else
        {
            LogPublishFailure(logger, item.EventId, item.ModuleKey, exception);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "ReferenceData Outbox scan or connection failed; messages remain pending.")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "ReferenceData Outbox event {EventId} for {Module} failed and will be retried.")]
    private static partial void LogPublishFailure(ILogger logger, Guid eventId, string module,
        Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error,
        Message = "ReferenceData Outbox event {EventId} for {Module} reached ten failed attempts and was retained.")]
    private static partial void LogTerminalFailure(ILogger logger, Guid eventId, string module,
        Exception exception);
}
