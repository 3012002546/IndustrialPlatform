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
