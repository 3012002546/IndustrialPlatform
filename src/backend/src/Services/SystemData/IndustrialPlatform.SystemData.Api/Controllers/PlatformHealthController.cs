using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Application.PlatformHealth;
using IndustrialPlatform.SystemData.Contracts.PlatformHealth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Controllers;

[ApiController, Route("platform-health"), Authorize]
public sealed class PlatformHealthController(IPlatformHealthQuery query, ICurrentUser currentUser) : SystemDataControllerBase(currentUser)
{
    [HttpGet("summary"), Authorize(Policy = SystemDataPermissionPolicies.PlatformHealthView)]
    public Task<ActionResult<PlatformHealthSummary>> Summary(string? serviceNId, CancellationToken cancellationToken) => Read(serviceNId, cancellationToken);
    [HttpGet("workspace"), Authorize(Policy = SystemDataPermissionPolicies.PlatformHealthWorkspaceView)]
    public Task<ActionResult<PlatformHealthSummary>> Workspace(CancellationToken cancellationToken) => Read(null, cancellationToken);
    private async Task<ActionResult<PlatformHealthSummary>> Read(string? serviceNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out _)) return UnauthorizedEnvelope();
        return Ok(await query.GetSummaryAsync(tenant, serviceNId, cancellationToken));
    }
}
