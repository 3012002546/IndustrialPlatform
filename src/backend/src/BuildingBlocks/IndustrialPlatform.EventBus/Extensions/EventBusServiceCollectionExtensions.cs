using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Connection;
using IndustrialPlatform.EventBus.Consumer;
using IndustrialPlatform.EventBus.Events;
using IndustrialPlatform.EventBus.Options;
using IndustrialPlatform.EventBus.Producer;
using IndustrialPlatform.EventBus.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// RabbitMQ 事件总线依赖注入扩展。
/// </summary>
public static class EventBusServiceCollectionExtensions
{
    private static readonly EventBusSubscriptionsManager SubscriptionsManager = new();

    /// <summary>
    /// 注册 RabbitMQ 事件总线(发布器、连接、订阅管理、消费后台服务)。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">RabbitMQ 配置委托。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddEventBus(this IServiceCollection services, Action<RabbitMqOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<RabbitMqOptions>().Configure(configure);
        return AddEventBusCore(services);
    }

    /// <summary>
    /// 从配置的 "RabbitMQ" 节点绑定选项并注册事件总线。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddEventBus(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection("RabbitMQ"));
        return AddEventBusCore(services);
    }

    /// <summary>
    /// 注册集成事件消费者,并登记事件订阅关系。
    /// </summary>
    /// <typeparam name="TEvent">事件类型。</typeparam>
    /// <typeparam name="TConsumer">消费者类型。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddIntegrationEventConsumer<TEvent, TConsumer>(this IServiceCollection services)
        where TEvent : IntegrationEvent
        where TConsumer : class, IIntegrationEventConsumer<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        SubscriptionsManager.AddSubscription<TEvent, TConsumer>();
        services.AddScoped<TConsumer>();
        return services;
    }

    private static IServiceCollection AddEventBusCore(IServiceCollection services)
    {
        // TryAdd/TryAddEnumerable:多模块宿主(UnifiedHost)中 Identity 与 ReferenceData 各自调用
        // AddEventBus 时保持单注册,避免重复连接复用器、重复 IEventBus 与重复消费后台服务。
        services.TryAddSingleton<IEventBusSubscriptionsManager>(SubscriptionsManager);
        services.TryAddSingleton<IRabbitMqConnection, RabbitMqConnection>();
        services.TryAddSingleton<IEventBus, RabbitMqEventBus>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EventBusConsumerBackgroundService>());
        return services;
    }
}
