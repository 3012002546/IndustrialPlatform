using IndustrialPlatform.Identity.Api.Export;
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
        [FromQuery] string? keyword,
        [FromQuery] string? loginNameSnapshot,
        [FromQuery] string? failureCode,
        [FromQuery] string? ipAddressHash,
        [FromQuery] string? userAgentHash,
        [FromQuery] string? traceId,
        [FromQuery] DateTimeOffset? occurredFrom,
        [FromQuery] DateTimeOffset? occurredTo,
        [FromQuery] bool? success,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sortField = null,
        [FromQuery] string? sortOrder = null,
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
                new LoginAuditFilter(tenantNId, userNId, success, pageIndex, pageSize, sortField, sortOrder, keyword, loginNameSnapshot, failureCode, ipAddressHash, userAgentHash, traceId, occurredFrom, occurredTo),
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

    [Authorize(Policy = PermissionPolicies.AuditLoginView)]
    [HttpGet("logins/export")]
    public IActionResult ExportLogins(
        [FromQuery] string? userNId,
        [FromQuery] string? keyword,
        [FromQuery] string? loginNameSnapshot,
        [FromQuery] string? failureCode,
        [FromQuery] string? ipAddressHash,
        [FromQuery] string? userAgentHash,
        [FromQuery] string? traceId,
        [FromQuery] DateTimeOffset? occurredFrom,
        [FromQuery] DateTimeOffset? occurredTo,
        [FromQuery] bool? success,
        [FromQuery] string quantity = "10000",
        [FromQuery] string? columns = null,
        [FromQuery] string? sortField = null,
        [FromQuery] string? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenantNId, out _)) return UnauthorizedEnvelope();
        var maxRows = StreamingXlsxExport.ParseQuantity(quantity);
        return StreamingXlsxExport.Start("login-audits.xlsx", async (output, ct) =>
        {
            var workbook = await StreamingXlsxWriter.CreateAsync(output, ct);
            var selectedColumns = StreamingXlsxExport.SelectColumns(columns,
                new StreamingXlsxExport.Column<LoginAuditItem>("userNId", "用户标识", audit => audit.UserNId),
                new StreamingXlsxExport.Column<LoginAuditItem>("loginNameSnapshot", "登录名", audit => audit.LoginNameSnapshot),
                new StreamingXlsxExport.Column<LoginAuditItem>("success", "结果", audit => audit.Success ? "成功" : "失败"),
                new StreamingXlsxExport.Column<LoginAuditItem>("failureCode", "失败原因", audit => audit.FailureCode),
                new StreamingXlsxExport.Column<LoginAuditItem>("ipAddressHash", "IP 哈希", audit => audit.IpAddressHash),
                new StreamingXlsxExport.Column<LoginAuditItem>("userAgentHash", "User-Agent 哈希", audit => audit.UserAgentHash),
                new StreamingXlsxExport.Column<LoginAuditItem>("traceId", "TraceId", audit => audit.TraceId),
                new StreamingXlsxExport.Column<LoginAuditItem>("occurredOn", "发生时间", audit => audit.OccurredOn.ToString("O")));
            await workbook.WriteRowAsync(selectedColumns.Select(column => column.Title), ct);
            var written = 0;
            var pageIndex = 1;
            while (written < maxRows)
            {
                var page = await _service.QueryLoginAuditsAsync(tenantNId, new LoginAuditFilter(tenantNId, userNId, success, pageIndex, 100, sortField, sortOrder, keyword, loginNameSnapshot, failureCode, ipAddressHash, userAgentHash, traceId, occurredFrom, occurredTo), ct);
                if (page.Items.Count == 0) break;
                foreach (var audit in page.Items)
                {
                    if (written++ >= maxRows) break;
                    await workbook.WriteRowAsync(selectedColumns.Select(column => column.Value(audit)), ct);
                }
                if (written >= page.Total || page.Items.Count < 100) break;
                pageIndex++;
            }
            await workbook.DisposeAsync();
        }, cancellationToken);
    }
}
