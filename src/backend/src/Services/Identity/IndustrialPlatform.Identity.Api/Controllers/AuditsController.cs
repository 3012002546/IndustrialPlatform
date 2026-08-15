using IndustrialPlatform.Identity.Api.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Contracts.Management;
using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// 审计查询端点(§16.3/§19.1):登录审计只读、只追加,按租户隔离查询。
/// 响应仅含哈希摘要与快照,不含原始 IP、User-Agent 与敏感请求体。
/// </summary>
[ApiController]
[Route("audits")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuditsController : ManagementControllerBase
{
    private readonly IAuditQueryService _service;

    /// <summary>初始化审计查询控制器。</summary>
    public AuditsController(IAuditQueryService service, ICurrentUser currentUser, IIdempotencyStore idempotencyStore)
        : base(currentUser, idempotencyStore)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>分页查询登录审计(§16.3 GET /api/v1/audits/logins),按发生时间倒序。</summary>
    [Authorize(Policy = PermissionPolicies.AuditLoginView)]
    [HttpGet("logins")]
    public async Task<ActionResult<PageResult<LoginAuditItem>>> Logins(
        [FromQuery] string? userNId,
        [FromQuery] bool? success,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        if (pageIndex < 1 || pageSize is < 1 or > 100)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", "分页参数无效。");
        }

        try
        {
            var page = await _service.QueryLoginAuditsAsync(
                tenantNId,
                new LoginAuditFilter(tenantNId, userNId, success, pageIndex, pageSize),
                cancellationToken);
            return PageResult.Create(page.Items, page.Total, page.PageIndex, page.PageSize);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }
}
