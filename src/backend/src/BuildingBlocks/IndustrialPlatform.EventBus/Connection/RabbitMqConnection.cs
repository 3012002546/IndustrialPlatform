using IndustrialPlatform.EventBus.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace IndustrialPlatform.EventBus.Connection;

/// <summary>
/// RabbitMQ 连接实现,懒建立连接并在异常关闭后自动重建。
/// </summary>
public sealed partial class RabbitMqConnection : IRabbitMqConnection, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnection> _logger;
    private readonly SemaphoreSlim _syncRoot = new(1, 1);

    private IConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// 创建连接。
    /// </summary>
    /// <param name="options">RabbitMQ 配置。</param>
    /// <param name="logger">日志器。</param>
    public RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return await connection.CreateChannelAsync(new CreateChannelOptions(false, true), cancellationToken);
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _syncRoot.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;

            LogConnectionEstablished(_options.Host, _options.Port, _options.VirtualHost);
            return _connection;
        }
        catch (Exception exception)
        {
            LogConnectionFailed(_options.Host, _options.Port, exception);
            throw;
        }
        finally
        {
            _syncRoot.Release();
        }
    }

    private Task OnConnectionShutdownAsync(object? sender, ShutdownEventArgs args)
    {
        LogConnectionShutdown(args.ReplyText);
        _connection = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        _syncRoot.Dispose();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "RabbitMQ 连接已建立:{Host}:{Port}/{VirtualHost}")]
    private partial void LogConnectionEstablished(string host, int port, string virtualHost);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "RabbitMQ 连接失败:{Host}:{Port}")]
    private partial void LogConnectionFailed(string host, int port, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "RabbitMQ 连接已关闭,原因:{Reason}")]
    private partial void LogConnectionShutdown(string reason);
}
