using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Identity.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Abstractions;

/// <summary>
/// MES 当前登录适配入口。实现者应在服务端读取现有 MES 的当前用户和会话接口，
/// 再映射稳定的来源、外部租户、用户主体、会话标识和安全版本。
/// 浏览器不能从请求体自报这些字段；未实现时必须保持失败关闭。
/// </summary>
public interface IEmbeddedCurrentUserAdapter
{
    /// <summary>
    /// Reads the already authenticated MES user server-side. If an account was
    /// supplied, the adapter must compare it with that MES user's account and
    /// return a principal carrying the verified value; it must not trust the
    /// browser's value as an identity claim.
    /// </summary>
    Task<EmbeddedSourcePrincipal?> ResolveAsync(HttpContext context, string? requestedAccountNId, CancellationToken cancellationToken);
}
