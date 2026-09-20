using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Identity.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Adapters;

/// <summary>把可替换的 MES 当前登录适配器接入既有握手协议。</summary>
public sealed class AdapterBackedEmbeddedSourcePrincipalResolver(IEmbeddedCurrentUserAdapter adapter)
    : IEmbeddedSourcePrincipalResolver
{
    public Task<EmbeddedSourcePrincipal?> ResolveAsync(HttpContext context, string? requestedAccountNId, CancellationToken cancellationToken) =>
        adapter.ResolveAsync(context, requestedAccountNId, cancellationToken);
}
