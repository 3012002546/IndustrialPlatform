using IndustrialPlatform.ReferenceData.Api.Health;
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
    /// <summary>注册 ReferenceData 模块全部服务(当前无认证/授权/控制器)。</summary>
    public static IServiceCollection AddReferenceDataModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddReferenceDataInfrastructure(configuration);
        services.AddHttpClient();
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
            .AddCheck<PostgresHealthCheck>(Name(namePrefix, "postgres"), timeout: TimeSpan.FromSeconds(3))
            .AddCheck<RedisHealthCheck>(Name(namePrefix, "redis"), timeout: TimeSpan.FromSeconds(3))
            .AddCheck<RabbitMqHealthCheck>(Name(namePrefix, "rabbitmq"), timeout: TimeSpan.FromSeconds(3))
            .AddCheck<SeqHealthCheck>(Name(namePrefix, "seq"), timeout: TimeSpan.FromSeconds(3));
    }

    /// <summary>映射 ReferenceData 模块端点(当前无模块专属 minimal API)。</summary>
    public static IEndpointRouteBuilder MapReferenceDataModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }

    private static string Name(string? prefix, string check) =>
        string.IsNullOrEmpty(prefix) ? check : $"{prefix}.{check}";
}
