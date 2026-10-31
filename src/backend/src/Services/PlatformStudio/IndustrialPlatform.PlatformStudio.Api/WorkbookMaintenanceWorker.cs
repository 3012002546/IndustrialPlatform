using IndustrialPlatform.PlatformStudio.Infrastructure.Report;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.PlatformStudio.Api;

/// <summary>重试已提交工作文件的回收；独立于Office保存响应，宿主重启后继续处理持久队列。</summary>
internal sealed partial class WorkbookMaintenanceWorker(IServiceScopeFactory scopes, ILogger<WorkbookMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<StudioWorkbooks>().RecycleWorkingFilesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception error) { LogRecycleFailed(logger, error); }
        }
    }
    [LoggerMessage(EventId = 551, Level = LogLevel.Warning, Message = "工作簿临时文件回收将在后续周期重试。")]
    private static partial void LogRecycleFailed(ILogger logger, Exception error);
}
