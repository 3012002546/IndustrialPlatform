using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections;
using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Connection;
using IndustrialPlatform.EventBus.Events;
using IndustrialPlatform.EventBus.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace IndustrialPlatform.EventBus.Consumer;

/// <summary>
/// 事件消费后台服务,启动后监听队列并将消息分发到注册的事件消费者。
/// 消费采用手动确认:业务成功后才 ACK,失败 NACK 不重入队(后续由重试队列/DLQ 兜底)。
/// </summary>
public sealed partial class EventBusConsumerBackgroundService : BackgroundService
{
    private readonly IRabbitMqConnection _connection;
    private readonly IEventBusSubscriptionsManager _subscriptionsManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<EventBusConsumerBackgroundService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private IChannel? _channel;

    /// <summary>
    /// 创建消费后台服务。
    /// </summary>
    public EventBusConsumerBackgroundService(
        IRabbitMqConnection connection,
        IEventBusSubscriptionsManager subscriptionsManager,
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<EventBusConsumerBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(subscriptionsManager);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _connection = connection;
        _subscriptionsManager = subscriptionsManager;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_subscriptionsManager.IsEmpty)
        {
            LogNoSubscriptions();
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            IChannel? channel = null;
            try
            {
                channel = await _connection.CreateChannelAsync(publisherConfirmationsEnabled: true, stoppingToken);
                _channel = channel;
                await channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic, true, false, null, false, stoppingToken);
                await channel.ExchangeDeclareAsync(_options.RetryExchangeName, ExchangeType.Topic, true, false, null, false, stoppingToken);
                await channel.ExchangeDeclareAsync(_options.BusyRetryExchangeName, ExchangeType.Topic, true, false, null, false, stoppingToken);
                await channel.ExchangeDeclareAsync(_options.DeadLetterExchangeName, ExchangeType.Direct, true, false, null, false, stoppingToken);
                await channel.QueueDeclareAsync(_options.RetryQueueName, true, false, false,
                    new Dictionary<string, object?>
                    {
                        ["x-message-ttl"] = Math.Max(100, _options.RetryDelayMilliseconds),
                        ["x-dead-letter-exchange"] = _options.ExchangeName,
                    }, false, stoppingToken);
                await channel.QueueBindAsync(_options.RetryQueueName, _options.RetryExchangeName, "#", null, false, stoppingToken);
                await channel.QueueDeclareAsync(_options.BusyRetryQueueName, true, false, false,
                    new Dictionary<string, object?>
                    {
                        ["x-message-ttl"] = Math.Max(100, _options.BusyRetryDelayMilliseconds),
                        ["x-dead-letter-exchange"] = _options.ExchangeName,
                    }, false, stoppingToken);
                await channel.QueueBindAsync(_options.BusyRetryQueueName, _options.BusyRetryExchangeName, "#", null, false, stoppingToken);
                await channel.QueueDeclareAsync(_options.DeadLetterQueueName, true, false, false, null, false, stoppingToken);
                await channel.QueueBindAsync(_options.DeadLetterQueueName, _options.DeadLetterExchangeName, _options.DeadLetterRoutingKey, null, false, stoppingToken);
                await channel.QueueDeclareAsync(_options.QueueName, true, false, false,
                    new Dictionary<string, object?>
                    {
                        ["x-dead-letter-exchange"] = _options.RetryExchangeName,
                    }, false, stoppingToken);
                await channel.QueueBindAsync(_options.QueueName, _options.ExchangeName, _options.RoutingPattern, null, false, stoppingToken);
                await channel.BasicQosAsync(0, _options.PrefetchCount, false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += OnMessageReceivedAsync;

                await channel.BasicConsumeAsync(_options.QueueName, false, consumer, stoppingToken);

                LogConsumerStarted(_options.QueueName, _options.ExchangeName, _options.RoutingPattern);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogConsumerUnavailable(exception);
            }
            finally
            {
                if (ReferenceEquals(_channel, channel))
                {
                    _channel = null;
                }

                channel?.Dispose();
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task OnMessageReceivedAsync(object? sender, BasicDeliverEventArgs args)
    {
        var eventName = args.BasicProperties.Type;
        if (string.IsNullOrEmpty(eventName) || !_subscriptionsManager.HasSubscriptionsForEvent(eventName))
        {
            await _channel!.BasicAckAsync(args.DeliveryTag, false, CancellationToken.None);
            return;
        }

        try
        {
            var eventType = _subscriptionsManager.GetEventTypeByName(eventName);
            if (JsonSerializer.Deserialize(args.Body.Span, eventType, _jsonOptions) is not IntegrationEvent integrationEvent)
            {
                await _channel!.BasicAckAsync(args.DeliveryTag, false, CancellationToken.None);
                return;
            }

            var consumerInterface = typeof(IIntegrationEventConsumer<>).MakeGenericType(eventType);
            var handleMethod = consumerInterface.GetMethod(nameof(IIntegrationEventConsumer<IntegrationEvent>.HandleAsync))
                ?? throw new MissingMethodException(consumerInterface.FullName, nameof(IIntegrationEventConsumer<IntegrationEvent>.HandleAsync));

            using var scope = _scopeFactory.CreateScope();
            foreach (var consumerType in _subscriptionsManager.GetConsumersForEvent(eventName))
            {
                var consumer = scope.ServiceProvider.GetRequiredService(consumerType);
                await (Task)handleMethod.Invoke(consumer, new object[] { integrationEvent, CancellationToken.None })!;
            }

            await _channel!.BasicAckAsync(args.DeliveryTag, false, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogHandleFailed(eventName, exception);
            if (exception is IEventBusDeferredRetryFailure)
            {
                await DeferForBusyRetryAsync(args);
                return;
            }

            var attempt = RabbitDeliveryPolicy.GetDeliveryAttempt(args.BasicProperties.Headers, _options.QueueName);
            var maxAttempts = Math.Max(1, _options.MaxDeliveryAttempts);
            if (attempt < maxAttempts)
            {
                // Rejecting without requeue routes the message to the retry exchange;
                // the retry queue's TTL dead-letters it back to the main exchange.
                await _channel!.BasicNackAsync(args.DeliveryTag, false, false, CancellationToken.None);
            }
            else
            {
                try
                {
                    var deadLetterProperties = new BasicProperties
                    {
                        ContentType = args.BasicProperties.ContentType,
                        Type = args.BasicProperties.Type,
                        MessageId = args.BasicProperties.MessageId,
                        DeliveryMode = args.BasicProperties.DeliveryMode,
                        Headers = args.BasicProperties.Headers is null ? null : new Dictionary<string, object?>(args.BasicProperties.Headers),
                    };
                    await _channel!.BasicPublishAsync(
                        _options.DeadLetterExchangeName,
                        _options.DeadLetterRoutingKey,
                        true,
                        deadLetterProperties,
                        args.Body,
                        CancellationToken.None);
                    await _channel!.BasicAckAsync(args.DeliveryTag, false, CancellationToken.None);
                }
                catch
                {
                    // Preserve the message when the DLQ publish itself is unavailable.
                    await _channel!.BasicNackAsync(args.DeliveryTag, false, true, CancellationToken.None);
                    throw;
                }
            }
        }
    }

    private async Task DeferForBusyRetryAsync(BasicDeliverEventArgs args)
    {
        try
        {
            var headers = args.BasicProperties.Headers is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(args.BasicProperties.Headers);
            headers["x-industrial-retry-kind"] = "inbox-busy";
            var properties = new BasicProperties
            {
                ContentType = args.BasicProperties.ContentType,
                Type = args.BasicProperties.Type,
                MessageId = args.BasicProperties.MessageId,
                DeliveryMode = args.BasicProperties.DeliveryMode,
                Headers = headers,
            };
            await _channel!.BasicPublishAsync(
                _options.BusyRetryExchangeName,
                args.RoutingKey,
                true,
                properties,
                args.Body,
                CancellationToken.None);
            await _channel.BasicAckAsync(args.DeliveryTag, false, CancellationToken.None);
        }
        catch
        {
            // Keep the original delivery when the dedicated retry path is unavailable.
            await _channel!.BasicNackAsync(args.DeliveryTag, false, true, CancellationToken.None);
            throw;
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "未注册任何集成事件订阅,跳过 RabbitMQ 消费启动。")]
    private partial void LogNoSubscriptions();

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "RabbitMQ 消费已启动:队列 {Queue},交换 {Exchange},路由模式 {RoutingPattern}")]
    private partial void LogConsumerStarted(string queue, string exchange, string routingPattern);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "处理集成事件 {EventName} 失败,消息进入失败处理(重试/DLQ)。")]
    private partial void LogHandleFailed(string eventName, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "RabbitMQ 消费端暂不可用,将在稍后重试。")]
    private partial void LogConsumerUnavailable(Exception exception);
}

