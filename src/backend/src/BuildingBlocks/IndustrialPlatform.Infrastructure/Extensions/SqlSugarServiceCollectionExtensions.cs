using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Infrastructure.Repository;
using IndustrialPlatform.Infrastructure.Transaction;
using IndustrialPlatform.SharedKernel.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// SqlSugar 基础组件依赖注入扩展。
/// </summary>
public static class SqlSugarServiceCollectionExtensions
{
    /// <summary>
    /// 注册 SqlSugar 数据库上下文、通用仓储与工作单元。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">数据库配置委托。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddSqlSugar(this IServiceCollection services, Action<SqlSugarOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<SqlSugarOptions>().Configure(configure);
        return AddSqlSugarCore(services);
    }

    /// <summary>
    /// 从配置的 "SqlSugar" 节点绑定选项并注册 SqlSugar 组件。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddSqlSugar(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SqlSugarOptions>().Bind(configuration.GetSection("SqlSugar"));
        return AddSqlSugarCore(services);
    }

    private static IServiceCollection AddSqlSugarCore(IServiceCollection services)
    {
        services.AddSingleton<SqlSugarDbContext>();
        services.AddScoped(typeof(IRepository<>), typeof(BaseRepository<>));
        services.AddScoped<IUnitOfWork, SqlSugarUnitOfWork>();
        return services;
    }
}
