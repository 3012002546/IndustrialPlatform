using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Organizations;
using IndustrialPlatform.SystemData.Contracts.Administration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Controllers;

/// <summary>
/// 行政组织控制面端点(05 方案 §9.3 /api/v1/organizations/**):组织森林、详情、
/// 创建、改名、移动预览/提交与启用/停用。租户与执行者标识只从当前用户上下文读取,
/// 绝不信任请求体传入;错误统一 §9.9 信封。响应不暴露数据库 Guid。
/// </summary>
[ApiController]
[Route("organizations")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OrganizationsController : SystemDataControllerBase
{
    private readonly IAdministrativeOrganizationService _service;

    /// <summary>初始化行政组织控制面控制器。</summary>
    public OrganizationsController(IAdministrativeOrganizationService service, ICurrentUser currentUser)
        : base(currentUser)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>读取组织森林(GET /api/v1/organizations/tree),可按状态过滤。</summary>
    [HttpGet("tree")]
    [Authorize(Policy = SystemDataPermissionPolicies.OrganizationView)]
    public async Task<ActionResult<IReadOnlyList<OrganizationNodeV1>>> GetTree(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return (await _service.GetTreeAsync(tenantNId, status, cancellationToken)).ToList();
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (AdministrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>读取组织详情与并发版本(GET /api/v1/organizations/{organizationNId});跨租户 404。</summary>
    [HttpGet("{organizationNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.OrganizationView)]
    public async Task<ActionResult<OrganizationDetailV1>> Get(
        string organizationNId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.GetAsync(tenantNId, organizationNId, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (AdministrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>创建根公司或子组织(POST /api/v1/organizations)。</summary>
    [HttpPost]
    [Authorize(Policy = SystemDataPermissionPolicies.OrganizationCreate)]
    public async Task<ActionResult<OrganizationDetailV1>> Create(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.CreateAsync(tenantNId, actorUserNId, HttpContext.TraceIdentifier, request, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (AdministrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>修改组织名称、顺序(PUT /api/v1/organizations/{organizationNId});双版本不匹配 409。</summary>
    [HttpPut("{organizationNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.OrganizationUpdate)]
    public async Task<ActionResult<OrganizationDetailV1>> Update(
        string organizationNId,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.UpdateAsync(tenantNId, actorUserNId, HttpContext.TraceIdentifier, organizationNId, request, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (AdministrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>移动预览(POST /api/v1/organizations/{organizationNId}/move-preview):返回影响摘要与提交必需的 revision/双版本。</summary>
    [HttpPost("{organizationNId}/move-preview")]
    [Authorize(Policy = SystemDataPermissionPolicies.OrganizationMove)]
    public async Task<ActionResult<OrganizationMovePreviewV1>> PreviewMove(
        string organizationNId,
        MoveOrganizationPreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.PreviewMoveAsync(tenantNId, organizationNId, request, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (AdministrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>提交移动(POST /api/v1/organizations/{organizationNId}/move);预览过期、循环或版本不匹配 409。</summary>
    [HttpPost("{organizationNId}/move")]
    [Authorize(Policy = SystemDataPermissionPolicies.OrganizationMove)]
    public async Task<ActionResult<OrganizationDetailV1>> Move(
        string organizationNId,
        MoveOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.MoveAsync(tenantNId, actorUserNId, HttpContext.TraceIdentifier, organizationNId, request, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (AdministrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>启用/停用组织(PUT /api/v1/organizations/{organizationNId}/status);有活动依赖停用拒绝 409。</summary>
    [HttpPut("{organizationNId}/status")]
    [Authorize(Policy = SystemDataPermissionPolicies.OrganizationStatus)]
    public async Task<ActionResult<OrganizationDetailV1>> SetStatus(
        string organizationNId,
        SetOrganizationStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.SetStatusAsync(tenantNId, actorUserNId, HttpContext.TraceIdentifier, organizationNId, request, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (AdministrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }
}
