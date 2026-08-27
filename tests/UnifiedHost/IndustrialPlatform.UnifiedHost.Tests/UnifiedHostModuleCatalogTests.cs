using IndustrialPlatform.UnifiedHost;
using Xunit;

namespace IndustrialPlatform.UnifiedHost.Tests;

public sealed class UnifiedHostModuleCatalogTests
{
    [Fact]
    public void Catalog_is_explicit_and_preserves_initialization_order()
    {
        Assert.Equal(
            ["identity", "systemdata", "referencedata"],
            UnifiedHostModuleCatalog.GetServiceKeys(UnifiedHostModuleCatalog.Modules));
    }

    [Fact]
    public void Catalog_derives_external_prefixes_from_module_metadata()
    {
        Assert.Equal(
            ["/identity", "/systemdata", "/referencedata"],
            UnifiedHostModuleCatalog.GetExternalPathPrefixes(UnifiedHostModuleCatalog.Modules));
    }

    [Fact]
    public void Adding_a_catalog_module_requires_no_coordinator_or_prefix_list_change()
    {
        var modules = UnifiedHostModuleCatalog.Modules
            .Append(new TestModule("audit", "/audit"))
            .ToArray();

        Assert.Equal(
            ["identity", "systemdata", "referencedata", "audit"],
            UnifiedHostModuleCatalog.GetServiceKeys(modules));
        Assert.Equal(
            ["/identity", "/systemdata", "/referencedata", "/audit"],
            UnifiedHostModuleCatalog.GetExternalPathPrefixes(modules));
    }

    private sealed class TestModule(string serviceKey, string externalPathPrefix) : IndustrialPlatform.Web.Extensions.IUnifiedHostModule
    {
        public string ServiceKey { get; } = serviceKey;

        public string ExternalPathPrefix { get; } = externalPathPrefix;

        public void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration) { }

        public void RegisterHealthChecks(Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder healthChecks) { }

        public void MapEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) { }
    }
}
