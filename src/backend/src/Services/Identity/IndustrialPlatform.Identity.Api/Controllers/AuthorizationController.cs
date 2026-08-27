using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Contracts.Authorization;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>供其他服务在独立部署模式下复用 Identity 权限裁决。</summary>
[ApiController]
[Route("authorization")]
[Authorize]
public sealed class AuthorizationController : ControllerBase
{
    private readonly IPermissionEvaluator _evaluator;

    public AuthorizationController(IPermissionEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    /// <summary>根据已认证令牌中的用户、租户、会话和安全版本评估一个权限。</summary>
    [HttpPost("evaluate")]
    public async Task<ActionResult<EvaluatePermissionResponse>> Evaluate(
        EvaluatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PermissionNId))
        {
            return BadRequest();
        }

        var userNId = User.FindFirst(ClaimConstants.UserNId)?.Value;
        var tenantNId = User.FindFirst(ClaimConstants.TenantId)?.Value;
        var sessionNId = User.FindFirst(ClaimConstants.SessionId)?.Value;
        var version = User.FindFirst(ClaimConstants.AuthVersion)?.Value;
        if (string.IsNullOrWhiteSpace(userNId)
            || string.IsNullOrWhiteSpace(tenantNId)
            || !int.TryParse(version, out var authVersion))
        {
            return Unauthorized();
        }

        try
        {
            var evaluation = await _evaluator.EvaluateAsync(
                tenantNId,
                userNId,
                sessionNId,
                authVersion,
                request.PermissionNId,
                cancellationToken);
            return Ok(new EvaluatePermissionResponse(evaluation.Allowed, evaluation.Reason.ToString()));
        }
        catch (SecurityStoreUnavailableException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new EvaluatePermissionResponse(false, AuthorizationDenialReason.SecurityStoreUnavailable.ToString()));
        }
    }
}
