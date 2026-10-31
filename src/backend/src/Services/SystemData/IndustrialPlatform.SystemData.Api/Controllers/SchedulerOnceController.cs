using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.RuntimeSupport.Jobs;
using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Application.Operations;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace IndustrialPlatform.SystemData.Api.Controllers;

/// <summary>服务断言绑定租户、actor、方法、路径、body和单次nonce；浏览器身份不能提交任务。</summary>
[ApiController, AllowAnonymous, Route("~/internal/v1/scheduler/once")]
public sealed class SchedulerOnceController(IScheduledTaskSubmission submissions, TrustedServiceCallValidator validator, ITrustedServiceCallNonceStore nonces) : ControllerBase
{
    [HttpPost]
    public Task<IActionResult> Submit(EnqueueOnceV1 request, CancellationToken token) => Invoke("scheduler.once.enqueue", context => submissions.EnqueueOnceAsync(context, request, token), token);
    [HttpGet("by-operation/{operationKey}")]
    public Task<IActionResult> ByOperation(string operationKey, CancellationToken token) => Invoke("scheduler.once.read", context => submissions.GetByOperationAsync(context, operationKey, token), token);
    public sealed record CancelInput(string StateVersion);
    [HttpGet("by-operation/{operationKey}/status")]
    public Task<IActionResult> Status(string operationKey, CancellationToken token) => Invoke("scheduler.once.read", context => submissions.GetStatusAsync(context, operationKey, token), token);
    [HttpPost("by-operation/{operationKey}/cancel")]
    public Task<IActionResult> Cancel(string operationKey, CancelInput input, CancellationToken token) => Invoke("scheduler.once.cancel", context => submissions.RequestCancelAsync(context, operationKey, input.StateVersion, token), token);
    private async Task<IActionResult> Invoke<T>(string action, Func<TrustedServiceContext, Task<T>> call, CancellationToken token)
    {
        var validation = await validator.ValidateAsync(Request, "systemdata.scheduler", action, nonces, token);
        if (validation.Status != TrustedServiceCallValidationStatus.Valid || validation.Call is null)
            return StatusCode(validation.Status == TrustedServiceCallValidationStatus.Unavailable ? 503 : 401, ApiResult.Fail<object?>("SCHEDULER_SERVICE_ASSERTION_INVALID", "受信服务断言无效或不可用。"));
        if (validation.Call.Issuer != "platform-studio") return StatusCode(403, ApiResult.Fail<object?>("SCHEDULER_SERVICE_FORBIDDEN", "仅platform-studio可提交一次性报告。"));
        try { return Ok(ApiResult.Ok(await call(new(validation.Call.TenantNId, "platform-studio", validation.Call.ActorUserNId)))); }
        catch (SystemDataOperationException exception) { return StatusCode(exception.StatusCode, ApiResult.Fail<object?>(exception.Code, exception.Message)); }
    }
}
