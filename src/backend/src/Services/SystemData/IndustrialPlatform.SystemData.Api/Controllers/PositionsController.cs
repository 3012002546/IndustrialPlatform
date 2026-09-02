using IndustrialPlatform.Security;
using System.Globalization;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Api.Export;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Positions;
using IndustrialPlatform.SystemData.Contracts.Administration;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Controllers;

/// <summary>
/// 岗位控制面端点(05 方案 §9.3 /api/v1/positions/**):分页查询、创建、
/// 改名/描述/顺序与启用/停用。租户与执行者标识只从当前用户上下文读取;
/// 错误统一 §9.9 信封。响应不暴露数据库 Guid。
/// </summary>
[ApiController]
[Route("positions")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PositionsController : SystemDataControllerBase
{
    private readonly IPositionService _service;

    /// <summary>初始化岗位控制面控制器。</summary>
    public PositionsController(IPositionService service, ICurrentUser currentUser)
        : base(currentUser)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>按组织/状态分页查询岗位(GET /api/v1/positions)。</summary>
    [HttpGet]
    [Authorize(Policy = SystemDataPermissionPolicies.PositionView)]
    public async Task<ActionResult<PageResult<PositionV1>>> List(
        [FromQuery] string? organizationNId,
        [FromQuery] string? status,
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
            return BadRequestEnvelope("SD_VALIDATION_FAILED", "分页参数无效。");
        }

        try
        {
            var page = await _service.ListAsync(tenantNId, organizationNId, status, pageIndex, pageSize, cancellationToken);
            return PageResult.Create(page.Items, page.Total, page.PageIndex, page.PageSize);
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

    [HttpGet("export")]
    [Authorize(Policy = SystemDataPermissionPolicies.PositionView)]
    public IActionResult Export([FromQuery] string? organizationNId, [FromQuery] string? status, [FromQuery] string quantity = "10000", [FromQuery] string? columns = null, [FromQuery] string? sortField = null, [FromQuery] string? sortOrder = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenantNId, out _)) return UnauthorizedEnvelope();
        var maxRows = StreamingXlsxExport.ParseQuantity(quantity);
        return StreamingXlsxExport.Start("systemdata-positions.xlsx", async (output, ct) =>
        {
            var all = new List<PositionV1>();
            var pageIndex = 1;
            while (all.Count < maxRows)
            {
                var page = await _service.ListAsync(tenantNId, organizationNId, status, pageIndex, 100, ct);
                all.AddRange(page.Items);
                if (all.Count >= page.Total || page.Items.Count < 100) break;
                pageIndex++;
            }
            IEnumerable<PositionV1> items = all;
            items = string.Equals(sortField, "name", StringComparison.OrdinalIgnoreCase) ? (string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase) ? items.OrderBy(x => x.Name) : items.OrderByDescending(x => x.Name)) : items.OrderBy(x => x.DisplayOrder).ThenBy(x => x.NId);
            var workbook = await StreamingXlsxWriter.CreateAsync(output, ct);
            var selectedColumns = StreamingXlsxExport.SelectColumns(columns,
                new StreamingXlsxExport.Column<PositionV1>("nId", "岗位标识", item => item.NId),
                new StreamingXlsxExport.Column<PositionV1>("name", "岗位", item => item.Name),
                new StreamingXlsxExport.Column<PositionV1>("organizationName", "组织", item => item.OrganizationName),
                new StreamingXlsxExport.Column<PositionV1>("status", "状态", item => item.Status),
                new StreamingXlsxExport.Column<PositionV1>("displayOrder", "显示顺序", item => item.DisplayOrder.ToString(CultureInfo.InvariantCulture)));
            await workbook.WriteRowAsync(selectedColumns.Select(column => column.Title), ct);
            foreach (var item in items.Take(maxRows)) await workbook.WriteRowAsync(selectedColumns.Select(column => column.Value(item)), ct);
            await workbook.DisposeAsync();
        }, cancellationToken);
    }

    /// <summary>创建岗位(POST /api/v1/positions)。</summary>
    [HttpPost]
    [Authorize(Policy = SystemDataPermissionPolicies.PositionCreate)]
    public async Task<ActionResult<PositionV1>> Create(
        CreatePositionRequest request,
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

    /// <summary>修改岗位名称、描述、顺序(PUT /api/v1/positions/{positionNId});双版本不匹配 409。</summary>
    [HttpPut("{positionNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.PositionUpdate)]
    public async Task<ActionResult<PositionV1>> Update(
        string positionNId,
        UpdatePositionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.UpdateAsync(tenantNId, actorUserNId, HttpContext.TraceIdentifier, positionNId, request, cancellationToken);
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

    /// <summary>启用/停用岗位(PUT /api/v1/positions/{positionNId}/status);有当前/未来任职停用拒绝 409。</summary>
    [HttpPut("{positionNId}/status")]
    [Authorize(Policy = SystemDataPermissionPolicies.PositionStatus)]
    public async Task<ActionResult<PositionV1>> SetStatus(
        string positionNId,
        SetPositionStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _service.SetStatusAsync(tenantNId, actorUserNId, HttpContext.TraceIdentifier, positionNId, request, cancellationToken);
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
