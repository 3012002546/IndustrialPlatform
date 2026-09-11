using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.Collaboration.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCollaborationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSqlSugar(configuration);
        services.AddSingleton<ICollaborationRepository, Persistence.SqlCollaborationRepository>();
        services.AddHttpClient("Collaboration.Identity", client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddHttpClient("Collaboration.SystemData", client => client.Timeout = TimeSpan.FromSeconds(5));
        if (string.Equals(configuration["Collaboration:Identity:Mode"], "Embedded", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ICollaborationIdentityDirectory, InProcessIdentityDirectory>();
        else
            services.AddSingleton<ICollaborationIdentityDirectory, HttpIdentityDirectory>();
        if (string.Equals(configuration["Collaboration:SystemData:Mode"], "Http", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ICollaborationFilePort, HttpSystemDataFilePort>();
        else
            services.AddSingleton<ICollaborationFilePort, SystemDataFilePort>();
        if (string.Equals(configuration["Collaboration:SystemData:Mode"], "Http", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ICollaborationAuditPort, HttpSystemDataAuditPort>();
        else
            services.AddSingleton<ICollaborationAuditPort, SystemDataAuditPort>();
        services.AddSingleton<ICollaborationPresence, PresenceRegistry>();
        services.AddSingleton<CollaborationMetricsCollector>();
        if (string.Equals(configuration["Collaboration:Identity:Mode"], "Embedded", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IComplianceStepUpVerifier, JwtStepUpProofVerifier>();
        else
            services.AddSingleton<IComplianceStepUpVerifier, HttpIdentityStepUpVerifier>();
        services.AddSingleton<IStepUpBindingIssuer, StepUpBindingIssuer>();
        services.AddSingleton<ICollaborationRealtimePublisher, NoopCollaborationRealtimePublisher>();
        services.AddSingleton(new PageCursorCodec(
            configuration["Collaboration:Cursor:SigningKey"] ?? configuration["Jwt:SigningKey"],
            TimeSpan.FromMinutes(5)));
        services.AddHostedService<CollaborationExportWorker>();
        services.AddHostedService<CollaborationOutboxDispatcher>();
        services.AddHostedService<CollaborationRetentionWorker>();
        services.AddSingleton<CollaborationServiceInitializer>();
        services.AddSingleton<IndustrialPlatform.Application.Abstractions.Initialization.IServiceInitializer>(sp => sp.GetRequiredService<CollaborationServiceInitializer>());
        return services;
    }
}
