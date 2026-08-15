using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// 启动时执行身份库迁移与不可变系统目录种子(SystemCatalog/TenantSecurity)的后台服务。
/// 数据库不可达时不抛异常(记录告警跳过),保持无 Docker 环境下服务可运行基线;
/// 迁移/种子在数据库就绪后通过服务重启或显式初始化补齐。
/// SecretBootstrap(内置 admin)不在此自动执行:admin 引导必须由显式初始化编排调用,
/// 以保证临时密码只向受保护调用方交付一次(§29A.4)。
/// </summary>
public sealed class SchemaMigrationBackgroundService : BackgroundService
{
    private static readonly Action<ILogger, Exception?> MigrationDeferred =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(MigrationDeferred)),
            "身份库当前不可达,迁移与系统目录种子暂缓执行;待数据库就绪后重试。");

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
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<ISchemaMigrationRunner>();
            await runner.ApplyPendingAsync(stoppingToken);

            // 不可变系统目录种子(权限目录 + SYSTEM_ADMIN)幂等补齐,保持开发基线可用;
            // 不含 SecretBootstrap,内置 admin 只在显式初始化时创建。
            var options = scope.ServiceProvider.GetRequiredService<IOptions<BootstrapOptions>>().Value;
            var seedRunner = scope.ServiceProvider.GetRequiredService<IdentitySeedRunner>();
            await seedRunner.RunAsync(
                new IdentitySeedContext(options.TenantNId, SystemDataOperationNId: null, TraceId: null),
                includeBootstrapAdmin: false,
                stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !stoppingToken.IsCancellationRequested)
        {
            MigrationDeferred(_logger, ex);
        }
    }
}
