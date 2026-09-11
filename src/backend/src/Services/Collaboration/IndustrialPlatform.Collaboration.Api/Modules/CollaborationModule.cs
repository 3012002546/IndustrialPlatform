using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Collaboration.Infrastructure;
using IndustrialPlatform.Collaboration.Api.Hubs;
using IndustrialPlatform.Collaboration.Api.Security;
using IndustrialPlatform.Collaboration.Api.Health;
using IndustrialPlatform.Collaboration.Infrastructure.Files;
using IndustrialPlatform.EventBus.Events;
using IndustrialPlatform.SystemData.Contracts.Files;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.Collaboration.Api.Modules;

public static class CollaborationModule
{
    public static IServiceCollection AddCollaborationModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddCollaborationInfrastructure(configuration);
        services.AddCollaborationApplication();
        services.AddScoped<ICollaborationPermissionEvaluator, HttpCollaborationPermissionEvaluator>();
        var signalR = services.AddSignalR();
        var redisConnectionString = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            signalR.AddStackExchangeRedis(redisConnectionString, options =>
            {
                options.Configuration.AbortOnConnectFail = false;
            });
        }
        services.AddSingleton<CollaborationRealtimePublisher>();
        services.AddSingleton<ICollaborationRealtimePublisher>(serviceProvider => serviceProvider.GetRequiredService<CollaborationRealtimePublisher>());
        services.AddIntegrationEventConsumer<CollaborationRealtimeIntegrationEvent, CollaborationRealtimeIntegrationEventConsumer>();
        services.AddIntegrationEventConsumer<FileStatusChangedIntegrationEvent, CollaborationFileStatusConsumer>();
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var previous = options.Events.OnMessageReceived;
            options.Events.OnMessageReceived = async context =>
            {
                await previous(context);
                // Browser WebSocket/SSE transports cannot set the Authorization header.
                // Accept query credentials only on the exact collaboration hub endpoints.
                if (context.Result is null && string.IsNullOrEmpty(context.Token)
                    && HttpMethods.IsGet(context.Request.Method)
                    && !context.Request.Headers.ContainsKey("Authorization")
                    && (context.Request.Path == "/hubs/collaboration-v1"
                        || context.Request.Path == "/collaboration/hubs/collaboration-v1")
                    && context.Request.Query["access_token"].Count == 1)
                {
                    context.Token = context.Request.Query["access_token"];
                }
            };
        });
        return services;
    }

    public static IServiceCollection AddCollaborationApplication(this IServiceCollection services)
    {
        services.AddScoped<CollaborationService>();
        return services;
    }

    public static IHealthChecksBuilder AddCollaborationHealthChecks(this IHealthChecksBuilder builder, string? namePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var name = string.IsNullOrWhiteSpace(namePrefix) ? "collaboration.schema" : $"{namePrefix}.schema";
        return builder.AddCheck<CollaborationOutboxHealthCheck>(name, failureStatus: HealthStatus.Unhealthy, tags: ["capability"], timeout: TimeSpan.FromSeconds(3));
    }

    public static IEndpointRouteBuilder MapCollaborationModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapHub<CollaborationHub>("/hubs/collaboration-v1");
        endpoints.MapHub<CollaborationHub>("/collaboration/hubs/collaboration-v1");
        endpoints.MapGet("/collaboration/metrics", async (CollaborationMetricsCollector metrics, ICollaborationRepository repository, CancellationToken cancellationToken) =>
            Results.Text(await metrics.RenderPrometheusAsync(repository, cancellationToken), "text/plain; version=0.0.4"));
        return endpoints;
    }
}

public sealed class CollaborationUnifiedHostModule : IndustrialPlatform.Web.Extensions.IUnifiedHostModule
{
    public string ServiceKey => "collaboration";
    public string ExternalPathPrefix => "/collaboration";
    public bool StripExternalPathPrefix => false;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) => services.AddCollaborationModule(configuration);
    public void RegisterHealthChecks(IHealthChecksBuilder healthChecks) => healthChecks.AddCollaborationHealthChecks("collaboration");
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapCollaborationModule();
}
