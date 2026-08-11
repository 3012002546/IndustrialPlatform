using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Authentication;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Identity.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.Identity.Infrastructure;

/// <summary>
/// Identity 服务基础设施依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 Identity 服务的基础依赖(SqlSugar/Redis),配置节见各 Api 的 appsettings.Development.json。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSqlSugar(configuration);
        services.AddRedis(configuration);

        // 迁移执行框架与身份库迁移步骤(TASK-ID-004):9 建表 + 2 种子,运行器按 Id 排序幂等执行。
        services.AddTransient<ISchemaMigrationRunner, SchemaMigrationRunner>();
        foreach (var step in IdentitySchemaMigrations.All)
        {
            services.AddTransient<SchemaMigrationStep>(_ => step);
        }

        services.AddHostedService<SchemaMigrationBackgroundService>();

        // 密码哈希端口 BCrypt 实现与三个持久化仓储(SqlSugarDbContext 为单例,仓储可作单例)。
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IRoleRepository, RoleRepository>();
        services.AddSingleton<IPermissionRepository, PermissionRepository>();

        // 认证用例端口实现(TASK-ID-005):JWT 签发/JWKS/限流/审计/刷新会话。
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<RsaSigningKeyProvider>();
        services.AddSingleton<IAccessTokenFactory, AccessTokenFactory>();
        services.AddSingleton<IJwksProvider, JwksProvider>();
        services.AddSingleton<IAuthenticationStore, AuthenticationStore>();
        services.AddSingleton<ILoginRateLimiter, LoginRateLimiter>();
        services.AddSingleton<ILoginAuditSink, LoginAuditSink>();
        services.AddSingleton<IRefreshSessionStore, RefreshSessionStore>();
        services.AddSingleton<ISessionRevocationStore, SessionRevocationStore>();

        // 服务端授权存储端口(TASK-ID-007):租户校验的授权快照装载 + Redis 版本化权限缓存。
        services.AddSingleton<IAuthorizationDataStore, AuthorizationDataStore>();
        services.AddSingleton<IPermissionCache, PermissionCacheStore>();

        return services;
    }
}
