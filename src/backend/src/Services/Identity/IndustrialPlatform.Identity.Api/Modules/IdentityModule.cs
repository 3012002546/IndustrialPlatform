using IndustrialPlatform.Identity.Api.Health;
using IndustrialPlatform.Identity.Application;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Infrastructure;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.Identity.Api.Modules;

/// <summary>
/// Identity 模块入口:统一注册 Identity 服务(基础设施/应用/认证/授权/当前用户/HttpClient)
/// 与模块端点映射,供独立 Identity.Api 与 UnifiedHost 复用,避免复制业务实现。
/// 宿主级关注点不在本模块:OpenAPI、MVC(AddIndustrialApi + 路由前缀约定)、健康检查端点映射。
/// 健康检查经 <see cref="AddIdentityHealthChecks"/> 注册,可带前缀避免多模块宿主内检查名冲突
/// (独立宿主不传前缀,检查名保持既有 postgres/redis/seq)。
/// </summary>
public static class IdentityModule
{
    /// <summary>
    /// 注册 Identity 模块全部服务。认证方案(JwtBearer,Identity 自签发/自校验)随本模块注册;
    /// 多模块宿主中即为统一认证方案,后续模块复用同一 Bearer 方案。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <param name="includeStartupMigrationService">
    /// 是否注册 Identity 启动迁移后台服务(默认 true)。UnifiedHost 组合多模块时传 false,
    /// 由宿主级模块迁移协调器确定顺序执行(Identity → SystemData),避免并行迁移同一物理库。
    /// </param>
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeStartupMigrationService = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddIdentityInfrastructure(configuration, includeStartupMigrationService);
        services.AddIdentityApplication(configuration);
        services.AddIdentityAuthentication();
        services.AddIdentityAuthorization();
        services.AddCurrentUser();
        services.AddHttpClient();
        return services;
    }

    /// <summary>
    /// 注册 Identity 模块健康检查。独立宿主不传前缀(检查名 postgres/redis/seq);
    /// 多模块宿主传模块前缀(如 identity),避免与 SystemData/ReferenceData 同名检查互相覆盖。
    /// </summary>
    public static IHealthChecksBuilder AddIdentityHealthChecks(this IHealthChecksBuilder builder, string? namePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .AddCheck<PostgresHealthCheck>(Name(namePrefix, "postgres"), timeout: TimeSpan.FromSeconds(3))
            .AddCheck<RedisHealthCheck>(Name(namePrefix, "redis"), timeout: TimeSpan.FromSeconds(3))
            .AddCheck<SeqHealthCheck>(Name(namePrefix, "seq"), timeout: TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// 映射 Identity 模块端点(不含控制器,控制器由宿主统一 MapControllers)。
    /// 目前为 JWKS 公钥文档:仅供凭据校验,禁止缓存。minimal API,不经 ResultFilter/路由前缀。
    /// </summary>
    public static IEndpointRouteBuilder MapIdentityModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/.well-known/jwks.json", async (HttpContext http, IJwksProvider jwks, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var doc = await jwks.GetAsync(ct);
            return Results.Json(doc);
        });

        return endpoints;
    }

    private static string Name(string? prefix, string check) =>
        string.IsNullOrEmpty(prefix) ? check : $"{prefix}.{check}";
}
