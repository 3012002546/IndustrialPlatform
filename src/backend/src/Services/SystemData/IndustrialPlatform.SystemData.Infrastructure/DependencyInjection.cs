using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.SystemData.Infrastructure.Topology;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.SystemData.Infrastructure;

/// <summary>
/// SystemData 服务基础设施依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 SystemData 服务的基础依赖(SqlSugar/Redis/拓扑选项),配置节见 Api 的 appsettings.Development.json。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddSystemDataInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSqlSugar(configuration);
        services.AddRedis(configuration);

        // 受信任环境的数据库拓扑选项(05 方案 §2.3/§7.1),供拓扑解析与编排使用。
        services.AddOptions<DatabaseTopologyOptions>()
            .Bind(configuration.GetSection(DatabaseTopologyOptions.SectionName));

        // 迁移执行框架与 SystemData 库迁移步骤(TASK-SD-001):运行器按 Id 排序幂等执行。
        services.AddTransient<ISchemaMigrationRunner, SchemaMigrationRunner>();
        foreach (var step in SystemDataSchemaMigrations.All)
        {
            services.AddTransient<SchemaMigrationStep>(_ => step);
        }

        services.AddHostedService<SchemaMigrationBackgroundService>();

        // 数据库编排(TASK-SD-002):持久化端口与可信拓扑提供(TASK-SD-001 的 DatabaseTopologyOptions 已注册)。
        services.AddSingleton<IDatabaseOrchestrationStore, DatabaseOrchestrationStore>();
        services.AddSingleton<IDatabaseTopologyProvider, ConfigurationDatabaseTopologyProvider>();

        return services;
    }
}
