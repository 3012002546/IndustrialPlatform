using System.Diagnostics;
using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Application.Operations;
using IndustrialPlatform.SystemData.Application.Scheduler;
using IndustrialPlatform.SystemData.Contracts.Scheduler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Controllers;

[ApiController, Route("scheduler"), Authorize]
public sealed class SchedulerController(SchedulerAdministration service, ICurrentUser currentUser) : SystemDataControllerBase(currentUser)
{
    [HttpGet("handlers"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerTaskView)]
    public ActionResult<IReadOnlyList<SchedulerHandlerDto>> Handlers() => Ok(service.Handlers);

    [HttpPost("jobs/preview"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerTaskView)]
    public Task<ActionResult<SchedulerPreview>> Preview(SchedulerScheduleInput input) => Execute((_, _) => Task.FromResult(service.Preview(input)));

    [HttpGet("jobs"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerTaskView)]
    public Task<ActionResult<SchedulerPage<SchedulerJobDto>>> Jobs(string? keyword, string? status, string? sortBy, string? sortOrder, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
        Execute((tenant, _) => service.ListAsync(tenant, keyword, status, page, pageSize, cancellationToken, sortBy, sortOrder));

    [HttpGet("jobs/{nId}"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerTaskView)]
    public Task<ActionResult<SchedulerJobDto>> Job(string nId, CancellationToken cancellationToken) => Execute((tenant, _) => service.GetAsync(tenant, nId, cancellationToken));

    [HttpPost("jobs"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerTaskManage)]
    public Task<ActionResult<SchedulerJobDto>> Create(SchedulerJobCreate request, CancellationToken cancellationToken) => Execute((tenant, actor) => service.CreateAsync(tenant, actor, Trace(), request, cancellationToken));

    [HttpPut("jobs/{nId}"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerTaskManage)]
    public Task<ActionResult<SchedulerJobDto>> Update(string nId, SchedulerJobUpdate request, CancellationToken cancellationToken) => Execute((tenant, actor) => service.UpdateAsync(tenant, actor, Trace(), nId, request, cancellationToken));

    [HttpPost("jobs/{nId}/enable"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerTaskManage)]
    public Task<ActionResult<SchedulerJobDto>> Enable(string nId, SchedulerVersionCommand request, CancellationToken cancellationToken) => State(nId, "enable", request, cancellationToken);
    [HttpPost("jobs/{nId}/disable"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerTaskManage)]
    public Task<ActionResult<SchedulerJobDto>> Disable(string nId, SchedulerVersionCommand request, CancellationToken cancellationToken) => State(nId, "disable", request, cancellationToken);
    [HttpPost("jobs/{nId}/delete"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerTaskManage)]
    public Task<ActionResult<SchedulerJobDto>> Delete(string nId, SchedulerVersionCommand request, CancellationToken cancellationToken) => State(nId, "delete", request, cancellationToken);
    [HttpPost("jobs/{nId}/restore"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerTaskManage)]
    public Task<ActionResult<SchedulerJobDto>> Restore(string nId, SchedulerVersionCommand request, CancellationToken cancellationToken) => State(nId, "restore", request, cancellationToken);

    [HttpPost("jobs/{nId}/run"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerRunTrigger)]
    public Task<ActionResult<SchedulerRunAccepted>> Trigger(string nId, SchedulerVersionCommand request, [FromHeader(Name = "Idempotency-Key")] string requestKey, CancellationToken cancellationToken) =>
        Execute((tenant, actor) => service.TriggerAsync(tenant, actor, Trace(), nId, request, requestKey, cancellationToken), accepted: true);

    [HttpGet("runs"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerRunView)]
    public Task<ActionResult<SchedulerPage<SchedulerRunDto>>> Runs(string? jobNId, string? status, string? source, DateTimeOffset? from, DateTimeOffset? to, string? sortBy, string? sortOrder, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
        Execute((tenant, _) => service.RunsAsync(tenant, jobNId, status, source, from, to, page, pageSize, cancellationToken, sortBy, sortOrder));
    [HttpGet("runs/{nId}"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerRunView)]
    public Task<ActionResult<SchedulerRunDetail>> Run(string nId, CancellationToken cancellationToken) => Execute((tenant, actor) => service.DetailAsync(tenant, actor, nId, cancellationToken));
    [HttpPost("runs/{nId}/cancel"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerRunCancel)]
    public Task<ActionResult<SchedulerRunDto>> Cancel(string nId, SchedulerRunVersionCommand request, CancellationToken cancellationToken) => Execute((tenant, actor) => service.CancelAsync(tenant, actor, Trace(), nId, request, cancellationToken));
    [HttpPost("runs/{nId}/retry"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerRunRetry)]
    public Task<ActionResult<SchedulerRunAccepted>> Retry(string nId, SchedulerRunVersionCommand request, [FromHeader(Name = "Idempotency-Key")] string requestKey, CancellationToken cancellationToken) =>
        Execute((tenant, actor) => service.RetryAsync(tenant, actor, Trace(), nId, request, requestKey, cancellationToken), accepted: true);
    [HttpPost("runs/{nId}/resolve"), Authorize(Policy = SystemDataPermissionPolicies.SchedulerRunResolve)]
    public Task<ActionResult<SchedulerRunDto>> Resolve(string nId, SchedulerRunResolution request, CancellationToken cancellationToken) => Execute((tenant, actor) => service.ResolveAsync(tenant, actor, Trace(), nId, request, cancellationToken));

    private Task<ActionResult<SchedulerJobDto>> State(string nId, string action, SchedulerVersionCommand request, CancellationToken cancellationToken) => Execute((tenant, actor) => service.SetStateAsync(tenant, actor, Trace(), nId, action, request, cancellationToken));
    private string Trace() => Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
    private async Task<ActionResult<T>> Execute<T>(Func<string, string, Task<T>> action, bool accepted = false)
    {
        if (!TryGetActorContext(out var tenant, out var actor)) return UnauthorizedEnvelope();
        try { var result = await action(tenant, actor); return accepted ? AcceptedEnvelope(result) : Ok(result); }
        catch (SystemDataOperationException exception) { return StatusCodeEnvelope(exception.StatusCode, exception.Code, exception.Message); }
    }
}
