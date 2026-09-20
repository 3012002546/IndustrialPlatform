using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Identity.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Adapters;

/// <summary>用户目录未接线时返回空结果，不推断 MES 中的用户。</summary>
public sealed class NotConfiguredEmbeddedCollaborationAccessAdapter : IEmbeddedCollaborationAccessAdapter
{
    public Task<DirectoryUser?> GetDirectoryUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken) =>
        Task.FromResult<DirectoryUser?>(null);

    public Task<DirectorySearchPage> SearchDirectoryAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new DirectorySearchPage([], null));

}
