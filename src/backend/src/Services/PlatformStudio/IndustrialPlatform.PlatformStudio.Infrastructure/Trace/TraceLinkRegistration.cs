using IndustrialPlatform.PlatformStudio.Application.Trace;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.RuntimeSupport.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Trace;

public static class TraceLinkRegistration
{
    public static IServiceCollection AddStudioTraceLinks(this IServiceCollection services, StudioDeploymentProfile profile)
    {
        services.AddHttpClient<StudioMesFiles>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<TraceLinkTickets>();
        services.AddScoped<ITraceNodeLinks, TraceNodeLinks>();
        if (profile.ActiveModules.Contains("Traceability", StringComparer.Ordinal) && profile.Files.Provider != "Disabled") services.AddHostedService<TracePreviewCleanup>();
        return services;
    }
}

internal sealed class TracePreviewCleanup(IServiceScopeFactory scopes, ILogger<TracePreviewCleanup> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, Exception?> Retained = LoggerMessage.Define<int>(LogLevel.Warning, new EventId(9101, "TracePreviewRetained"), "Trace preview cleanup retained {Count} expired references: upload, scan, hold or concurrent state still requires confirmation.");
    private static readonly Action<ILogger, string, Exception?> Failed = LoggerMessage.Define<string>(LogLevel.Warning, new EventId(9102, "TracePreviewCleanupFailed"), "Trace preview cleanup failed ({Type}); references retained.");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (scope.ServiceProvider.GetService<IOwnedFileService>() is IOwnedFilePreviewCleanup cleanup)
                {
                    var result = await cleanup.CleanupExpiredPreviewsAsync(stoppingToken);
                    if (result.Retained > 0) Retained(logger, result.Retained, null);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { Failed(logger, exception.GetType().Name, null); }
        }
    }
}
