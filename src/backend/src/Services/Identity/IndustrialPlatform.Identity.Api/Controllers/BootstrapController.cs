using IndustrialPlatform.Identity.Api.Authorization;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Contracts.Management;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using IndustrialPlatform.Web.Results;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// bootstrap 状态/readiness 与紧急恢复端点(§29A.5)。
/// 状态与恢复在引导完成前由受控部署身份使用(此时尚无 admin 可登录),故端点允许匿名访问,
/// 但只返回非敏感状态/版本;恢复还必须通过一次性恢复引用 + 部署审批关联门禁。
/// 完整权限策略(identity.bootstrap.view / identity.bootstrap.recover)由 TASK-ID-020 接入。
/// </summary>
[ApiController]
[Route("bootstrap")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BootstrapController : ControllerBase
{
    private readonly IBootstrapService _service;
    private readonly IOptions<BootstrapOptions> _options;

    public BootstrapController(IBootstrapService service, IOptions<BootstrapOptions> options)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(options);
        _service = service;
        _options = options;
    }

    /// <summary>bootstrap 状态(§29A.5 GET /api/v1/bootstrap/status):只返回状态/版本,不含 Secret。</summary>
    [AllowAnonymous]
    [HttpGet("status")]
    public async Task<ActionResult<BootstrapStatusResponse>> Status(CancellationToken cancellationToken)
    {
        var status = await _service.GetStatusAsync(_options.Value.TenantNId, cancellationToken);
        return new BootstrapStatusResponse(
            status.State.ToString(),
            status.SchemaVersion,
            status.SeedVersions.Select(ToDto).ToList(),
            BootstrapSeedCatalog.BootstrapUserNId,
            status.AdminExists,
            status.MustChangePassword,
            status.CredentialDelivered);
    }

    /// <summary>Identity 初始化 readiness(§29A.4,GET /api/v1/bootstrap/readiness):SystemData 兼容形状。</summary>
    [AllowAnonymous]
    [HttpGet("readiness")]
    public async Task<ActionResult<IdentityReadinessResponse>> Readiness(CancellationToken cancellationToken)
    {
        var readiness = await _service.GetReadinessAsync(_options.Value.TenantNId, cancellationToken);
        return new IdentityReadinessResponse(
            readiness.ServiceKey,
            readiness.ModuleKey,
            readiness.LogicalDatabaseName,
            readiness.SchemaVersion,
            readiness.BootstrapStatus.ToString(),
            readiness.Ready ? "Ready" : "NotReady",
            readiness.MigrationReady,
            readiness.RequiredSeedReady,
            readiness.BootstrapReady,
            readiness.Ready,
            readiness.Reason,
            readiness.Seeds.Select(ToDto).ToList());
    }

    /// <summary>
    /// 紧急恢复(§29A.5 POST /api/v1/bootstrap/recover):校验一次性恢复引用与部署审批关联,
    /// 生成新的随机临时密码并只返回一次;旧凭据全部作废。响应 no-store,禁止缓存/日志/审计。
    /// </summary>
    [AllowAnonymous]
    [HttpPost("recover")]
    public async Task<ActionResult<BootstrapRecoverResponse>> Recover(
        [FromBody] BootstrapRecoverRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.RecoverAdminAsync(
                _options.Value.TenantNId,
                request.RecoveryReference ?? string.Empty,
                request.ApprovalReference ?? string.Empty,
                cancellationToken);
            return new BootstrapRecoverResponse(result.UserNId, result.TemporaryPassword, result.RecoveryReference);
        }
        catch (BootstrapException ex)
        {
            return new ObjectResult(ApiResult.Fail<object?>(ex.Code, ex.Message))
            {
                StatusCode = ex.StatusCode,
            };
        }
    }

    private static SeedVersionDto ToDto(SeedVersionStatus seed) =>
        new(seed.SeedKey, seed.SeedVersion, seed.Status);
}
