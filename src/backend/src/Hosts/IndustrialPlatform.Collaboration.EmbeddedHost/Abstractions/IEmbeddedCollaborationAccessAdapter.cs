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
/// Collaboration 需要的外部用户目录契约。实现者可调用 MES 用户接口，
/// 也可调用独立的受信任后端适配服务；当前登录身份由单独的登录适配器核验。
/// </summary>
public interface IEmbeddedCollaborationAccessAdapter
{
    Task<DirectoryUser?> GetDirectoryUserAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken);

    Task<DirectorySearchPage> SearchDirectoryAsync(
        string tenantNId,
        string actorUserNId,
        string keyword,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken);
}
