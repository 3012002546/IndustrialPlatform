using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Collaboration.Api.Controllers;

[ApiController]
[Route("collaboration/compliance")]
[Route("~/collaboration/api/v1/compliance")]
[Authorize(Policy = "permission:collaboration.compliance.read")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CollaborationComplianceController : ControllerBase
{
    private readonly CollaborationService _service;
    private readonly ICurrentUser _current;

    public CollaborationComplianceController(CollaborationService service, ICurrentUser current)
    {
        _service = service;
        _current = current;
    }

    [HttpPost("messages/search")]
    [Authorize(Policy = "permission:collaboration.compliance.view")]
    public async Task<IActionResult> Search(ComplianceSearchRequest request, [FromHeader(Name = "X-Collaboration-Step-Up")] string? stepUpProof, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.SearchComplianceAsync(Tenant, Actor, request, stepUpProof, SessionId, SecurityVersion, cancellationToken));

    [HttpPost("views")]
    [Authorize(Policy = "permission:collaboration.compliance.view")]
    public async Task<IActionResult> Views(ComplianceSearchRequest request, [FromHeader(Name = "X-Collaboration-Step-Up")] string? stepUpProof, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.SearchComplianceAsync(Tenant, Actor, request, stepUpProof, SessionId, SecurityVersion, cancellationToken));

    [HttpPost("step-up-context")]
    public async Task<IActionResult> CreateStepUpContext(StepUpContextRequest request, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.CreateStepUpContextAsync(Tenant, Actor, request, SessionId, SecurityVersion, cancellationToken));

    [HttpGet("dispositions")]
    public async Task<IActionResult> ListDispositions(CancellationToken cancellationToken) => await ExecuteAsync(() => _service.ListDispositionsAsync(Tenant, cancellationToken));

    [HttpPost("dispositions")]
    [Authorize(Policy = "permission:collaboration.compliance.dispose")]
    public async Task<IActionResult> CreateDisposition(CreateDispositionRequest request, [FromHeader(Name = "X-Collaboration-Step-Up")] string? stepUpProof, [FromHeader(Name = "X-PF05-Step-Up")] string? legacyStepUpProof, [FromHeader(Name = "X-Request-NId")] string? requestNId, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.CreateDispositionAsync(Tenant, Actor, request, stepUpProof ?? legacyStepUpProof, request.RequestNId ?? requestNId ?? string.Empty, SessionId, SecurityVersion, cancellationToken));

    [HttpPost("legal-holds")]
    [Authorize(Policy = "permission:collaboration.compliance.legal-hold.create")]
    public async Task<IActionResult> CreateLegalHold(CreateLegalHoldRequest request, [FromHeader(Name = "X-Collaboration-Step-Up")] string? stepUpProof, [FromHeader(Name = "X-PF05-Step-Up")] string? legacyStepUpProof, [FromHeader(Name = "X-Request-NId")] string? requestNId, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.CreateLegalHoldAsync(Tenant, Actor, request, stepUpProof ?? legacyStepUpProof, request.RequestNId ?? requestNId ?? string.Empty, SessionId, SecurityVersion, cancellationToken), StatusCodes.Status202Accepted);

    [HttpGet("legal-holds")]
    public async Task<IActionResult> ListLegalHolds([FromQuery] string? cursor, [FromQuery] int limit = 50, [FromQuery] string? status = null, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(() => _service.ListLegalHoldsAsync(Tenant, cursor, status, limit, cancellationToken));

    [HttpGet("legal-holds/{holdCaseNId}")]
    public async Task<IActionResult> GetLegalHold(string holdCaseNId, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.GetLegalHoldAsync(Tenant, holdCaseNId, cancellationToken));

    [HttpPost("legal-holds/{holdCaseNId}/review")]
    [Authorize(Policy = "permission:collaboration.compliance.legal-hold.review")]
    public async Task<IActionResult> ReviewLegalHold(string holdCaseNId, UpdateLegalHoldRequest request, [FromHeader(Name = "X-Collaboration-Step-Up")] string? stepUpProof, [FromHeader(Name = "X-PF05-Step-Up")] string? legacyStepUpProof, [FromHeader(Name = "X-Request-NId")] string? requestNId, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.UpdateLegalHoldAsync(Tenant, Actor, holdCaseNId, request with { Action = "review" }, stepUpProof ?? legacyStepUpProof, request.RequestNId ?? requestNId ?? string.Empty, SessionId, SecurityVersion, cancellationToken));

    [HttpPost("legal-holds/{holdCaseNId}/release-requests")]
    [Authorize(Policy = "permission:collaboration.compliance.legal-hold.release")]
    public async Task<IActionResult> RequestLegalHoldRelease(string holdCaseNId, UpdateLegalHoldRequest request, [FromHeader(Name = "X-Collaboration-Step-Up")] string? stepUpProof, [FromHeader(Name = "X-PF05-Step-Up")] string? legacyStepUpProof, [FromHeader(Name = "X-Request-NId")] string? requestNId, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.UpdateLegalHoldAsync(Tenant, Actor, holdCaseNId, request with { Action = "release-request" }, stepUpProof ?? legacyStepUpProof, request.RequestNId ?? requestNId ?? string.Empty, SessionId, SecurityVersion, cancellationToken), StatusCodes.Status202Accepted);

    [HttpPost("legal-holds/{holdCaseNId}/release-approvals")]
    [Authorize(Policy = "permission:collaboration.compliance.legal-hold.release.approve")]
    public async Task<IActionResult> ApproveLegalHoldRelease(string holdCaseNId, UpdateLegalHoldRequest request, [FromHeader(Name = "X-Collaboration-Step-Up")] string? stepUpProof, [FromHeader(Name = "X-PF05-Step-Up")] string? legacyStepUpProof, [FromHeader(Name = "X-Request-NId")] string? requestNId, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.UpdateLegalHoldAsync(Tenant, Actor, holdCaseNId, request with { Action = "release-approve" }, stepUpProof ?? legacyStepUpProof, request.RequestNId ?? requestNId ?? string.Empty, SessionId, SecurityVersion, cancellationToken), StatusCodes.Status202Accepted);

    [HttpPost("exports")]
    [Authorize(Policy = "permission:collaboration.compliance.export.request")]
    public async Task<IActionResult> PrepareExport(PrepareComplianceExportRequest request, [FromHeader(Name = "X-Collaboration-Step-Up")] string? stepUpProof, [FromHeader(Name = "X-PF05-Step-Up")] string? legacyStepUpProof, [FromHeader(Name = "X-Request-NId")] string? requestNId, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.PrepareExportAsync(Tenant, Actor, request, stepUpProof ?? legacyStepUpProof, request.RequestNId ?? requestNId ?? string.Empty, SessionId, SecurityVersion, cancellationToken), StatusCodes.Status202Accepted);

    [HttpPost("exports/{exportNId}/approve")]
    [Authorize(Policy = "permission:collaboration.compliance.export.approve")]
    public async Task<IActionResult> ApproveExport(string exportNId, ApproveComplianceExportRequest request, [FromHeader(Name = "X-Collaboration-Step-Up")] string? stepUpProof, [FromHeader(Name = "X-PF05-Step-Up")] string? legacyStepUpProof, [FromHeader(Name = "X-Request-NId")] string? requestNId, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.ApproveExportAsync(Tenant, Actor, exportNId, request, stepUpProof ?? legacyStepUpProof, request.RequestNId ?? requestNId ?? string.Empty, SessionId, SecurityVersion, cancellationToken));

    [HttpPost("exports/{exportNId}/authorizations")]
    [Authorize(Policy = "permission:collaboration.compliance.export.download")]
    public async Task<IActionResult> AuthorizeExportDownload(string exportNId, ComplianceDownloadAuthorizationRequest request, [FromHeader(Name = "X-Collaboration-Step-Up")] string? stepUpProof, [FromHeader(Name = "X-PF05-Step-Up")] string? legacyStepUpProof, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.AuthorizeExportDownloadAsync(Tenant, Actor, exportNId, request, stepUpProof ?? legacyStepUpProof, SessionId, SecurityVersion, cancellationToken));

    [HttpGet("exports/{exportNId}/content")]
    [Authorize(Policy = "permission:collaboration.compliance.export.download")]
    public async Task<IActionResult> DownloadExport(string exportNId, [FromHeader(Name = "X-Collaboration-Step-Up")] string? stepUpProof, [FromHeader(Name = "X-Collaboration-Request-Id")] string? requestNId, CancellationToken cancellationToken) => await ExecuteStreamAsync(() => _service.OpenExportContentAsync(Tenant, Actor, exportNId, requestNId, stepUpProof, SessionId, SecurityVersion, cancellationToken));

    [HttpGet("exports/{exportNId}")]
    public async Task<IActionResult> GetExport(string exportNId, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.GetExportAsync(Tenant, exportNId, cancellationToken));

    [HttpGet("exports")]
    public async Task<IActionResult> ListExports([FromQuery] string? cursor, [FromQuery] int limit = 50, [FromQuery] string? status = null, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(() => _service.ListExportsAsync(Tenant, cursor, status, limit, cancellationToken));

    [HttpGet("retention"), HttpGet("retention-policy")]
    public async Task<IActionResult> GetRetention(CancellationToken cancellationToken) => await ExecuteAsync(() => _service.GetRetentionPolicyAsync(Tenant, cancellationToken));

    [HttpPut("retention"), HttpPut("retention-policy")]
    [Authorize(Policy = "permission:collaboration.compliance.retention.update")]
    public async Task<IActionResult> UpdateRetention(UpdateRetentionPolicyRequest request, [FromHeader(Name = "X-Collaboration-Step-Up")] string? stepUpProof, [FromHeader(Name = "X-PF05-Step-Up")] string? legacyStepUpProof, [FromHeader(Name = "X-Request-NId")] string? requestNId, CancellationToken cancellationToken) => await ExecuteAsync(() => _service.UpdateRetentionPolicyAsync(Tenant, Actor, request, stepUpProof ?? legacyStepUpProof, request.RequestNId ?? requestNId ?? string.Empty, SessionId, SecurityVersion, cancellationToken));

    private string Tenant => _current.TenantId ?? throw new UnauthorizedAccessException();
    private string Actor => _current.UserNId ?? throw new UnauthorizedAccessException();
    private string SessionId => User.FindFirst(ClaimConstants.SessionId)?.Value ?? string.Empty;
    private string SecurityVersion => User.FindFirst(ClaimConstants.AuthVersion)?.Value ?? string.Empty;

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action, int successStatus = StatusCodes.Status200OK)
    {
        try { var result = await action(); return successStatus == StatusCodes.Status200OK ? Ok(ApiResult.Ok(result)) : StatusCode(successStatus, ApiResult.Ok(result)); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return exception switch
            {
                CollaborationException item => new ObjectResult(ApiResult.Fail<object?>(item.Code, item.Message)) { StatusCode = item.StatusCode },
                UnauthorizedAccessException => Unauthorized(ApiResult.Fail<object?>("COLLAB_UNAUTHORIZED", "当前会话未认证。")),
                ArgumentException argument => BadRequest(ApiResult.Fail<object?>("COLLAB_VALIDATION_FAILED", argument.Message)),
                _ => StatusCode(503, ApiResult.Fail<object?>("COLLAB_SERVICE_UNAVAILABLE", "协作服务暂不可用.")),
            };
        }
    }

    private async Task<IActionResult> ExecuteStreamAsync(Func<Task<CollaborationFileContent>> action)
    {
        try
        {
            var result = await action();
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(result.FileName)}";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(result.Content, result.ContentType);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return exception switch
            {
                CollaborationException item => new ObjectResult(ApiResult.Fail<object?>(item.Code, item.Message)) { StatusCode = item.StatusCode },
                UnauthorizedAccessException => Unauthorized(ApiResult.Fail<object?>("COLLAB_UNAUTHORIZED", "当前会话未认证。")),
                _ => StatusCode(503, ApiResult.Fail<object?>("COLLAB_SERVICE_UNAVAILABLE", "协作服务暂不可用。")),
            };
        }
    }
}
