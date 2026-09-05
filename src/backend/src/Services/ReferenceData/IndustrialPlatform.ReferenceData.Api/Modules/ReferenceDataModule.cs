using IndustrialPlatform.ReferenceData.Api.Health;
using IndustrialPlatform.ReferenceData.Api.Endpoints;
using IndustrialPlatform.ReferenceData.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.ReferenceData.Api.Modules;

/// <summary>
/// ReferenceData 模块入口:统一注册 ReferenceData 服务(基础设施/HttpClient),
/// 供独立 ReferenceData.Api 与 UnifiedHost 复用,避免复制业务实现。
/// RabbitMQ/Seq 等可选依赖继续服从既有 Enabled 配置,不阻塞 core profile 启动。
/// </summary>
public static class ReferenceDataModule
{
    /// <summary>注册 ReferenceData 模块全部服务。</summary>
    public static IServiceCollection AddReferenceDataModule(this IServiceCollection services, IConfiguration configuration, bool unifiedHost = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddReferenceDataInfrastructure(configuration);
        services.AddHttpClient();
        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddCurrentUser();
        services.AddSingleton(provider => new IndustrialPlatform.ReferenceData.Api.Initialization.ReferenceDataHostContext(
            configuration, provider.GetRequiredService<IHostEnvironment>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<IndustrialPlatform.Infrastructure.Database.SqlSugarOptions>>(), unifiedHost));
        return services;
    }

    /// <summary>
    /// 注册 ReferenceData 模块健康检查。独立宿主不传前缀(检查名 postgres/redis/rabbitmq/seq);
    /// 多模块宿主传模块前缀(如 referencedata)。
    /// </summary>
    public static IHealthChecksBuilder AddReferenceDataHealthChecks(this IHealthChecksBuilder builder, string? namePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddCheck<PostgresHealthCheck>(Name(namePrefix, "postgres"), tags: ["ready"], timeout: TimeSpan.FromSeconds(3))
            .AddCheck<InitializationHealthCheck>(Name(namePrefix, "initialization"), tags: ["ready"], timeout: TimeSpan.FromSeconds(3))
            .AddCheck<RedisHealthCheck>(Name(namePrefix, "redis"), failureStatus: HealthStatus.Degraded, tags: ["capability"], timeout: TimeSpan.FromSeconds(3))
            .AddCheck<RabbitMqHealthCheck>(Name(namePrefix, "rabbitmq"), failureStatus: HealthStatus.Degraded, tags: ["capability"], timeout: TimeSpan.FromSeconds(3))
            .AddCheck<SeqHealthCheck>(Name(namePrefix, "seq"), failureStatus: HealthStatus.Degraded, tags: ["capability"], timeout: TimeSpan.FromSeconds(3));
    }

    /// <summary>映射 ReferenceData 模块端点。</summary>
    public static IEndpointRouteBuilder MapReferenceDataModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapDictionaryEndpoints();
        endpoints.MapParameterEndpoints();
        endpoints.MapDynamicConfigurationEndpoints();
        endpoints.MapUnitOfMeasureEndpoints();
        endpoints.MapMetadataEndpoints();
        endpoints.MapCodingRuleEndpoints();
        endpoints.MapStateMachineEndpoints();
        return endpoints;
    }

    private static string Name(string? prefix, string check) =>
        string.IsNullOrEmpty(prefix) ? check : $"{prefix}.{check}";
}
