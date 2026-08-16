using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.UnifiedHost;

/// <summary>
/// UnifiedHost 模块迁移协调器:在宿主启动阶段**确定顺序**执行
/// Identity 启动迁移与系统目录种子 → SystemData 启动迁移,替代两套并行
/// <c>SchemaMigrationBackgroundService</c>(避免并行迁移同一 Shared 物理库,SQLite 锁竞争)。
///
/// 作为普通 <see cref="IHostedService"/> 注册在宿主托管服务最前,StartAsync 阻塞等待
/// 两个模块迁移完成;宿主随后按注册顺序启动其余托管服务(Outbox 派发、数据库编排 Runner 等),
/// 因此依赖迁移完成的后台服务必然在迁移完成后才启动。失败即上抛,宿主启动失败(确定顺序语义,
/// 不依赖重启、偶然时序或 PostgreSQL 最终收敛)。执行逻辑复用各模块提取的启动迁移实现,不复制实现。
/// </summary>
public sealed partial class ModuleMigrationCoordinatorHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ModuleMigrationCoordinatorHostedService> _logger;

    /// <summary>初始化模块迁移协调器。</summary>
    public ModuleMigrationCoordinatorHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ModuleMigrationCoordinatorHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogIdentityMigrationsStarted();
        await IdentityStartupMigrations.RunAsync(_scopeFactory, cancellationToken);
        LogIdentityMigrationsCompleted();

        LogSystemDataMigrationsStarted();
        await SystemDataStartupMigrations.RunAsync(_scopeFactory, cancellationToken);
        LogSystemDataMigrationsCompleted();
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "UnifiedHost 模块迁移:开始执行 Identity 迁移与系统目录种子。")]
    private partial void LogIdentityMigrationsStarted();

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "UnifiedHost 模块迁移:Identity 完成,开始 SystemData。")]
    private partial void LogIdentityMigrationsCompleted();

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "UnifiedHost 模块迁移:执行 SystemData 迁移。")]
    private partial void LogSystemDataMigrationsStarted();

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "UnifiedHost 模块迁移:全部完成。")]
    private partial void LogSystemDataMigrationsCompleted();
}
