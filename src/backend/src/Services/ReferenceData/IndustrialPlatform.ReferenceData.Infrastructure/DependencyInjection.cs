using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Caching;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Infrastructure.Caching;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.ReferenceData.Infrastructure.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

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
        services.AddSingleton<IReferenceDataCache>(serviceProvider => new RedisReferenceDataCache(
            serviceProvider.GetRequiredService<IConnectionMultiplexer>(),
            serviceProvider.GetRequiredService<IOptions<RedisOptions>>(),
            serviceProvider.GetRequiredService<ILogger<RedisReferenceDataCache>>(),
            serviceProvider.GetRequiredService<SqlSugarDbContext>()));
        services.AddEventBus(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ReferenceDataOutboxStore>();
        services.AddHostedService<ReferenceDataOutboxDispatcher>();
        services.AddSingleton<ReferenceDataInitializationLedger>();
        services.AddHttpClient();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.Dictionary.IDictionaryRepository, Dictionary.DictionaryRepository>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.Dictionary.DictionaryService>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.Parameter.IParameterRepository, Parameter.ParameterRepository>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.Parameter.ParameterService>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.DynamicProperty.IDynamicConfigurationRepository, DynamicProperty.DynamicConfigurationRepository>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.DynamicProperty.DynamicConfigurationService>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.UnitOfMeasure.IUnitDimensionRepository, UnitOfMeasure.UnitDimensionRepository>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.UnitOfMeasure.UnitDimensionService>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.Metadata.IMetadataSchemaRepository, Metadata.MetadataSchemaRepository>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.Metadata.MetadataSchemaService>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.CodingRule.ICodingRuleRepository, CodingRule.CodingRuleRepository>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.CodingRule.CodingRuleService>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.StateMachine.IStateMachineRepository, StateMachine.StateMachineRepository>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.StateMachine.StateMachineService>();
        services.AddScoped<IndustrialPlatform.ReferenceData.Application.Authorization.IReferenceDataPermissionEvaluator,
            IndustrialPlatform.ReferenceData.Infrastructure.Authorization.HttpReferenceDataPermissionEvaluator>();
        services.AddSingleton<ReferenceDataServiceInitializer>();
        services.AddSingleton<IServiceInitializer>(sp => sp.GetRequiredService<ReferenceDataServiceInitializer>());
        return services;
    }
}
