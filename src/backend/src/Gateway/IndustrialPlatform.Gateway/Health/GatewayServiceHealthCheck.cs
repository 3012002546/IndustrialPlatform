using IndustrialPlatform.Gateway.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.Gateway.Health;

/// <summary>
/// 下游服务就绪检查:GET <c>{DestinationUrl}/health/ready</c>,2xx 视为健康,
/// 503 视为未就绪,其他失败按类型名降级,不泄漏异常消息原文。
/// </summary>
public sealed class GatewayServiceHealthCheck : IHealthCheck
{
    private readonly GatewayServiceOptions _service;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>创建健康检查。</summary>
    public GatewayServiceHealthCheck(GatewayServiceOptions service, IHttpClientFactory httpClientFactory)
    {
        _service = service;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            using var response = await client.GetAsync(_service.DestinationUrl.TrimEnd('/') + "/health/ready", cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("服务就绪")
                : response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable
                    ? HealthCheckResult.Unhealthy("服务就绪检查未通过")
                    : HealthCheckResult.Degraded($"服务返回 {(int)response.StatusCode}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"服务不可访问:{exception.GetType().Name}");
        }
    }
}
