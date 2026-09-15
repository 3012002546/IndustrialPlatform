using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Application.RemoteAssistance;
using IndustrialPlatform.Identity.Application.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>
/// PF06 endpoint authorization. Embedded UnifiedHost uses the public Identity evaluator so a
/// stored endpoint is never authorized by the current HTTP principal; a standalone Collaboration
/// process falls back to its existing signed/current-principal evaluator.
/// </summary>
public sealed class RemoteAssistancePermissionEvaluator : IRemoteAssistancePermissionEvaluator
{
    private readonly IServiceProvider _services;

    public RemoteAssistancePermissionEvaluator(IServiceProvider services) => _services = services;

    public async Task<bool> HasPermissionAsync(
        string permission,
        string tenantNId,
        string userNId,
        string sessionNId,
        string securityVersion,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(securityVersion, out var authVersion)) return false;
        var evaluator = _services.GetService<IPermissionEvaluator>();
        if (evaluator is not null)
        {
            var result = await evaluator.EvaluateAsync(tenantNId, userNId, sessionNId, authVersion, permission, cancellationToken);
            return result.Allowed;
        }
        var fallback = _services.GetRequiredService<ICollaborationPermissionEvaluator>();
        return await fallback.HasPermissionAsync(permission, tenantNId, userNId, sessionNId, securityVersion, cancellationToken);
    }
}
