using System.Security.Claims;
using IndustrialPlatform.Identity.Api.Authorization;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// Identity 有效刷新会话管理端点。返回只读摘要，不返回 token、IP 或 User-Agent。
/// </summary>
[ApiController]
[Route("sessions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SessionsController : ManagementControllerBase
{
    private readonly IIdentitySessionManagementService _service;

    public SessionsController(
        IIdentitySessionManagementService service,
        ICurrentUser currentUser,
        IIdempotencyStore idempotencyStore)
        : base(currentUser, idempotencyStore)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>列出租户内有效登录会话；有效不代表页面实时活跃。</summary>
    [Authorize(Policy = PermissionPolicies.SessionView)]
    [HttpGet("active")]
    public async Task<ActionResult<PageResult<IdentityActiveSession>>> Active(CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _)) return UnauthorizedEnvelope();
        var items = await _service.ListActiveAsync(
            tenantNId,
            User.FindFirstValue(ClaimConstants.SessionId),
            cancellationToken);
        return PageResult.Create(items, items.Count, 1, Math.Max(items.Count, 1));
    }

    /// <summary>租户内撤销一个刷新会话；目标不存在或重复撤销保持幂等。</summary>
    [Authorize(Policy = PermissionPolicies.SessionRevoke)]
    [HttpPost("{sessionNId}/revoke")]
    public async Task<ActionResult<IdentitySessionRevokeResult>> Revoke(
        string sessionNId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId)) return UnauthorizedEnvelope();
        if (string.IsNullOrWhiteSpace(sessionNId)) return BadRequestEnvelope("ID_VALIDATION_FAILED", "会话标识不能为空。");

        var request = new { sessionNId = sessionNId.Trim() };
        try
        {
            var result = await ExecuteIdempotentAsync(
                tenantNId,
                actorUserNId,
                request,
                () => _service.RevokeAsync(
                    tenantNId,
                    sessionNId,
                    User.FindFirstValue(ClaimConstants.SessionId),
                    cancellationToken),
                cancellationToken);
            return result ?? new IdentitySessionRevokeResult(true, false);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
    }
}
