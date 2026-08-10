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
                builder.UseSetting("Gateway:Services:1:PathPrefix", string.Empty);
                builder.UseSetting("Gateway:Services:1:DestinationUrl", string.Empty);
                builder.UseSetting("Gateway:RequestTimeoutSeconds", timeoutSeconds.ToString(CultureInfo.InvariantCulture));
            });
    }
}
