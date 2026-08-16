using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

/// <summary>
/// SystemData 启动迁移的可复用实现。
/// 供 <see cref="SchemaMigrationBackgroundService"/>(独立 SystemData.Api)与
/// UnifiedHost 的模块迁移协调器复用,避免复制迁移实现。
/// </summary>
public static class SystemDataStartupMigrations
{
    /// <summary>
    /// 执行 SystemData 库迁移。异常原样上抛,由调用方决定降级或失败策略:
    /// 独立宿主后台服务捕获后记录告警跳过;UnifiedHost 协调器顺序执行且失败即启动失败(确定顺序语义)。
    /// </summary>
    public static async Task RunAsync(IServiceScopeFactory scopeFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        using var scope = scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ISchemaMigrationRunner>();
        await runner.ApplyPendingAsync(cancellationToken);
    }
}
