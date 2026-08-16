using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

/// <summary>
/// 启动时执行 SystemData 库迁移的后台服务。
/// 数据库不可达时不抛异常(记录告警跳过),保持无 Docker 环境下服务可运行基线;
/// 迁移在数据库就绪后通过服务重启或后续任务的显式触发补齐。
/// 执行逻辑复用 <see cref="SystemDataStartupMigrations.RunAsync"/>,供 UnifiedHost 协调器按序调用。
/// </summary>
public sealed class SchemaMigrationBackgroundService : BackgroundService
{
    private static readonly Action<ILogger, Exception?> MigrationDeferred =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(MigrationDeferred)),
            "SystemData 库当前不可达,迁移暂缓执行;待数据库就绪后重试。");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchemaMigrationBackgroundService> _logger;

    /// <summary>
    /// 初始化后台迁移服务。
    /// </summary>
    /// <param name="scopeFactory">作用域工厂,用于解析迁移运行器。</param>
    /// <param name="logger">运行日志。</param>
    public SchemaMigrationBackgroundService(IServiceScopeFactory scopeFactory, ILogger<SchemaMigrationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SystemDataStartupMigrations.RunAsync(_scopeFactory, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !stoppingToken.IsCancellationRequested)
        {
            MigrationDeferred(_logger, ex);
        }
    }
}
