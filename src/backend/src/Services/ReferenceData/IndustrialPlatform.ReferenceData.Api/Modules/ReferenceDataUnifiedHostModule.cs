using IndustrialPlatform.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.ReferenceData.Api.Modules;

/// <summary>ReferenceData 在 UnifiedHost 中的显式组合适配器。</summary>
public sealed class ReferenceDataUnifiedHostModule : IUnifiedHostModule
{
    public string ServiceKey => "referencedata";

    public string ExternalPathPrefix => "/referencedata";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddReferenceDataModule(configuration);

    public void RegisterHealthChecks(IHealthChecksBuilder healthChecks) =>
        healthChecks.AddReferenceDataHealthChecks("referencedata");

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapReferenceDataModule();
}
