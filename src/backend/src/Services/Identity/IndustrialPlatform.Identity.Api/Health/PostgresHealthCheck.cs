using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.Identity.Api.Health;

/// <summary>
/// PostgreSQL 依赖健康检查,执行 SELECT 1 验证连接可用。
/// </summary>
public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>创建健康检查。</summary>
    public PostgresHealthCheck(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SqlSugar.Ado.GetIntAsync("SELECT 1");
            return HealthCheckResult.Healthy("PostgreSQL 可访问");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"PostgreSQL 不可访问:{exception.GetType().Name}");
        }
    }
}
