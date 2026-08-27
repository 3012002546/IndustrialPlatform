using IndustrialPlatform.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.SystemData.Api.Modules;

/// <summary>SystemData 在 UnifiedHost 中的显式组合适配器。</summary>
public sealed class SystemDataUnifiedHostModule : IUnifiedHostModule
{
    public string ServiceKey => "systemdata";

    public string ExternalPathPrefix => "/systemdata";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSystemDataModule(configuration, includeStartupMigrationService: false);

    public void RegisterHealthChecks(IHealthChecksBuilder healthChecks) =>
        healthChecks.AddSystemDataHealthChecks("systemdata");

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapSystemDataModule();
}
