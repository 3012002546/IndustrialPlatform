using IndustrialPlatform.EventBus.Connection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.SystemData.Api.Health;

/// <summary>RabbitMQ 依赖健康检查，创建短生命周期通道验证连接可用。</summary>
public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IRabbitMqConnection _connection;

    public RabbitMqHealthCheck(IRabbitMqConnection connection) => _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var channel = await _connection.CreateChannelAsync(cancellationToken);
            return HealthCheckResult.Healthy("RabbitMQ 可访问");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"RabbitMQ 不可访问:{exception.GetType().Name}");
        }
    }
}
