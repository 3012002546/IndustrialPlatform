using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.Infrastructure;

internal static class CollaborationHttpActorContext
{
    public static (string SessionNId, string SecurityVersion) Require(
        IHttpContextAccessor accessor,
        IConfiguration configuration,
        string tenantNId,
        string actorUserNId)
    {
        var principal = accessor.HttpContext?.User;
        var claimTenant = principal?.FindFirst(ClaimConstants.TenantId)?.Value;
        var claimActor = principal?.FindFirst(ClaimConstants.UserNId)?.Value;
        var session = principal?.FindFirst(ClaimConstants.SessionId)?.Value;
        var version = principal?.FindFirst(ClaimConstants.AuthVersion)?.Value;
        if (principal?.Identity?.IsAuthenticated == true
            && string.Equals(claimTenant, tenantNId, StringComparison.Ordinal)
            && string.Equals(claimActor, actorUserNId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(session)
            && !string.IsNullOrWhiteSpace(version))
            return (session, version);

        var workerSession = configuration["Collaboration:ServiceIdentity:WorkerSessionNId"];
        var workerVersion = configuration["Collaboration:ServiceIdentity:WorkerSecurityVersion"];
        if (!string.IsNullOrWhiteSpace(workerSession) && !string.IsNullOrWhiteSpace(workerVersion))
            return (workerSession, workerVersion);

        throw new CollaborationException(401, "COLLAB_SESSION_INVALID", "当前登录会话已失效，请重新登录。");
    }
}
