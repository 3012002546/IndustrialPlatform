using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Identity.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Adapters;

/// <summary>聊天目录也必须走同一个外部能力边界，不能直接读 MES 表。</summary>
public sealed class EmbeddedCollaborationIdentityDirectory(IEmbeddedCollaborationAccessAdapter adapter) : ICollaborationIdentityDirectory
{
    public Task<DirectoryUser?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken) => adapter.GetDirectoryUserAsync(tenantNId, userNId, cancellationToken);

    public Task<DirectorySearchPage> SearchAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken) =>
        adapter.SearchDirectoryAsync(tenantNId, actorUserNId, keyword, cursor, pageSize, cancellationToken);
}
