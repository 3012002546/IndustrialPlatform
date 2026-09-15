using IndustrialPlatform.Collaboration.Application.RemoteAssistance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IndustrialPlatform.Collaboration.Infrastructure;

public sealed class RemoteAssistanceLifecycleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public RemoteAssistanceLifecycleWorker(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var startupScope = _scopeFactory.CreateScope())
            await startupScope.ServiceProvider.GetRequiredService<RemoteAssistanceService>().SweepLifecycleAsync(true, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RemoteAssistanceService>().SweepLifecycleAsync(false, stoppingToken);
        }
    }
}
