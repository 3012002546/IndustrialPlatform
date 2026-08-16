using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// Identity 启动迁移与系统目录种子的可复用实现:Schema migration → SystemCatalogSeed → TenantSecuritySeed
/// (不含 SecretBootstrap,内置 admin 只由显式初始化编排创建,§29A.4)。
/// 供 <see cref="SchemaMigrationBackgroundService"/>(独立 Identity.Api)与
/// UnifiedHost 的模块迁移协调器复用,避免复制迁移/种子实现。
/// </summary>
public static class IdentityStartupMigrations
{
    /// <summary>
    /// 执行启动迁移与系统目录种子。异常原样上抛,由调用方决定降级或失败策略:
    /// 独立宿主后台服务捕获后记录告警跳过;UnifiedHost 协调器顺序执行且失败即启动失败(确定顺序语义)。
    /// </summary>
    public static async Task RunAsync(IServiceScopeFactory scopeFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        using var scope = scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ISchemaMigrationRunner>();
        await runner.ApplyPendingAsync(cancellationToken);

        var options = scope.ServiceProvider.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        var seedRunner = scope.ServiceProvider.GetRequiredService<IdentitySeedRunner>();
        await seedRunner.RunAsync(
            new IdentitySeedContext(options.TenantNId, SystemDataOperationNId: null, TraceId: null),
            includeBootstrapAdmin: false,
            cancellationToken);
    }
}
