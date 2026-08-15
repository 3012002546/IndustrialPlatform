using IndustrialPlatform.SystemData.Api.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// SystemData 服务授权扩展(TASK-SD-006,§9.6):注册权限策略与令牌声明授权处理器。
/// 须在 <see cref="SystemDataAuthenticationServiceCollectionExtensions.AddSystemDataAuthentication"/> 之后调用。
/// </summary>
public static class SystemDataAuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// 注册服务端 RBAC:权限策略(每个已登记目录权限一条)与权限授权处理器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddSystemDataAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAuthorizationHandler, SystemDataPermissionAuthorizationHandler>();
        services.AddAuthorization(options => SystemDataPermissionPolicies.AddPermissionPolicies(options));

        return services;
    }
}
