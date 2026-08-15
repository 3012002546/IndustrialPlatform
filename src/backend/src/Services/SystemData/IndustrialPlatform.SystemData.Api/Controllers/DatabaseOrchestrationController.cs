using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Controllers;

/// <summary>
/// 数据库编排控制面端点(05 方案 §9.2):注册清单、计划/apply 异步入队(202)、
/// 审批与备份证据、操作状态/取消与服务就绪查询。
/// 本任务(SD-006)接入 [Authorize] 权限策略(§9.6 systemdata.database-orchestration.*);
/// 保留 TryGetActorContext→401 防御,正常路径由授权管线保证声明有效。
/// 写路径幂等(Idempotency-Key + 请求哈希),错误统一 SD_DB_ 信封(§9.9)。
/// 环境(NId)由服务端可信拓扑解析,请求体不含环境标识(防客户端伪造)。
/// </summary>
[ApiController]
[Route("database-orchestration")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DatabaseOrchestrationController : SystemDataControllerBase
{
    private readonly IRegistrationService _registrationService;
    private readonly IPlanService _planService;
    private readonly IOperationService _operationService;
    private readonly IApprovalService _approvalService;
    private readonly IBackupService _backupService;

    /// <summary>初始化数据库编排控制面控制器。</summary>
    public DatabaseOrchestrationController(
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

    // ===== 注册清单 =====

    /// <summary>注册/重注册版本化清单(PUT /api/v1/database-orchestration/registrations/{serviceKey});幂等返回现有注册。</summary>
    [HttpPut("registrations/{serviceKey}")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationRegister)]
    public async Task<ActionResult<DatabaseRegistrationV1>> Register(
        string serviceKey,
        DatabaseRegistrationManifestV1 manifest,
        CancellationToken cancellationToken)
    {
        _ = serviceKey;
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _registrationService.RegisterAsync(tenantNId, actorUserNId, manifest, cancellationToken);
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

    /// <summary>分页查询注册清单(GET /api/v1/database-orchestration/registrations),ServiceKey 可选包含过滤。</summary>
    [HttpGet("registrations")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationView)]
    public async Task<ActionResult<PageResult<DatabaseRegistrationSummaryV1>>> ListRegistrations(
        [FromQuery] string? serviceKey,
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
            var page = await _registrationService.ListAsync(tenantNId, serviceKey, pageIndex, pageSize, cancellationToken);
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

    /// <summary>按服务键查询注册清单(GET /api/v1/database-orchestration/registrations/{serviceKey});不存在 404。</summary>
    [HttpGet("registrations/{serviceKey}")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationView)]
    public async Task<ActionResult<DatabaseRegistrationV1>> GetRegistration(
        string serviceKey,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _registrationService.GetAsync(tenantNId, serviceKey, cancellationToken);
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

    // ===== 计划 =====

    /// <summary>入队异步计划(POST /api/v1/database-orchestration/plans);Idempotency-Key 幂等重放返回原 Operation。</summary>
    [HttpPost("plans")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationPlan)]
    public async Task<ActionResult<EnqueueOperationV1>> EnqueuePlan(
        DatabasePlanRequestV1 request,
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
            var operation = await _planService.EnqueuePlanAsync(
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

    /// <summary>分页查询不可变计划(GET /api/v1/database-orchestration/plans)。</summary>
    [HttpGet("plans")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationView)]
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

    /// <summary>按计划标识查询(GET /api/v1/database-orchestration/plans/{planNId});不存在 404。</summary>
    [HttpGet("plans/{planNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationView)]
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

    /// <summary>为计划登记审批(POST /api/v1/database-orchestration/plans/{planNId}/approvals);计划过期 409。</summary>
    [HttpPost("plans/{planNId}/approvals")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationApprove)]
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

    /// <summary>为计划登记备份证据(POST /api/v1/database-orchestration/plans/{planNId}/backup-evidence);创建为 Captured。</summary>
    [HttpPost("plans/{planNId}/backup-evidence")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationBackup)]
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

    /// <summary>验证备份证据(POST /api/v1/database-orchestration/backup-evidence/{evidenceNId}/verify);Captured → Verified。</summary>
    [HttpPost("backup-evidence/{evidenceNId}/verify")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationBackup)]
    public async Task<ActionResult<DatabaseBackupEvidenceV1>> VerifyBackupEvidence(
        string evidenceNId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return await _backupService.VerifyAsync(tenantNId, actorUserNId, evidenceNId, cancellationToken);
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

    // ===== Operation =====

    /// <summary>入队异步 apply(POST /api/v1/database-orchestration/operations/apply);门禁:计划有效/未漂移/目标匹配/审批与备份。Idempotency-Key 幂等重放返回原 Operation。</summary>
    [HttpPost("operations/apply")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationApply)]
    public async Task<ActionResult<EnqueueOperationV1>> EnqueueApply(
        DatabaseApplyRequestV1 request,
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
            var operation = await _operationService.EnqueueApplyAsync(
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

    /// <summary>分页查询操作(GET /api/v1/database-orchestration/operations),Kind/Status 枚举名可选过滤。</summary>
    [HttpGet("operations")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationView)]
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

    /// <summary>按操作标识查询(GET /api/v1/database-orchestration/operations/{operationNId});不存在 404。</summary>
    [HttpGet("operations/{operationNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationView)]
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

    /// <summary>取消操作(POST /api/v1/database-orchestration/operations/{operationNId}/cancel);仅 Queued 或 Running 且未越过 Inspect 的安全边界。</summary>
    [HttpPost("operations/{operationNId}/cancel")]
    [Authorize(Policy = SystemDataPermissionPolicies.DatabaseOrchestrationCancel)]
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

    // ===== 就绪状态 =====

    /// <summary>查询服务数据库就绪状态(GET /api/v1/database-orchestration/readiness/{serviceKey});未就绪返回 503 SD_DB_NOT_READY 并携带形状。</summary>
    [HttpGet("readiness/{serviceKey}")]
    [Authorize]
    public async Task<ActionResult<DatabaseReadinessV1>> GetReadiness(
        string serviceKey,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            var readiness = await _operationService.GetReadinessAsync(
                tenantNId,
                serviceKey,
                HttpContext.TraceIdentifier,
                cancellationToken);
            if (readiness.Ready)
            {
                return readiness;
            }

            return StatusCodeEnvelope(
                StatusCodes.Status503ServiceUnavailable,
                "SD_DB_NOT_READY",
                readiness.Reason ?? "服务数据库未就绪。",
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
