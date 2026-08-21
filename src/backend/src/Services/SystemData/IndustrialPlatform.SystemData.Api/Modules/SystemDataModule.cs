using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Api.Health;
using IndustrialPlatform.SystemData.Application;
using IndustrialPlatform.SystemData.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.SystemData.Api.Modules;

/// <summary>
/// SystemData 模块入口:统一注册 SystemData 服务(基础设施/应用/授权/当前用户/HttpClient),
/// 供独立 SystemData.Api 与 UnifiedHost 复用,避免复制业务实现。
/// 注意:令牌校验(JwtBearer,<c>AddSystemDataAuthentication</c>)是宿主级关注点——
/// 独立 SystemData.Api 在 Program.cs 显式调用;UnifiedHost 复用 Identity 模块注册的统一 Bearer 方案
/// (同一签发方/公钥),不再重复注册,避免同名方案被二次配置覆盖。
/// </summary>
public static class SystemDataModule
{
    /// <summary>
    /// 注册 SystemData 模块全部服务(不含 JwtBearer 令牌校验,见类注释)。
    /// 授权策略与处理器随本模块注册,可与 Identity 授权共存(各处理各自的权限需求类型)。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <param name="includeStartupMigrationService">
    /// 是否注册 SystemData 启动迁移后台服务(默认 true)。UnifiedHost 组合多模块时传 false,
    /// 由宿主级模块迁移协调器确定顺序执行(Identity → SystemData),避免并行迁移同一物理库。
    /// </param>
    public static IServiceCollection AddSystemDataModule(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeStartupMigrationService = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSystemDataInfrastructure(configuration, includeStartupMigrationService);
        services.AddEventBus(configuration);
        services.AddSystemDataApplication(configuration);
        services.AddSystemDataAuthorization();
        services.AddCurrentUser();
        services.AddHttpClient();
        return services;
    }

    /// <summary>
    /// 注册 SystemData 模块健康检查。独立宿主不传前缀(检查名 postgres/redis/seq);
    /// 多模块宿主传模块前缀(如 systemdata)。
    /// </summary>
    public static IHealthChecksBuilder AddSystemDataHealthChecks(this IHealthChecksBuilder builder, string? namePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddCheck<PostgresHealthCheck>(Name(namePrefix, "postgres"), timeout: TimeSpan.FromSeconds(3))
            .AddCheck<RedisHealthCheck>(Name(namePrefix, "redis"), timeout: TimeSpan.FromSeconds(3))
            .AddCheck<RabbitMqHealthCheck>(Name(namePrefix, "rabbitmq"), timeout: TimeSpan.FromSeconds(3))
            .AddCheck<OutboxHealthCheck>(Name(namePrefix, "outbox"), timeout: TimeSpan.FromSeconds(3))
            .AddCheck<SeqHealthCheck>(Name(namePrefix, "seq"), timeout: TimeSpan.FromSeconds(3));
    }

    /// <summary>映射 SystemData 模块端点(当前无模块专属 minimal API;控制器由宿主统一 MapControllers)。</summary>
    public static IEndpointRouteBuilder MapSystemDataModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }

    private static string Name(string? prefix, string check) =>
        string.IsNullOrEmpty(prefix) ? check : $"{prefix}.{check}";
}
