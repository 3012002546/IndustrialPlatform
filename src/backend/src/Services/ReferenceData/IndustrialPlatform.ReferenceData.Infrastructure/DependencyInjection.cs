using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.ReferenceData.Infrastructure;

/// <summary>
/// ReferenceData 服务基础设施依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 ReferenceData 服务的基础依赖(SqlSugar/Redis/RabbitMQ),配置节见各 Api 的 appsettings.Development.json。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddReferenceDataInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSqlSugar(configuration);
        services.AddRedis(configuration);
        services.AddEventBus(configuration);
        return services;
    }
}
