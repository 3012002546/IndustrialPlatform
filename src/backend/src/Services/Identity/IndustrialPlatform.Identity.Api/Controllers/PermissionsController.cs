using IndustrialPlatform.Identity.Api.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Contracts.Management;
using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// 权限目录查询端点(§16.3):只读权限目录树。权限为平台级数据,不按租户隔离;
/// 目录树由 ParentPermissionNId 组织,节点按规范化标识升序。
/// </summary>
[ApiController]
[Route("permissions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PermissionsController : ManagementControllerBase
{
    private readonly IPermissionQueryService _service;

    /// <summary>初始化权限目录查询控制器。</summary>
    public PermissionsController(IPermissionQueryService service, ICurrentUser currentUser)
        : base(currentUser)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>权限目录树(§16.3 GET /api/v1/permissions/tree)。</summary>
    [Authorize(Policy = PermissionPolicies.PermissionView)]
    [HttpGet("tree")]
    public async Task<ActionResult<IReadOnlyList<PermissionTreeNode>>> Tree(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.GetTreeAsync(cancellationToken));
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
