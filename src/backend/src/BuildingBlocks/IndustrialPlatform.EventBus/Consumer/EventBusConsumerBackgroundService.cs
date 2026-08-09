using System.Text.Json;
using System.Text.Json.Serialization;
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

        _channel = await _connection.CreateChannelAsync(stoppingToken);
        await _channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic, true, false, null, false, stoppingToken);
        await _channel.QueueDeclareAsync(_options.QueueName, true, false, false, null, false, stoppingToken);
        await _channel.QueueBindAsync(_options.QueueName, _options.ExchangeName, _options.RoutingPattern, null, false, stoppingToken);
        await _channel.BasicQosAsync(0, _options.PrefetchCount, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(_options.QueueName, false, consumer, stoppingToken);

        LogConsumerStarted(_options.QueueName, _options.ExchangeName, _options.RoutingPattern);

        await Task.Delay(Timeout.Infinite, stoppingToken);
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
            await _channel!.BasicNackAsync(args.DeliveryTag, false, false, CancellationToken.None);
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
}
