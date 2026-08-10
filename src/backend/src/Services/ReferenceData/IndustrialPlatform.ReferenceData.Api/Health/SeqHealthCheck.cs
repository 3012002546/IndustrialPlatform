using IndustrialPlatform.Logging.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.ReferenceData.Api.Health;

/// <summary>
/// Seq 日志服务健康检查,GET /api 验证可访问;未启用视为健康,故障返回降级而非故障。
/// </summary>
public sealed class SeqHealthCheck : IHealthCheck
{
    private readonly SerilogOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>创建健康检查。</summary>
    public SeqHealthCheck(IOptions<SerilogOptions> options, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var seq = _options.Seq;
        if (seq is null || !seq.Enabled || string.IsNullOrWhiteSpace(seq.ServerUrl))
        {
            return HealthCheckResult.Healthy("Seq 未启用,跳过检查");
        }

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(2);

        using var request = new HttpRequestMessage(HttpMethod.Get, seq.ServerUrl.TrimEnd('/') + "/api");
        if (!string.IsNullOrWhiteSpace(seq.ApiKey))
        {
            request.Headers.Add("X-Seq-ApiKey", seq.ApiKey);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Seq 可访问")
                : HealthCheckResult.Degraded($"Seq 返回 {(int)response.StatusCode}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return HealthCheckResult.Degraded($"Seq 不可访问:{exception.GetType().Name}");
        }
    }
}
