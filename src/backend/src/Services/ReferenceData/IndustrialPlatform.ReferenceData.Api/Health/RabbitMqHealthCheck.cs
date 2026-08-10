using IndustrialPlatform.EventBus.Connection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.ReferenceData.Api.Health;

/// <summary>
/// RabbitMQ 依赖健康检查,建立连接并创建通道验证可用。
/// </summary>
public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IRabbitMqConnection _connection;

    /// <summary>创建健康检查。</summary>
    public RabbitMqHealthCheck(IRabbitMqConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var channel = await _connection.CreateChannelAsync(cancellationToken);
            await channel.DisposeAsync();
            return HealthCheckResult.Healthy("RabbitMQ 可访问");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"RabbitMQ 不可访问:{exception.GetType().Name}");
        }
    }
}
