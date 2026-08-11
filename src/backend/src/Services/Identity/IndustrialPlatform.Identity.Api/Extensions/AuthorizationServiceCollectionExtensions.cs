using IndustrialPlatform.Identity.Api.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Identity 服务授权扩展(§18):注册权限策略与处理器。
/// 须在 <see cref="AuthenticationServiceCollectionExtensions.AddIdentityAuthentication"/> 之后调用。
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// 注册服务端 RBAC:权限策略(每个目录权限一条)与权限授权处理器。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddIdentityAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization(options => PermissionPolicies.AddPermissionPolicies(options));

        return services;
    }
}
