using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Connection;
using IndustrialPlatform.EventBus.Events;
using IndustrialPlatform.EventBus.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace IndustrialPlatform.EventBus.Producer;

/// <summary>
/// 基于 RabbitMQ 的事件发布器,写入 Topic 交换器。
/// </summary>
public sealed partial class RabbitMqEventBus : IEventBus, IDisposable
{
    private readonly IRabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _syncRoot = new(1, 1);

    private IChannel? _channel;
    private bool _disposed;

    /// <summary>
    /// 创建事件发布器。
    /// </summary>
    /// <param name="connection">RabbitMQ 连接。</param>
    /// <param name="options">事件总线配置。</param>
    /// <param name="logger">日志器。</param>
    public RabbitMqEventBus(IRabbitMqConnection connection, IOptions<RabbitMqOptions> options, ILogger<RabbitMqEventBus> logger)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _connection = connection;
        _options = options.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    /// <inheritdoc/>
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, string? routingKey = null, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var channel = await GetChannelAsync(cancellationToken);
        var eventName = integrationEvent.EventType;
        var body = JsonSerializer.SerializeToUtf8Bytes(integrationEvent, _jsonOptions);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Type = eventName,
            MessageId = integrationEvent.EventId.ToString(),
            DeliveryMode = DeliveryModes.Persistent,
        };

        await channel.BasicPublishAsync(_options.ExchangeName, routingKey ?? eventName, false, properties, body, cancellationToken);

        LogPublished(eventName, integrationEvent.EventId);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _syncRoot.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            _channel = await _connection.CreateChannelAsync(cancellationToken);
            await _channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic, true, false, null, false, cancellationToken);
            return _channel;
        }
        finally
        {
            _syncRoot.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel?.Dispose();
        _syncRoot.Dispose();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "已发布集成事件 {EventName},EventId {EventId}")]
    private partial void LogPublished(string eventName, Guid eventId);
}
