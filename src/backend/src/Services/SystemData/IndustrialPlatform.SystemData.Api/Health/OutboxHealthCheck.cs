using IndustrialPlatform.SystemData.Application.Reliability;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.SystemData.Api.Health;

/// <summary>Outbox 可靠性检查，验证待发布队列可查询，避免只报告底层数据库连接。</summary>
public sealed class OutboxHealthCheck : IHealthCheck
{
    private readonly IControlPlaneOutbox _outbox;

    public OutboxHealthCheck(IControlPlaneOutbox outbox) => _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = await _outbox.GetPendingAsync(1, cancellationToken);
            return HealthCheckResult.Healthy($"Outbox 可查询，待发布探测数:{pending.Count}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"Outbox 不可查询:{exception.GetType().Name}");
        }
    }
}
