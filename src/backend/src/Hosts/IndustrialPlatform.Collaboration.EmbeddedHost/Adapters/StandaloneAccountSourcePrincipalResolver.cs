using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Adapters;

/// <summary>用独立服务的用户列表核对 URL account；其余身份字段由独立服务生成。</summary>
public sealed class StandaloneAccountSourcePrincipalResolver(
    IEmbeddedCollaborationAccessAdapter users,
    IConfiguration configuration) : IEmbeddedSourcePrincipalResolver
{
    public async Task<EmbeddedSourcePrincipal?> ResolveAsync(
        HttpContext context, string? requestedAccountNId, CancellationToken cancellationToken)
    {
        _ = context;
        if (string.IsNullOrWhiteSpace(requestedAccountNId))
            return null;
        var tenant = configuration["EmbeddedCollaboration:PlatformTenantNId"];
        if (string.IsNullOrWhiteSpace(tenant))
            return null;
        var user = await users.GetDirectoryUserAsync(tenant, requestedAccountNId, cancellationToken);
        if (user is null || !string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
            return null;
        return new EmbeddedSourcePrincipal(
            "standalone-mes", tenant, user.UserNId, user.DisplayName, "1", user.UserNId, user.UserNId);
    }
}
