using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Application.Sso;
using IndustrialPlatform.Identity.Application.UserGroups;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.Identity.Application;

/// <summary>
/// Identity 应用层依赖注入扩展:绑定认证配置并注册用例服务。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册应用层服务(配置节 <c>Identity:Authentication</c>)。认证端口实现由基础设施注册。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AuthenticationOptions>()
            .Bind(configuration.GetSection("Identity:Authentication"));
        services.AddSingleton<IAuthenticationService, AuthenticationService>();

        // 服务端授权(TASK-ID-007):评估器、拒绝审计与授权配置;数据/缓存端口由基础设施注册。
        services.AddOptions<AuthorizationOptions>()
            .Bind(configuration.GetSection(AuthorizationOptions.SectionName));
        services.AddSingleton<IPermissionEvaluator, PermissionEvaluator>();
        services.AddSingleton<IAuthorizationDenialSink, AuthorizationDenialSink>();

        // 管理用例(TASK-ID-008):用户/角色/权限/审计;持久化端口由基础设施注册。
        services.AddSingleton<IUserManagementService, UserManagementService>();
        services.AddSingleton<IRoleManagementService, RoleManagementService>();
        services.AddSingleton<IPermissionQueryService, PermissionQueryService>();
        services.AddSingleton<IAuditQueryService, AuditQueryService>();

        // 用户组用例(TASK-ID-017):用户组管理编排;持久化端口由基础设施注册。
        services.AddSingleton<IUserGroupService, UserGroupService>();

        // 企业级联合登录(TASK-ID-013):SSO 用例与管理用例;存储/票据/适配器端口由基础设施注册。
        services.AddOptions<SsoOptions>()
            .Bind(configuration.GetSection(SsoOptions.SectionName));
        services.AddSingleton<ISsoService, SsoService>();
        services.AddSingleton<ISsoManagementService, SsoManagementService>();

        return services;
    }
}
