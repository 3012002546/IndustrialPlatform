using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 数据库编排 Runner 后台服务(05 方案 §7.1.4):延迟启动等待控制面迁移完成后,
/// 有活动操作时按心跳间隔续租,否则按轮询间隔领取执行;轮询异常退避后继续。
/// <see cref="DatabaseOperationRunnerOptions.Enabled"/> 为 <c>false</c> 时直接退出(不消费队列),
/// 避免未配置产物/凭据时误消费。间隔参数沿用 <see cref="DatabaseOrchestrationOptions"/>。
/// </summary>
public sealed class DatabaseOrchestrationRunnerHostedService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BackoffInterval = TimeSpan.FromSeconds(5);

    private readonly IOperationRunnerCoordinator _runner;
    private readonly IOptions<DatabaseOperationRunnerOptions> _runnerOptions;
    private readonly IOptions<DatabaseOrchestrationOptions> _options;
    private readonly ILogger<DatabaseOrchestrationRunnerHostedService> _logger;

    /// <summary>初始化 Runner 后台服务。</summary>
    public DatabaseOrchestrationRunnerHostedService(
        IOperationRunnerCoordinator runner,
        IOptions<DatabaseOperationRunnerOptions> runnerOptions,
        IOptions<DatabaseOrchestrationOptions> options,
        ILogger<DatabaseOrchestrationRunnerHostedService> logger)
    {
        _runner = runner;
        _runnerOptions = runnerOptions;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_runnerOptions.Value.Enabled)
        {
            return;
        }

        // 延迟启动:等 SchemaMigrationBackgroundService 完成控制面表迁移,避免 claim 查表失败。
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_runner.HasActiveOperation)
                {
                    await _runner.HeartbeatActiveAsync(stoppingToken);
                    await Task.Delay(HeartbeatInterval, stoppingToken);
                }
                else
                {
                    var processed = await _runner.RunOnceAsync(stoppingToken);
                    await Task.Delay(
                        processed == 0 ? PollInterval : HeartbeatInterval,
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Log.RunnerLoopFailed(_logger, exception);
                await Task.Delay(BackoffInterval, stoppingToken);
            }
        }
    }

    private TimeSpan PollInterval => TimeSpan.FromSeconds(_options.Value.PollSeconds);

    private TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(_options.Value.HeartbeatSeconds);

    private static class Log
    {
        private static readonly Action<ILogger, Exception?> RunnerLoopFailedMessage = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(RunnerLoopFailed)),
            "数据库编排 Runner 轮询异常。");

        public static void RunnerLoopFailed(ILogger logger, Exception exception) =>
            RunnerLoopFailedMessage(logger, exception);
    }
}
