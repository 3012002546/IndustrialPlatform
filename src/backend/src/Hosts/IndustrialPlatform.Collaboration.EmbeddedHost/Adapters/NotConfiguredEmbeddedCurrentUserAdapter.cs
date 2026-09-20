using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Identity.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Adapters;

/// <summary>
/// 默认实现故意不猜测 MES API，也不接受客户端自报身份。部署者应替换 DI 注册；
/// 没有真实当前登录适配器时，挑战不能签发断言。
/// </summary>
public sealed class NotConfiguredEmbeddedCurrentUserAdapter : IEmbeddedCurrentUserAdapter
{
    public Task<EmbeddedSourcePrincipal?> ResolveAsync(HttpContext context, CancellationToken cancellationToken) =>
        ResolveAsync(context, null, cancellationToken);

    public Task<EmbeddedSourcePrincipal?> ResolveAsync(HttpContext context, string? requestedAccountNId, CancellationToken cancellationToken) =>
        Task.FromException<EmbeddedSourcePrincipal?>(new EmbeddedHandshakeException(
            StatusCodes.Status503ServiceUnavailable,
            "EMBEDDED_SOURCE_ADAPTER_NOT_CONFIGURED",
            "MES 当前登录适配器尚未配置。"));
}
