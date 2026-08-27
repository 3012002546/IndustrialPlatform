using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.Web.Extensions;

/// <summary>
/// UnifiedHost 可组合模块的显式边界。模块负责自己的服务、健康检查和专属端点；
/// 宿主只负责遍历目录、通用 Web 管道和启动协调。
/// </summary>
public interface IUnifiedHostModule
{
    string ServiceKey { get; }

    string ExternalPathPrefix { get; }

    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    void RegisterHealthChecks(IHealthChecksBuilder healthChecks);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
