using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Api.Export;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Controllers;

/// <summary>
/// 服务初始化控制面 v2(05 方案 §9.2 /api/v1/service-initialization/**):按 (ServiceKey, ModuleKey)
/// 粒度提供注册、计划/apply 异步入队(202)、审批与备份证据、操作状态/取消与模块级服务就绪查询。
/// v1 <see cref="DatabaseOrchestrationController"/> 保持兼容(moduleKey 缺省=serviceKey),本控制器
/// 不加重复执行路径,仅暴露模块级 v2 契约;同一写请求的幂等语义(Idempotency-Key + 语义 request hash)
/// 与 v1 完全一致,错误统一 SD_ 信封(§9.9)。
/// 环境(NId)由服务端可信拓扑解析,请求体不含环境标识;本任务(SD-006)接入
/// [Authorize] 权限策略(§9.6 systemdata.service-initialization.*),保留 401 防御。
/// </summary>
[ApiController]
[Route("service-initialization")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ServiceInitializationController : SystemDataControllerBase
{
    private readonly IRegistrationService _registrationService;
    private readonly IPlanService _planService;
    private readonly IOperationService _operationService;
    private readonly IApprovalService _approvalService;
    private readonly IBackupService _backupService;

    /// <summary>初始化服务初始化控制面控制器。</summary>
    public ServiceInitializationController(
        IRegistrationService registrationService,
        IPlanService planService,
        IOperationService operationService,
        IApprovalService approvalService,
        IBackupService backupService,
        ICurrentUser currentUser)
        : base(currentUser)
    {
        ArgumentNullException.ThrowIfNull(registrationService);
        ArgumentNullException.ThrowIfNull(planService);
        ArgumentNullException.ThrowIfNull(operationService);
        ArgumentNullException.ThrowIfNull(approvalService);
        ArgumentNullException.ThrowIfNull(backupService);
        _registrationService = registrationService;
        _planService = planService;
        _operationService = operationService;
        _approvalService = approvalService;
        _backupService = backupService;
    }

    // ===== 注册清单(模块级) =====

    /// <summary>注册/重注册模块级版本化清单(PUT /api/v1/service-initialization/registrations/{serviceKey}/{moduleKey});幂等返回现有注册。</summary>
    [HttpPut("registrations/{serviceKey}/{moduleKey}")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationRegister)]
    public async Task<ActionResult<DatabaseRegistrationV1>> RegisterModule(
        string serviceKey,
        string moduleKey,
        ServiceInitializationManifestV2 manifest,
        CancellationToken cancellationToken)
    {
        _ = serviceKey;
        _ = moduleKey;
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _registrationService.RegisterModuleAsync(tenantNId, actorUserNId, manifest, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>分页查询注册清单(GET /api/v1/service-initialization/registrations),ServiceKey/ModuleKey 可选包含过滤。</summary>
    [HttpGet("registrations")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationView)]
    public async Task<ActionResult<PageResult<DatabaseRegistrationSummaryV1>>> ListRegistrations(
        [FromQuery] string? serviceKey,
        [FromQuery] string? moduleKey,
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
            var page = await _registrationService.ListAsync(tenantNId, serviceKey, pageIndex, pageSize, cancellationToken, moduleKey);
            return PageResult.Create(page.Items, page.Total, page.PageIndex, page.PageSize);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    [HttpGet("registrations/export")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationView)]
    public IActionResult ExportRegistrations([FromQuery] string? serviceKey, [FromQuery] string? moduleKey, [FromQuery] string quantity = "10000", [FromQuery] string? columns = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenantNId, out _)) return UnauthorizedEnvelope();
        var maxRows = StreamingXlsxExport.ParseQuantity(quantity);
        return StreamingXlsxExport.Start("systemdata-initialization-registrations.xlsx", async (output, ct) =>
        {
            var all = new List<DatabaseRegistrationSummaryV1>();
            var pageIndex = 1;
            while (all.Count < maxRows)
            {
                var page = await _registrationService.ListAsync(tenantNId, serviceKey, pageIndex, 100, ct, moduleKey);
                all.AddRange(page.Items);
                if (all.Count >= page.Total || page.Items.Count < 100) break;
                pageIndex++;
            }
            var workbook = await StreamingXlsxWriter.CreateAsync(output, ct);
            var selectedColumns = StreamingXlsxExport.SelectColumns(columns,
                new StreamingXlsxExport.Column<DatabaseRegistrationSummaryV1>("serviceKey", "ServiceKey", item => item.ServiceKey),
                new StreamingXlsxExport.Column<DatabaseRegistrationSummaryV1>("moduleKey", "ModuleKey", item => item.ModuleKey),
                new StreamingXlsxExport.Column<DatabaseRegistrationSummaryV1>("status", "状态", item => item.Status),
                new StreamingXlsxExport.Column<DatabaseRegistrationSummaryV1>("desiredState", "期望状态", item => item.DesiredState),
                new StreamingXlsxExport.Column<DatabaseRegistrationSummaryV1>("migrationVersion", "迁移版本", item => item.MigrationVersion),
                new StreamingXlsxExport.Column<DatabaseRegistrationSummaryV1>("logicalDatabaseName", "逻辑库", item => item.LogicalDatabaseName));
            await workbook.WriteRowAsync(selectedColumns.Select(column => column.Title), ct);
            foreach (var item in all.Take(maxRows)) await workbook.WriteRowAsync(selectedColumns.Select(column => column.Value(item)), ct);
            await workbook.DisposeAsync();
        }, cancellationToken);
    }

    /// <summary>按 (ServiceKey, ModuleKey) 查询注册清单(GET /api/v1/service-initialization/registrations/{serviceKey}/{moduleKey});不存在 404。</summary>
    [HttpGet("registrations/{serviceKey}/{moduleKey}")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationView)]
    public async Task<ActionResult<DatabaseRegistrationV1>> GetRegistration(
        string serviceKey,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _registrationService.GetModuleAsync(tenantNId, serviceKey, moduleKey, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    // ===== 计划(模块级) =====

    /// <summary>入队异步模块级计划(POST /api/v1/service-initialization/plans);Idempotency-Key 幂等重放返回原 Operation。</summary>
    [HttpPost("plans")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationPlan)]
    public async Task<ActionResult<EnqueueOperationV1>> EnqueuePlan(
        ServiceInitializationPlanRequestV2 request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        var idempotencyKey = ReadIdempotencyKey();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", "缺少 Idempotency-Key 请求头,幂等写请求必须携带。");
        }

        try
        {
            var operation = await _planService.EnqueuePlanModuleAsync(
                tenantNId,
                actorUserNId,
                idempotencyKey,
                request,
                HttpContext.TraceIdentifier,
                cancellationToken);
            return AcceptedEnvelope(operation);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>分页查询不可变计划(GET /api/v1/service-initialization/plans)。</summary>
    [HttpGet("plans")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationView)]
    public async Task<ActionResult<PageResult<DatabasePlanV1>>> ListPlans(
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
            var page = await _planService.ListAsync(tenantNId, pageIndex, pageSize, cancellationToken);
            return PageResult.Create(page.Items, page.Total, page.PageIndex, page.PageSize);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>按计划标识查询(GET /api/v1/service-initialization/plans/{planNId});不存在 404。</summary>
    [HttpGet("plans/{planNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationView)]
    public async Task<ActionResult<DatabasePlanV1>> GetPlan(
        string planNId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _planService.GetAsync(tenantNId, planNId, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    // ===== 审批 =====

    /// <summary>为计划登记审批(POST /api/v1/service-initialization/plans/{planNId}/approvals);计划过期 409。</summary>
    [HttpPost("plans/{planNId}/approvals")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationApprove)]
    public async Task<ActionResult<DatabaseApprovalV1>> CreateApproval(
        string planNId,
        DatabaseApprovalRequestV1 request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _approvalService.CreateAsync(tenantNId, actorUserNId, planNId, request, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    // ===== 备份证据 =====

    /// <summary>为计划登记备份证据(POST /api/v1/service-initialization/plans/{planNId}/backup-evidence);创建为 Captured。</summary>
    [HttpPost("plans/{planNId}/backup-evidence")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationBackup)]
    public async Task<ActionResult<DatabaseBackupEvidenceV1>> CreateBackupEvidence(
        string planNId,
        DatabaseBackupEvidenceRequestV1 request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _backupService.CreateAsync(tenantNId, actorUserNId, planNId, request, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    // ===== Operation(模块级 apply) =====

    /// <summary>入队异步模块级 apply(POST /api/v1/service-initialization/operations/apply);门禁:计划有效/未漂移/目标匹配/审批与备份。Idempotency-Key 幂等重放返回原 Operation。</summary>
    [HttpPost("operations/apply")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationApply)]
    public async Task<ActionResult<EnqueueOperationV1>> EnqueueApply(
        ServiceInitializationApplyRequestV2 request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        var idempotencyKey = ReadIdempotencyKey();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", "缺少 Idempotency-Key 请求头,幂等写请求必须携带。");
        }

        try
        {
            var operation = await _operationService.EnqueueApplyModuleAsync(
                tenantNId,
                actorUserNId,
                idempotencyKey,
                request,
                HttpContext.TraceIdentifier,
                cancellationToken);
            return AcceptedEnvelope(operation);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>分页查询操作(GET /api/v1/service-initialization/operations),Kind/Status 枚举名可选过滤。</summary>
    [HttpGet("operations")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationView)]
    public async Task<ActionResult<PageResult<DatabaseOperationV1>>> ListOperations(
        [FromQuery] string? kind,
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
            var page = await _operationService.ListAsync(tenantNId, kind, status, pageIndex, pageSize, cancellationToken);
            return PageResult.Create(page.Items, page.Total, page.PageIndex, page.PageSize);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    [HttpGet("operations/export")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationView)]
    public IActionResult ExportOperations([FromQuery] string? kind, [FromQuery] string? status, [FromQuery] string quantity = "10000", [FromQuery] string? columns = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenantNId, out _)) return UnauthorizedEnvelope();
        var maxRows = StreamingXlsxExport.ParseQuantity(quantity);
        return StreamingXlsxExport.Start("systemdata-initialization-operations.xlsx", async (output, ct) =>
        {
            var all = new List<DatabaseOperationV1>();
            var pageIndex = 1;
            while (all.Count < maxRows)
            {
                var page = await _operationService.ListAsync(tenantNId, kind, status, pageIndex, 100, ct);
                all.AddRange(page.Items);
                if (all.Count >= page.Total || page.Items.Count < 100) break;
                pageIndex++;
            }
            var workbook = await StreamingXlsxWriter.CreateAsync(output, ct);
            var selectedColumns = StreamingXlsxExport.SelectColumns(columns,
                new StreamingXlsxExport.Column<DatabaseOperationV1>("operationNId", "OperationNId", item => item.OperationNId),
                new StreamingXlsxExport.Column<DatabaseOperationV1>("kind", "类型", item => item.Kind),
                new StreamingXlsxExport.Column<DatabaseOperationV1>("serviceKey", "服务", item => item.ServiceKey),
                new StreamingXlsxExport.Column<DatabaseOperationV1>("moduleKey", "模块", item => item.ModuleKey),
                new StreamingXlsxExport.Column<DatabaseOperationV1>("status", "状态", item => item.Status),
                new StreamingXlsxExport.Column<DatabaseOperationV1>("phase", "阶段", item => item.Phase),
                new StreamingXlsxExport.Column<DatabaseOperationV1>("queuedOn", "入队时间", item => item.QueuedOn.ToString("O")));
            await workbook.WriteRowAsync(selectedColumns.Select(column => column.Title), ct);
            foreach (var item in all.Take(maxRows)) await workbook.WriteRowAsync(selectedColumns.Select(column => column.Value(item)), ct);
            await workbook.DisposeAsync();
        }, cancellationToken);
    }

    /// <summary>按操作标识查询(GET /api/v1/service-initialization/operations/{operationNId});不存在 404。</summary>
    [HttpGet("operations/{operationNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationView)]
    public async Task<ActionResult<DatabaseOperationV1>> GetOperation(
        string operationNId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _operationService.GetAsync(tenantNId, operationNId, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>取消操作(POST /api/v1/service-initialization/operations/{operationNId}/cancel);仅 Queued 或 Running 且未越过 Inspect 的安全边界。</summary>
    [HttpPost("operations/{operationNId}/cancel")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceInitializationCancel)]
    public async Task<ActionResult<DatabaseOperationV1>> CancelOperation(
        string operationNId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _operationService.CancelAsync(tenantNId, operationNId, cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    // ===== 就绪状态(模块级) =====

    /// <summary>查询模块级服务初始化就绪状态(GET /api/v1/service-initialization/readiness/{serviceKey}/{moduleKey});未就绪返回 503 SD_DB_NOT_READY 并携带 NotReady 形状。</summary>
    [HttpGet("readiness/{serviceKey}/{moduleKey}")]
    [Authorize]
    public async Task<ActionResult<ServiceInitializationReadinessV2>> GetReadiness(
        string serviceKey,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            var readiness = await _operationService.GetReadinessV2Async(
                tenantNId,
                serviceKey,
                moduleKey,
                HttpContext.TraceIdentifier,
                cancellationToken);
            if (readiness.Ready)
            {
                return readiness;
            }

            return StatusCodeEnvelope(
                StatusCodes.Status503ServiceUnavailable,
                "SD_DB_NOT_READY",
                readiness.Reason ?? "服务数据库初始化未就绪。",
                readiness);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message);
        }
        catch (DatabaseOrchestrationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>读取 Idempotency-Key 请求头(取首个值,缺失返回 null 由服务校验拒绝)。</summary>
    private string? ReadIdempotencyKey() =>
        HttpContext.Request.Headers.TryGetValue("Idempotency-Key", out var values) && values.Count > 0
            ? values[0]
            : null;
}
