using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace IndustrialPlatform.SystemData.Api.Health;

/// <summary>
/// Redis 依赖健康检查,通过 PING 验证连接可用。
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _connection;

    /// <summary>创建健康检查。</summary>
    public RedisHealthCheck(IConnectionMultiplexer connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _connection.GetDatabase().PingAsync(CommandFlags.None);
            return HealthCheckResult.Healthy("Redis 可访问");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"Redis 不可访问:{exception.GetType().Name}");
        }
    }
}
