using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Identity.Application.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Authorization;

/// <summary>独立宿主使用固定协作权限目录；由独立宿主注册，不改变平台默认权限判断器。</summary>
public sealed class EmbeddedPermissionEvaluator : IPermissionEvaluator
{
    public Task<PermissionEvaluation> EvaluateAsync(string tenantNId, string userNId, string? sessionNId, int authVersion, string requiredPermissionNId, CancellationToken cancellationToken)
    {
        _ = tenantNId;
        _ = userNId;
        _ = cancellationToken;
        var allowed = authVersion > 0 && !string.IsNullOrWhiteSpace(sessionNId)
            && EmbeddedCollaborationPermissionCatalog.IsAllowed(requiredPermissionNId);
        return Task.FromResult(new PermissionEvaluation(allowed, allowed ? AuthorizationDenialReason.None : AuthorizationDenialReason.MissingPermission));
    }
}
