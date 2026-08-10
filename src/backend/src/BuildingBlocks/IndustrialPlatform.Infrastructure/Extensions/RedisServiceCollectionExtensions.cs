using IndustrialPlatform.Infrastructure.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Redis 基础组件依赖注入扩展。
/// </summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Redis 连接、缓存服务与分布式锁。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">Redis 配置委托。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddRedis(this IServiceCollection services, Action<RedisOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<RedisOptions>().Configure(configure);
        return AddRedisCore(services);
    }

    /// <summary>
    /// 从配置的 "Redis" 节点绑定选项并注册 Redis 组件。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddRedis(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RedisOptions>().Bind(configuration.GetSection("Redis"));
        return AddRedisCore(services);
    }

    private static IServiceCollection AddRedisCore(IServiceCollection services)
    {
        // AbortOnConnectFail=false:Redis 暂时不可达时返回断开的连接复用器而非抛异常,
        // 保证服务启动/健康检查期间缓存降级而不是整站 500;连接在后台持续重试。
        services.AddSingleton<IConnectionMultiplexer>(static sp =>
        {
            var configuration = ConfigurationOptions.Parse(sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString);
            configuration.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(configuration);
        });
        services.AddSingleton<ICacheService, CacheService>();
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        return services;
    }
}
