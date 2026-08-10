using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace IndustrialPlatform.Gateway.Configuration;

/// <summary>
/// 将 <see cref="GatewayOptions"/> 转换为 YARP 路由与集群配置。
/// </summary>
public static class GatewayRouteFactory
{
    /// <summary>按服务配置生成 YARP 路由,转发时剥离 <c>PathPrefix</c>。</summary>
    public static IReadOnlyList<RouteConfig> BuildRoutes(GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Services
            .Where(IsValid)
            .Select(service => new RouteConfig
            {
                RouteId = $"{service.Name}-route",
                ClusterId = $"{service.Name}-cluster",
                Match = new RouteMatch { Path = $"{service.PathPrefix}/{{**catch-all}}" },
                Transforms = [new Dictionary<string, string> { { "PathRemovePrefix", service.PathPrefix } }],
            })
            .ToArray();
    }

    /// <summary>按服务配置生成 YARP 集群,含请求活动超时。</summary>
    public static IReadOnlyList<ClusterConfig> BuildClusters(GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Services
            .Where(IsValid)
            .Select(service => new ClusterConfig
            {
                ClusterId = $"{service.Name}-cluster",
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    [service.Name] = new DestinationConfig { Address = service.DestinationUrl },
                },
                HttpRequest = new ForwarderRequestConfig
                {
                    ActivityTimeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
                },
            })
            .ToArray();
    }

    /// <summary>服务配置是否可用于路由与健康检查。</summary>
    public static bool IsValid(GatewayServiceOptions service)
        => !string.IsNullOrWhiteSpace(service.Name)
           && !string.IsNullOrWhiteSpace(service.PathPrefix)
           && service.PathPrefix.StartsWith('/')
           && !string.IsNullOrWhiteSpace(service.DestinationUrl);
}
