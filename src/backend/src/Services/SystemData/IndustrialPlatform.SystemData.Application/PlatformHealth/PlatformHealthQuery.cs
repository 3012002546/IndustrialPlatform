using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Contracts.PlatformHealth;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Application.PlatformHealth;

/// <summary>服务目录授权范围与真实新鲜度共同约束的只读聚合。</summary>
public sealed class PlatformHealthQuery(IServiceCatalogControlService catalog, IPlatformHealthSnapshot snapshot,
    TimeProvider timeProvider, IOptions<PlatformHealthOptions> options) : IPlatformHealthQuery
{
    public async Task<PlatformHealthSummary> GetSummaryAsync(string tenantNId, string? serviceNId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var visible = await catalog.ListAsync(tenantNId, cancellationToken);
        var services = visible.Where(service => serviceNId is null || service.ServiceNId == serviceNId)
            .Select(service => PlatformHealthAggregation.Build(service.ServiceNId, service.Name,
                snapshot.Read(ServiceKey(service)).Select(check => check with { ServiceNId = service.ServiceNId }).ToArray(), now,
                Math.Clamp(options.Value.StaleAfterSeconds, 10, 3600), enabled: options.Value.Enabled && service.Status == "Active")).ToArray();
        var state = services.Length == 0 ? "Unknown"
            : services.All(service => service.State == "Disabled") ? "Disabled"
            : services.Any(service => service.State == "Unavailable") ? "Unavailable"
            : services.Any(service => service.State == "Unknown") ? "Unknown"
            : services.Any(service => service.State == "Degraded") ? "Degraded" : "Healthy";
        return new(state, now, services, services.Count(service => service.State == "Healthy"),
            services.Count(service => service.State == "Degraded"), services.Count(service => service.State == "Unavailable"),
            services.Count(service => service.State == "Unknown"), services.Count(service => service.State == "Disabled"));
    }

    private static string ServiceKey(IndustrialPlatform.SystemData.Contracts.ControlPlane.ServiceCatalogResponse service)
    {
        // 只把已登记的内部模块前缀映射到宿主公开检查；不从目录URL主动发请求。
        var key = service.GatewayPathPrefix?.Trim('/').Split('/')[^1];
        return key is "identity" or "systemdata" or "referencedata" or "collaboration" ? key : service.ServiceNId;
    }
}
