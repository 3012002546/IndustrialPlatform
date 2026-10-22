using IndustrialPlatform.Web.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.PlatformStudio.Api;

/// <summary>按显式开关加入UnifiedHost的可选模块，外部固定studio路径不二次剥前缀。</summary>
public sealed class PlatformStudioUnifiedHostModule : IUnifiedHostModule
{
    public string ServiceKey=>"platform-studio";
    public string ExternalPathPrefix=>"/api/v1/studio";
    public bool StripExternalPathPrefix=>false;
    public void RegisterServices(IServiceCollection services,IConfiguration configuration)=>services.AddPlatformStudio(configuration);
    public void RegisterHealthChecks(IHealthChecksBuilder healthChecks)=>healthChecks.AddCheck<StudioReadinessHealthCheck>("studio.core");
    public void MapEndpoints(IEndpointRouteBuilder endpoints)=>endpoints.MapPlatformStudio();
}
