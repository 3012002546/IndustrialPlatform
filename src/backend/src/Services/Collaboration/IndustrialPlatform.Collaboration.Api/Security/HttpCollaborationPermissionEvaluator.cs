using System.Security.Claims;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace IndustrialPlatform.Collaboration.Api.Security;

public sealed class HttpCollaborationPermissionEvaluator : ICollaborationPermissionEvaluator
{
    private readonly IAuthorizationService _authorization;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCollaborationPermissionEvaluator(IAuthorizationService authorization, IHttpContextAccessor httpContextAccessor)
    {
        _authorization = authorization;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<bool> HasPermissionAsync(string permission, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || !Matches(principal, ClaimConstants.TenantId, tenantNId)
            || !Matches(principal, ClaimConstants.UserNId, actorUserNId)
            || !Matches(principal, ClaimConstants.SessionId, actorSessionNId)
            || !Matches(principal, ClaimConstants.AuthVersion, actorSecurityVersion))
            return false;

        try
        {
            return (await _authorization.AuthorizeAsync(principal, $"permission:{permission}")).Succeeded;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            return false;
        }
    }

    public async Task<bool> IsSystemAdministratorAsync(string tenantNId, string actorUserNId,
        string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        return principal?.HasClaim(ClaimConstants.Role, "SYSTEM_ADMIN") == true
            && await HasPermissionAsync("collaboration.compliance.export.approve", tenantNId,
                actorUserNId, actorSessionNId, actorSecurityVersion, cancellationToken);
    }

    private static bool Matches(ClaimsPrincipal principal, string claimType, string expected) =>
        !string.IsNullOrWhiteSpace(expected) && string.Equals(principal.FindFirst(claimType)?.Value, expected, StringComparison.Ordinal);
}
