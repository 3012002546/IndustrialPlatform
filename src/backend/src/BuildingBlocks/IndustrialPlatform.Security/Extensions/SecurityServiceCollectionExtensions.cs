using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 当前用户上下文组件依赖注入扩展。
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <see cref="IHttpContextAccessor"/> 与 <see cref="ICurrentUser"/> 服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddCurrentUser(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }
}