/// <summary>Reads RabbitMQ's x-death history so retry is bounded across process restarts.</summary>
public static class RabbitDeliveryPolicy
{
    public static int GetDeliveryAttempt(IDictionary<string, object?>? headers, string mainQueueName)
    {
        if (headers is null || !headers.TryGetValue("x-death", out var raw) || raw is not IEnumerable deaths)
            return 0;

        var count = 0;
        foreach (var death in deaths)
        {
            if (death is not IDictionary values
                || !TryGetValue(values, "queue", out var queue)
                || !string.Equals(Convert.ToString(queue, System.Globalization.CultureInfo.InvariantCulture), mainQueueName, StringComparison.Ordinal)
                || !TryGetValue(values, "reason", out var reason)
                || !string.Equals(Convert.ToString(reason, System.Globalization.CultureInfo.InvariantCulture), "rejected", StringComparison.Ordinal)
                || !TryGetValue(values, "count", out var countValue))
            {
                continue;
            }

            count += Convert.ToInt32(countValue, System.Globalization.CultureInfo.InvariantCulture);
        }

        return Math.Max(0, count);
    }

    private static bool TryGetValue(IDictionary values, string key, out object? value)
    {
        foreach (DictionaryEntry entry in values)
        {
            if (string.Equals(Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture), key, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
