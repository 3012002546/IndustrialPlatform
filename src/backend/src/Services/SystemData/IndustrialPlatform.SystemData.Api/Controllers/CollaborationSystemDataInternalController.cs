using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Contracts.Auditing;
using IndustrialPlatform.SystemData.Domain.Auditing;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Controllers;

/// <summary>受信 Collaboration 内部审计事实的固定受信入口。</summary>
[ApiController]
[AllowAnonymous]
[Route("~/internal/pf05/audits")]
public sealed class CollaborationSystemDataInternalController : ControllerBase
{
    private readonly TrustedServiceCallValidator _trustedCalls;
    private readonly ITrustedServiceCallNonceStore _nonceStore;
    private readonly IAuditService _audit;

    public CollaborationSystemDataInternalController(
        TrustedServiceCallValidator trustedCalls,
        ITrustedServiceCallNonceStore nonceStore,
        IAuditService audit)
    {
        _trustedCalls = trustedCalls;
        _nonceStore = nonceStore;
        _audit = audit;
    }

    [HttpPost("facts:ingest")]
    public async Task<IActionResult> Ingest(AuditFactIngestRequest request, CancellationToken cancellationToken)
    {
        var validation = await _trustedCalls.ValidateAsync(
            Request,
            "systemdata.pf05",
            "audit.ingest",
            _nonceStore,
            cancellationToken);
        if (validation.Status != TrustedServiceCallValidationStatus.Valid || validation.Call is null)
            return validation.Status == TrustedServiceCallValidationStatus.Unavailable
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResult.Fail<object?>("PF05_SERVICE_AUTH_UNAVAILABLE", "PF05 service assertion validation is unavailable."))
                : Unauthorized(ApiResult.Fail<object?>("PF05_SERVICE_CALL_INVALID", "PF05 service assertion is invalid."));

        var call = validation.Call;
        if ((!string.IsNullOrWhiteSpace(request.ProducerServiceKey) && !string.Equals(request.ProducerServiceKey, "collaboration", StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(request.ActorUserNId) && !string.Equals(request.ActorUserNId, call.ActorUserNId, StringComparison.Ordinal)))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult.Fail<object?>("AUDIT_PRODUCER_UNTRUSTED", "审计生产者或执行者与受信上下文不一致。"));
        if (!string.Equals(request.AuditEventNId, call.RequestNId, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult.Fail<object?>("PF05_SERVICE_CALL_SCOPE_INVALID", "审计事实与受信调用范围不一致。"));
        if (request.SourceIp is not null || request.UserAgent is not null
            || !AuditPayloadRules.IsAllowedCollaborationAction(request.Action)
            || !AuditPayloadRules.IsValidCollaborationPayload(request.PayloadJson))
            return UnprocessableEntity(ApiResult.Fail<object?>("COLLAB_AUDIT_PAYLOAD_INVALID", "审计事实载荷不符合固定合规结构。"));

        try
        {
            var trusted = request with { ProducerServiceKey = "collaboration", ActorUserNId = call.ActorUserNId, SourceIp = null, UserAgent = null };
            return Ok(ApiResult.Ok(await _audit.IngestAsync(call.TenantNId, trusted, cancellationToken)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResult.Fail<object?>("SYSTEMDATA_AUDIT_UNAVAILABLE", "SystemData 审计服务暂不可用。"));
        }
    }
}
