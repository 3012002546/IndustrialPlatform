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
/// 角色管理端点(§16.2):分页/详情/创建/修改/权限分配。
/// 写操作按乐观并发,跨租户资源统一 404;系统角色标记创建后不可变,权限变更失效持有者缓存。
/// </summary>
[ApiController]
[Route("roles")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RolesController : ManagementControllerBase
{
    private readonly IRoleManagementService _service;

    /// <summary>初始化角色管理控制器。</summary>
    public RolesController(IRoleManagementService service, ICurrentUser currentUser)
        : base(currentUser)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>分页查询角色(§16.2 GET /api/v1/roles),NId/Name 包含匹配。</summary>
    [Authorize(Policy = PermissionPolicies.RoleView)]
    [HttpGet]
    public async Task<ActionResult<PageResult<RoleSummary>>> List(
        [FromQuery] string? nId,
        [FromQuery] string? name,
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
            var page = await _service.ListAsync(
                tenantNId,
                new RoleListFilter(tenantNId, nId, name, pageIndex, pageSize),
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

    /// <summary>角色详情(§16.2 GET /api/v1/roles/{id});不存在或跨租户返回 404。</summary>
    [Authorize(Policy = PermissionPolicies.RoleView)]
    [HttpGet("{id}")]
    public async Task<ActionResult<RoleSummary>> Get(string id, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.GetAsync(tenantNId, id, cancellationToken);
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

    /// <summary>创建角色(§16.2 POST /api/v1/roles);NId 冲突 409,无效权限引用 400。</summary>
    [Authorize(Policy = PermissionPolicies.RoleCreate)]
    [HttpPost]
    public async Task<ActionResult<RoleSummary>> Create(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.CreateAsync(tenantNId, actorUserNId, request, cancellationToken);
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

    /// <summary>修改角色名称/描述(§16.2 PUT /api/v1/roles/{id});乐观并发 409。</summary>
    [Authorize(Policy = PermissionPolicies.RoleUpdate)]
    [HttpPut("{id}")]
    public async Task<ActionResult<RoleSummary>> Update(
        string id,
        UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.UpdateAsync(tenantNId, actorUserNId, id, request, cancellationToken);
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

    /// <summary>分配权限(§16.2 PUT /api/v1/roles/{id}/permissions);差量更新并失效持有者权限缓存。</summary>
    [Authorize(Policy = PermissionPolicies.RoleAssignPermission)]
    [HttpPut("{id}/permissions")]
    public async Task<ActionResult<RoleSummary>> AssignPermissions(
        string id,
        AssignRolePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.AssignPermissionsAsync(tenantNId, actorUserNId, id, request, cancellationToken);
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
