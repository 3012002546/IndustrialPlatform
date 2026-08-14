using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IndustrialPlatform.Gateway.Tests;

/// <summary>
/// 构造指向测试桩或不可达目标的 Gateway 测试宿主。
/// 使用 <c>UseSetting</c> 注入配置,确保在 Program 启动阶段(WebApplication.CreateBuilder)即可读到覆盖值。
/// </summary>
internal static class TestGateway
{
    /// <summary>构造指向 <paramref name="stub"/> 的 Gateway。</summary>
    public static WebApplicationFactory<Program> Create(DownstreamStub stub, int timeoutSeconds = 10)
        => CreateCore(stub.BaseUrl, timeoutSeconds);

    /// <summary>构造指向未监听端口(不可达)的 Gateway。</summary>
    public static WebApplicationFactory<Program> CreateUnreachable(int timeoutSeconds = 10)
        => CreateCore($"http://localhost:{DownstreamStub.GetUnusedPort()}", timeoutSeconds);

    private static WebApplicationFactory<Program> CreateCore(string destinationUrl, int timeoutSeconds)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Gateway:Services:0:Name", "stub");
                builder.UseSetting("Gateway:Services:0:PathPrefix", "/stub");
                builder.UseSetting("Gateway:Services:0:DestinationUrl", destinationUrl);
                // 中和 appsettings.Development.json 中索引 1 起的其余服务(如 referencedata/systemdata),
                // 使其 PathPrefix/DestinationUrl 为空,经 GatewayRouteFactory.IsValid 过滤后不参与路由与就绪聚合,
                // 避免泄漏指向不可达 localhost 端口的健康检查。上界覆盖未来新增服务,避免逐条追加。
                for (var i = 1; i <= 10; i++)
                {
                    builder.UseSetting($"Gateway:Services:{i}:PathPrefix", string.Empty);
                    builder.UseSetting($"Gateway:Services:{i}:DestinationUrl", string.Empty);
                }
                builder.UseSetting("Gateway:RequestTimeoutSeconds", timeoutSeconds.ToString(CultureInfo.InvariantCulture));
            });
    }
}
