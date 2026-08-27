using IndustrialPlatform.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.Identity.Api.Modules;

/// <summary>Identity 在 UnifiedHost 中的显式组合适配器。</summary>
public sealed class IdentityUnifiedHostModule : IUnifiedHostModule
{
    public string ServiceKey => "identity";

    public string ExternalPathPrefix => "/identity";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddIdentityModule(configuration, includeStartupMigrationService: false);

    public void RegisterHealthChecks(IHealthChecksBuilder healthChecks) =>
        healthChecks.AddIdentityHealthChecks("identity");

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapIdentityModule();
}
