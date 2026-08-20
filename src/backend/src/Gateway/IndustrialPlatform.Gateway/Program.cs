using IndustrialPlatform.Gateway.Configuration;
using IndustrialPlatform.Gateway.Errors;
using IndustrialPlatform.Gateway.Health;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.UseIndustrialSerilog();

var gatewayOptions = builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>() ?? new GatewayOptions();

builder.Services.AddCors(options =>
{
    options.AddPolicy(GatewayOptions.CorsPolicyName, policy =>
    {
        var origins = gatewayOptions.Cors.AllowedOrigins.Where(origin => !string.IsNullOrWhiteSpace(origin)).ToArray();
        if (origins.Length == 0)
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(origins);
            // SSO 浏览器会话 Cookie(§26.4)经网关跨源读写:仅对显式源放行凭据。
            policy.AllowCredentials();
        }

        policy.AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddHttpClient();

var healthChecks = builder.Services.AddHealthChecks();
foreach (var service in gatewayOptions.Services.Where(GatewayRouteFactory.IsValid))
{
    var serviceOptions = service;
    healthChecks.AddTypeActivatedCheck<GatewayServiceHealthCheck>(
        $"service.{serviceOptions.Name}",
        failureStatus: default,
        tags: [],
        timeout: TimeSpan.FromSeconds(10),
        args: [serviceOptions]);
}

builder.Services.AddReverseProxy()
    .LoadFromMemory(GatewayRouteFactory.BuildRoutes(gatewayOptions), GatewayRouteFactory.BuildClusters(gatewayOptions));

var app = builder.Build();

app.UseCors(GatewayOptions.CorsPolicyName);
app.UseIndustrialWeb();
app.UseMiddleware<GatewayProxyErrorMiddleware>();
app.MapReverseProxy();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Gateway" }));
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthCheckResponseWriter.Write,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.Write,
});
app.MapFallback(() => Results.Json(ApiResult.Fail<object?>("404", "路由不存在"), statusCode: StatusCodes.Status404NotFound));

app.Run();

public partial class Program;

namespace IndustrialPlatform.Gateway
{
    /// <summary>Gateway 部署边界的可执行保护：只允许代理相关程序集依赖。</summary>
    public static class GatewayBoundary
    {
        private static readonly string[] ForbiddenServiceAssemblies =
            ["IndustrialPlatform.Identity", "IndustrialPlatform.SystemData", "IndustrialPlatform.ReferenceData"];

        public static bool IsPureProxyAssembly(System.Reflection.Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            return assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name is not null)
                .All(name => ForbiddenServiceAssemblies.All(forbidden =>
                    !name!.StartsWith(forbidden, StringComparison.Ordinal)));
        }
    }
}
