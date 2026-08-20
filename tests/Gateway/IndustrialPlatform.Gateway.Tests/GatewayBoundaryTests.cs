using IndustrialPlatform.Gateway;

namespace IndustrialPlatform.Gateway.Tests;

public sealed class GatewayBoundaryTests
{
    [Fact]
    public void Gateway_only_proxies_and_does_not_load_service_modules()
    {
        Assert.True(GatewayBoundary.IsPureProxyAssembly(typeof(Program).Assembly));
    }
}
