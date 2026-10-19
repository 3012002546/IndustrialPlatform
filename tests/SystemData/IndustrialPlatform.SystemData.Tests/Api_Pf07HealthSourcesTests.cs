using IndustrialPlatform.Logging.Options;
using IndustrialPlatform.SystemData.Api.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using IndustrialPlatform.SystemData.Application.Files;

namespace IndustrialPlatform.SystemData.Api.Tests;

public sealed class Pf07HealthSourcesTests
{
    [Fact]
    public async Task Disabled_Seq_is_explicitly_disabled_without_changing_legacy_readiness()
    {
        var options = Options.Create(new SerilogOptions());
        var clients = new UnusedClients();
        IHealthCheck[] checks = [new SeqHealthCheck(options, clients)];
        foreach (var checker in checks)
        {
            var result = await checker.CheckHealthAsync(new(), CancellationToken.None);
            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Equal("Disabled", result.Data["reportedState"]);
        }
    }
    [Fact]
    public async Task Development_scan_bypass_has_explicit_degradation_reason()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["SystemData:ClamAv:DevelopmentBypass"]="true" }).Build();
        var checker = new FileScanHealthCheck(new BypassScanner(), configuration);
        var result = await checker.CheckHealthAsync(new(), CancellationToken.None);
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("FILE_SCAN_DEVELOPMENT_BYPASS", result.Data["reasonCode"]);
    }
    private sealed class UnusedClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Disabled检查不得发HTTP请求。");
    }
    private sealed class BypassScanner : IFileScanner, IFileScannerHealth
    {
        public Task<string> ReadHealthAsync(CancellationToken cancellationToken) => Task.FromResult("Degraded");
        public Task<FileScanResult> ScanAsync(FileObjectRecord file, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
