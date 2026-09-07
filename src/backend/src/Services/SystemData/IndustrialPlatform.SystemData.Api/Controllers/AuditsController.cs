using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Pf04;
using IndustrialPlatform.SystemData.Contracts.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace IndustrialPlatform.SystemData.Api.Controllers;

[ApiController]
[Route("audits")]
[Authorize]
public sealed class AuditsController : SystemDataControllerBase
{
    private readonly IAuditService _service;

    public AuditsController(IAuditService service, ICurrentUser currentUser) : base(currentUser) => _service = service;

    [HttpPost("facts:ingest")]
    [Authorize(Policy = SystemDataPermissionPolicies.AuditWrite)]
    public async Task<ActionResult<AuditFactV1>> Ingest(AuditFactIngestRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var actorUserNId)) return UnauthorizedEnvelope();
        var trustedServiceKey = User.FindFirst(ClaimConstants.ServiceKey)?.Value;
        if (!string.Equals(trustedServiceKey, "systemdata", StringComparison.OrdinalIgnoreCase))
            return StatusCodeEnvelope(StatusCodes.Status403Forbidden, "AUDIT_TRUSTED_CHANNEL_REQUIRED", "审计写入必须来自受信任服务身份。常规用户不能自报生产者或执行者。 ");
        if (!string.IsNullOrWhiteSpace(request.ProducerServiceKey) && !string.Equals(request.ProducerServiceKey, "systemdata", StringComparison.OrdinalIgnoreCase))
            return StatusCodeEnvelope(StatusCodes.Status403Forbidden, "AUDIT_PRODUCER_UNTRUSTED", "审计生产者必须由服务端信任上下文确定。");
        var trusted = request with { ProducerServiceKey = "systemdata", ActorUserNId = actorUserNId };
        return await Execute(() => _service.IngestAsync(tenant, trusted, cancellationToken));
    }

    [HttpGet("facts")]
    [Authorize(Policy = SystemDataPermissionPolicies.AuditRead)]
    public async Task<ActionResult<AuditFactPageV1>> Query([FromQuery] string? producerServiceKey, [FromQuery] string? action, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? until, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenant, out var actorUserNId)) return UnauthorizedEnvelope();
        return await Execute(() => _service.QueryAsync(tenant, actorUserNId, producerServiceKey, action, from, until, page, pageSize, cancellationToken));
    }

    [HttpGet("facts/{producerServiceKey}/{auditEventNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.AuditRead)]
    public async Task<ActionResult<AuditFactV1>> Get(string producerServiceKey, string auditEventNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var actorUserNId)) return UnauthorizedEnvelope();
        var value = await _service.GetAsync(tenant, actorUserNId, producerServiceKey, auditEventNId, cancellationToken);
        return value is null ? NotFound() : value;
    }

    [HttpGet("exports")]
    [Authorize(Policy = SystemDataPermissionPolicies.AuditExport)]
    public async Task<IActionResult> Export([FromQuery] string? producerServiceKey, [FromQuery] string? action, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? until, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var actorUserNId)) return UnauthorizedEnvelope();
        try
        {
            var csv = await _service.ExportCsvAsync(tenant, actorUserNId, producerServiceKey, action, from, until, cancellationToken);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"systemdata-audit-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.csv");
        }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }

    [HttpPost("facts/{producerServiceKey}/{auditEventNId}/lifecycle")]
    [Authorize(Policy = SystemDataPermissionPolicies.AuditRetentionManage)]
    public async Task<IActionResult> Lifecycle(string producerServiceKey, string auditEventNId, AuditLifecycleRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var actorUserNId)) return UnauthorizedEnvelope();
        try
        {
            await _service.UpdateLifecycleAsync(tenant, actorUserNId, producerServiceKey, auditEventNId, request, cancellationToken);
            return OkEnvelope();
        }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }

    [HttpPost("outbox/{eventId:guid}/recover")]
    [Authorize(Policy = SystemDataPermissionPolicies.AuditRetentionManage)]
    public async Task<IActionResult> RecoverOutbox(Guid eventId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out var actorUserNId)) return UnauthorizedEnvelope();
        try
        {
            return await _service.RecoverOutboxAsync(tenant, actorUserNId, eventId, cancellationToken)
                ? OkEnvelope()
                : NotFound();
        }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }

    private static async Task<ActionResult<T>> Execute<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (Pf04ServiceException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }
}
