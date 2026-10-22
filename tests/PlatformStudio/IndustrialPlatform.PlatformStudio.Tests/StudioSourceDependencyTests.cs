using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Api;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioSourceDependencyTests
{
    [Fact]
    public void Valid_external_profile_resolves_source_administration_execution_and_catalog_from_real_registration()
    {
        var folder = Path.Combine(Path.GetTempPath(), "studio-source-di-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var configuration = Configuration(folder);
            var services = Services(configuration);
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var first = provider.CreateScope(); using var second = provider.CreateScope();
            Assert.NotNull(first.ServiceProvider.GetRequiredService<SourceAdministration>());
            Assert.NotNull(first.ServiceProvider.GetRequiredService<SourceCatalog>());
            Assert.IsType<RegisteredSourceCatalog>(first.ServiceProvider.GetRequiredService<IStudioSourceCatalogReader>());
            var firstExecutor = first.ServiceProvider.GetRequiredService<IStudioSourceExecutor>();
            var secondExecutor = second.ServiceProvider.GetRequiredService<IStudioSourceExecutor>();
            Assert.IsType<RegisteredSourceExecutor>(firstExecutor); Assert.NotSame(firstExecutor, secondExecutor);
            Assert.Same(first.ServiceProvider.GetRequiredService<StudioSourceProbes>(), second.ServiceProvider.GetRequiredService<StudioSourceProbes>());
            Assert.Same(first.ServiceProvider.GetRequiredService<StudioSourceLimits>(), second.ServiceProvider.GetRequiredService<StudioSourceLimits>());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Different_scoped_executors_share_the_tenant_limit_and_disposing_one_does_not_destroy_it()
    {
        var folder = Path.Combine(Path.GetTempPath(), "studio-source-limit-" + Guid.NewGuid().ToString("N"));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var server = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var connection = await listener.AcceptTcpClientAsync(stop.Token);
                    await using var stream = connection.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 }) { }
                    if (Interlocked.Increment(ref requests) == 1) { entered.TrySetResult(); await release.Task.WaitAsync(stop.Token); }
                    var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n[]");
                    await stream.WriteAsync(response, stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }, stop.Token);
        try
        {
            using var configuration = Configuration(folder);
            var services = Services(configuration);
            using var http = new HttpClient();
            // The only test override permits this controlled loopback server; the production singleton budget remains registered by AddPlatformStudio.
            services.AddScoped<IStudioSourceExecutor>(sp => new RegisteredSourceExecutor(sp.GetRequiredService<StudioDeploymentProfile>(), configuration, http, TimeProvider.System, allowLoopbackHttp: true, limits: sp.GetRequiredService<StudioSourceLimits>()));
            await using var provider = services.BuildServiceProvider();
            var profile = provider.GetRequiredService<StudioDeploymentProfile>();
            var endpoint = new StudioSourceEndpoint
            {
                BaseAddress = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture) + "/",
                ReadOnlyVerified = true, PublicQueriesAllowed = true, Operations = new() { ["read"] = new() { Path = "read" } },
            };
            profile.SourceRegistry["limited"] = endpoint;
            using var first = provider.CreateScope(); using var second = provider.CreateScope();
            var firstExecutor = first.ServiceProvider.GetRequiredService<IStudioSourceExecutor>();
            var secondExecutor = second.ServiceProvider.GetRequiredService<IStudioSourceExecutor>();
            Assert.NotSame(firstExecutor, secondExecutor);
            var source = new StudioSource { NId = "limited", Provider = "RegisteredApi", EndpointKey = "limited", EndpointConfigurationHash = firstExecutor.ConfigurationHash(endpoint), AllowedObjectsJson = "[{\"qualifiedName\":\"read\",\"kind\":\"ApiOperation\"}]", MaxRows = 100, MaxBytes = 1000, TimeoutSeconds = 10 };
            var document = new JsonObject { ["rowPolicyKey"] = "public", ["query"] = new JsonObject { ["kind"] = "Api", ["operationKey"] = "read" } };
            var principal = new StudioPrincipal("tenant-a", "user-a", "test", "test", [], [], [], "1", "1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
            var pending = firstExecutor.ExecuteAsync(principal, source, document, new(), false, stop.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), stop.Token);
            var blocked = await Assert.ThrowsAsync<StudioException>(() => secondExecutor.ExecuteAsync(principal, source, document, new(), false, stop.Token));
            Assert.Equal("STUDIO_LIMIT", blocked.Code); Assert.Equal(429, blocked.StatusCode); Assert.Equal(1, Volatile.Read(ref requests));
            release.TrySetResult(); Assert.Equal(0, (await pending).RowCount);
            first.Dispose();
            Assert.Equal(0, (await secondExecutor.ExecuteAsync(principal, source, document, new(), false, stop.Token)).RowCount);
            Assert.Equal(2, Volatile.Read(ref requests));
        }
        finally
        {
            release.TrySetResult(); await stop.CancelAsync(); listener.Stop(); await server;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private static ServiceCollection Services(IConfiguration configuration)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(configuration); services.AddAuthentication(); services.AddAuthorization();
        services.AddPlatformStudio(configuration); return services;
    }
    private static ConfigurationRoot Configuration(string folder) => (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["PlatformStudio:Enabled"] = "true", ["PlatformStudio:DeploymentMode"] = "ExternalMes", ["PlatformStudio:InstalledModules:0"] = "LowCode",
        ["PlatformStudio:Identity:Provider"] = "LocalAdmin", ["PlatformStudio:Identity:LocalAdminEnabled"] = "true", ["PlatformStudio:Identity:IssuerKey"] = "test-local",
        ["PlatformStudio:Identity:DataProtectionKeyPath"] = Path.Combine(folder, "keys"), ["PlatformStudio:Files:Provider"] = "Disabled", ["PlatformStudio:Audit:Provider"] = "Embedded",
        ["PlatformStudio:Jobs:Provider"] = "Disabled", ["PlatformStudio:PublicOrigin"] = "https://localhost", ["PlatformStudio:Database:Provider"] = "Sqlite", ["PlatformStudio:Database:SqliteFile"] = Path.Combine(folder, "studio.db"),
        ["PlatformStudio:MaxQueryConcurrencyPerTenant"] = "1",
    }).Build();
}
