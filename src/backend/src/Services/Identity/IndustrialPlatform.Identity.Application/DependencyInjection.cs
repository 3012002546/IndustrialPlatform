using IndustrialPlatform.Identity.Application.Authentication;
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

        return services;
    }
}
