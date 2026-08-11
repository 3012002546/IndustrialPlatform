using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.Identity.Infrastructure;

/// <summary>
/// Identity 服务基础设施依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 Identity 服务的基础依赖(SqlSugar/Redis),配置节见各 Api 的 appsettings.Development.json。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSqlSugar(configuration);
        services.AddRedis(configuration);

        // 迁移执行框架:生产阶段(ID-001)注册零迁移步骤,真实表迁移由 TASK-ID-004 注册。
        services.AddTransient<ISchemaMigrationRunner, SchemaMigrationRunner>();
        services.AddHostedService<SchemaMigrationBackgroundService>();
        return services;
    }
}
