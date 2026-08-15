using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Application.Sso;
using IndustrialPlatform.Identity.Application.UserGroups;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Authentication;
using IndustrialPlatform.Identity.Infrastructure.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Management;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Outbox;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using IndustrialPlatform.Identity.Infrastructure.Security;
using IndustrialPlatform.Identity.Infrastructure.Sso;
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
        services.AddEventBus(configuration);

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
        services.AddSingleton<IUserGroupRepository, UserGroupRepository>();

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

        // 管理用例持久化端口(TASK-ID-008):管理存储/操作审计/登录审计查询。
        services.AddSingleton<IManagementStore, ManagementStore>();
        services.AddSingleton<IOperationAuditSink, OperationAuditSink>();
        services.AddSingleton<ILoginAuditQueryStore, LoginAuditQueryStore>();

        // 用户组持久化端口(TASK-ID-017):用户组存储(聚合装载/原子写/授权求值辅助查询)。
        services.AddSingleton<IUserGroupStore, UserGroupStore>();

        // Outbox 发布管线(TASK-ID-009):后台发布器轮询未发布事件转发到 RabbitMQ,
        // RabbitMQ 不可达时退避等待,不阻塞服务启动(保持无 Docker 可运行基线)。
        services.AddHostedService<OutboxDispatcherBackgroundService>();

        // SSO 持久化与外部 IdP 适配器(TASK-ID-013):Provider/账号/Client/浏览器会话存储、
        // Redis 一次性票据存储、密钥解析、OIDC/SAML 适配器与协议工厂。
        services.AddSingleton<ISsoStore, SsoStore>();
        services.AddSingleton<ISsoTicketStore, SsoTicketStore>();
        services.AddSingleton<ISsoSecretResolver, ConfigurationSsoSecretResolver>();
        services.AddSingleton<IExternalIdentityProvider, OidcExternalIdentityProvider>();
        services.AddSingleton<IExternalIdentityProvider, Saml2ExternalIdentityProvider>();
        services.AddSingleton<IExternalIdentityProviderFactory, ExternalIdentityProviderFactory>();

        // bootstrap 编排(TASK-ID-019,§29A.4):凭据交付存储、admin/状态存储、随机密码生成、
        // 三层种子执行器与显式初始化编排。明文临时密码只在受保护结果中出现一次。
        services.AddSingleton<IBootstrapCredentialStore, BootstrapCredentialStore>();
        services.AddSingleton<IBootstrapStore, BootstrapStore>();
        services.AddSingleton<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();
        services.AddSingleton<IdentitySeedRunner>();
        services.AddSingleton<IdentityInitializationService>();

        return services;
    }
}
