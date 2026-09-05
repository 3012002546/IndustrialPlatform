using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using IndustrialPlatform.Web.Initialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class InitializationHttpTests
{
    [Fact]
    public async Task Internal_protocol_resolves_local_target_and_returns_raw_plan_and_state()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pf03-http-{Guid.NewGuid():N}.db");
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SqlSugar:ConnectionString"] = $"Data Source={path};Pooling=False",
                ["DatabaseTopology:SharedSqliteFile"] = path,
                ["InternalInitialization:Key"] = "test-only-initialization-key",
                ["ReferenceData:Initialization:AutoApply"] = "false",
            })).ConfigureTestServices(services => services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(path + ".keys")).UseEphemeralDataProtectionProvider()));
            using var client = factory.CreateClient();
            var request = new InternalInitializationRequest("TENANT-A", "OP-A", ReferenceDataServiceInitializer.CurrentVersion, ServiceInitializationPolicy.Standard, "TRACE-A");
            using var denied = await client.PostAsJsonAsync("/api/v1/internal/initialization/referencedata/inspect", request);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            client.DefaultRequestHeaders.Add("X-Industrial-Initialization-Key", "test-only-initialization-key");
            using var inspected = await client.PostAsJsonAsync("/api/v1/internal/initialization/referencedata/inspect", request);
            inspected.EnsureSuccessStatusCode();
            var inspection = await inspected.Content.ReadFromJsonAsync<ServiceInitializationState>();
            Assert.False(inspection!.Ready);
            using var planned = await client.PostAsJsonAsync("/api/v1/internal/initialization/referencedata/plan", request with { Inspection = inspection });
            planned.EnsureSuccessStatusCode();
            var plan = await planned.Content.ReadFromJsonAsync<ServiceInitializationPlan>();
            Assert.True(plan!.RequiresApply);
            using var applied = await client.PostAsJsonAsync("/api/v1/internal/initialization/referencedata/apply", request with { Plan = plan });
            applied.EnsureSuccessStatusCode();
            Assert.True((await applied.Content.ReadFromJsonAsync<ServiceInitializationState>())!.Ready);
            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            using var json = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
            Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(path + ".keys")) Directory.Delete(path + ".keys", true);
        }
    }
}
