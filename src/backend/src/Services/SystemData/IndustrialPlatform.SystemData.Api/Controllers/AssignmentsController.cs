using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Api.Export;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Assignments;
using IndustrialPlatform.SystemData.Contracts.Administration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Controllers;

/// <summary>
/// 用户任职控制面端点(05 方案 §9.3):查询时间线、创建、修改 Scheduled 区间、
/// 结束、取消与主任职原子切换。租户与执行者标识只从当前用户上下文读取,
/// 绝不信任请求体传入;错误统一 §9.9 信封。响应不暴露数据库 Guid。
/// </summary>
[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssignmentsController : SystemDataControllerBase
{
    private readonly IUserAssignmentService _service;

    /// <summary>初始化用户任职控制面控制器。</summary>
    public AssignmentsController(IUserAssignmentService service, ICurrentUser currentUser)
        : base(currentUser)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>读取用户任职时间线(GET /api/v1/users/{userNId}/assignments),投影状态按当前时间派生。</summary>
    [HttpGet("users/{userNId}/assignments")]
    [Authorize(Policy = SystemDataPermissionPolicies.AssignmentView)]
    public async Task<ActionResult<IReadOnlyList<AssignmentV1>>> ListForUser(
        string userNId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return (await _service.ListForUserAsync(tenantNId, userNId, cancellationToken)).ToList();
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

    [HttpGet("users/{userNId}/assignments/export")]
    [Authorize(Policy = SystemDataPermissionPolicies.AssignmentView)]
    public IActionResult ExportForUser(string userNId, [FromQuery] string quantity = "10000", [FromQuery] string? columns = null, [FromQuery] string? sortField = null, [FromQuery] string? sortOrder = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenantNId, out _)) return UnauthorizedEnvelope();
        var maxRows = StreamingXlsxExport.ParseQuantity(quantity);
        return StreamingXlsxExport.Start("systemdata-assignments.xlsx", async (output, ct) =>
        {
            var items = await _service.ListForUserAsync(tenantNId, userNId, ct);
            IEnumerable<AssignmentV1> ordered = string.Equals(sortField, "effectiveFrom", StringComparison.OrdinalIgnoreCase) ? (string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase) ? items.OrderBy(x => x.EffectiveFrom) : items.OrderByDescending(x => x.EffectiveFrom)) : items.OrderBy(x => x.EffectiveFrom);
            var workbook = await StreamingXlsxWriter.CreateAsync(output, ct);
            var selectedColumns = StreamingXlsxExport.SelectColumns(columns,
                new StreamingXlsxExport.Column<AssignmentV1>("nId", "任职标识", item => item.NId),
                new StreamingXlsxExport.Column<AssignmentV1>("userDisplayNameSnapshot", "用户", item => item.UserDisplayNameSnapshot),
                new StreamingXlsxExport.Column<AssignmentV1>("organizationNId", "组织", item => item.OrganizationNId),
                new StreamingXlsxExport.Column<AssignmentV1>("positionName", "岗位", item => item.PositionName),
                new StreamingXlsxExport.Column<AssignmentV1>("state", "状态", item => item.State),
                new StreamingXlsxExport.Column<AssignmentV1>("isPrimary", "主任职", item => item.IsPrimary ? "是" : "否"),
                new StreamingXlsxExport.Column<AssignmentV1>("effectiveFrom", "生效时间", item => item.EffectiveFrom.ToString("O")),
                new StreamingXlsxExport.Column<AssignmentV1>("effectiveTo", "失效时间", item => item.EffectiveTo?.ToString("O")));
            await workbook.WriteRowAsync(selectedColumns.Select(column => column.Title), ct);
            foreach (var item in ordered.Take(maxRows)) await workbook.WriteRowAsync(selectedColumns.Select(column => column.Value(item)), ct);
            await workbook.DisposeAsync();
        }, cancellationToken);
    }

    /// <summary>为用户创建任职(POST /api/v1/users/{userNId}/assignments);用户目录不可用 503。</summary>
    [HttpPost("users/{userNId}/assignments")]
    [Authorize(Policy = SystemDataPermissionPolicies.AssignmentManage)]
    public async Task<ActionResult<AssignmentV1>> Create(
        string userNId,
        CreateAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.CreateAsync(tenantNId, actorUserNId, HttpContext.TraceIdentifier, userNId, request, cancellationToken);
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

    /// <summary>修改计划中任职区间(PUT /api/v1/assignments/{assignmentNId},仅 Scheduled);双版本不匹配 409。</summary>
    [HttpPut("assignments/{assignmentNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.AssignmentManage)]
    public async Task<ActionResult<AssignmentV1>> UpdateScheduled(
        string assignmentNId,
        UpdateScheduledAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.UpdateScheduledAsync(tenantNId, actorUserNId, HttpContext.TraceIdentifier, assignmentNId, request, cancellationToken);
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

    /// <summary>结束当前任职(POST /api/v1/assignments/{assignmentNId}/end,仅 Current)。</summary>
    [HttpPost("assignments/{assignmentNId}/end")]
    [Authorize(Policy = SystemDataPermissionPolicies.AssignmentManage)]
    public async Task<ActionResult<AssignmentV1>> End(
        string assignmentNId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.EndAsync(tenantNId, actorUserNId, HttpContext.TraceIdentifier, assignmentNId, cancellationToken);
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

    /// <summary>取消计划中任职(POST /api/v1/assignments/{assignmentNId}/cancel,仅 Scheduled)。</summary>
    [HttpPost("assignments/{assignmentNId}/cancel")]
    [Authorize(Policy = SystemDataPermissionPolicies.AssignmentManage)]
    public async Task<ActionResult<AssignmentV1>> Cancel(
        string assignmentNId,
        CancelAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.CancelAsync(tenantNId, actorUserNId, HttpContext.TraceIdentifier, assignmentNId, request, cancellationToken);
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

    /// <summary>原子切换主任职(POST /api/v1/users/{userNId}/primary-assignment);并发 revision 不匹配 409。</summary>
    [HttpPost("users/{userNId}/primary-assignment")]
    [Authorize(Policy = SystemDataPermissionPolicies.AssignmentManage)]
    public async Task<ActionResult<IReadOnlyList<AssignmentV1>>> SetPrimary(
        string userNId,
        SetPrimaryAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return (await _service.SetPrimaryAsync(tenantNId, actorUserNId, HttpContext.TraceIdentifier, userNId, request, cancellationToken)).ToList();
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
