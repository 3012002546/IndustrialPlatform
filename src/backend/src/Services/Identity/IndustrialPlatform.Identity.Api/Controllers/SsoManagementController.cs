using IndustrialPlatform.Identity.Api.Authorization;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Application.Sso;
using IndustrialPlatform.Identity.Contracts.Sso;
using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// SSO 管理端点(§26.7/§26.8):企业登录源、外部账号与平台 SSO Client 管理。
/// 读操作需 <see cref="PermissionPolicies.SsoView"/>,写操作需 <see cref="PermissionPolicies.SsoManage"/>,
/// 连接测试需 <see cref="PermissionPolicies.SsoTest"/>;密钥只更新引用(写后不回显,摘要仅暴露 HasSecretReference)。
/// </summary>
[ApiController]
[Route("sso-management")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize]
public sealed class SsoManagementController : ManagementControllerBase
{
    private readonly ISsoManagementService _service;

    /// <summary>初始化 SSO 管理控制器。</summary>
    public SsoManagementController(ISsoManagementService service, ICurrentUser currentUser)
        : base(currentUser)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    // ---- 企业登录源(§26.8) ----

    /// <summary>列出企业登录源(含停用,管理端)。</summary>
    [Authorize(Policy = PermissionPolicies.SsoView)]
    [HttpGet("providers")]
    public async Task<ActionResult<IReadOnlyList<ProviderSummary>>> ListProviders(CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.ListProvidersAsync(tenantNId, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>查询企业登录源详情。</summary>
    [Authorize(Policy = PermissionPolicies.SsoView)]
    [HttpGet("providers/{providerNId}")]
    public async Task<ActionResult<ProviderSummary>> GetProvider(string providerNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.GetProviderAsync(tenantNId, providerNId, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>创建企业登录源。</summary>
    [Authorize(Policy = PermissionPolicies.SsoManage)]
    [HttpPost("providers")]
    public async Task<ActionResult<ProviderSummary>> CreateProvider(CreateSsoProviderRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.CreateProviderAsync(tenantNId, actorUserNId, request, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>更新企业登录源配置。</summary>
    [Authorize(Policy = PermissionPolicies.SsoManage)]
    [HttpPut("providers/{providerNId}")]
    public async Task<ActionResult<ProviderSummary>> UpdateProvider(string providerNId, UpdateSsoProviderRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.UpdateProviderAsync(tenantNId, actorUserNId, providerNId, request, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>更新企业登录源密钥引用(只写配置节引用,不接收明文)。</summary>
    [Authorize(Policy = PermissionPolicies.SsoManage)]
    [HttpPut("providers/{providerNId}/secret")]
    public async Task<ActionResult<ProviderSummary>> UpdateProviderSecret(string providerNId, UpdateSsoProviderSecretRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.UpdateProviderSecretAsync(tenantNId, actorUserNId, providerNId, request, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>启用/停用企业登录源。</summary>
    [Authorize(Policy = PermissionPolicies.SsoManage)]
    [HttpPut("providers/{providerNId}/enabled")]
    public async Task<ActionResult<ProviderSummary>> SetProviderEnabled(string providerNId, SetSsoProviderEnabledRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.SetProviderEnabledAsync(tenantNId, actorUserNId, providerNId, request, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>连接测试企业登录源(强制刷新远端发现/元数据)。</summary>
    [Authorize(Policy = PermissionPolicies.SsoTest)]
    [HttpPost("providers/{providerNId}/test")]
    public async Task<ActionResult<ProviderTestResult>> TestProvider(string providerNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.TestProviderAsync(tenantNId, providerNId, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    // ---- 外部账号(§26.3/§26.8) ----

    /// <summary>列出 Provider 的外部账号(展示平台用户字段,不暴露 external subject)。</summary>
    [Authorize(Policy = PermissionPolicies.SsoView)]
    [HttpGet("providers/{providerNId}/accounts")]
    public async Task<ActionResult<IReadOnlyList<ExternalAccountSummary>>> ListAccounts(string providerNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.ListAccountsAsync(tenantNId, providerNId, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>绑定外部账号到平台用户。</summary>
    [Authorize(Policy = PermissionPolicies.SsoManage)]
    [HttpPost("providers/{providerNId}/accounts")]
    public async Task<ActionResult<ExternalAccountSummary>> BindAccount(string providerNId, BindSsoAccountRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.BindAccountAsync(tenantNId, actorUserNId, providerNId, request, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>解绑外部账号(幂等)。</summary>
    [Authorize(Policy = PermissionPolicies.SsoManage)]
    [HttpDelete("providers/{providerNId}/accounts/{userNId}")]
    public async Task<IActionResult> UnbindAccount(string providerNId, string userNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            await _service.UnbindAccountAsync(tenantNId, actorUserNId, providerNId, userNId, cancellationToken);
            return OkEnvelope();
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    // ---- 平台 SSO Client(§26.7) ----

    /// <summary>列出平台 SSO Client(含端点)。</summary>
    [Authorize(Policy = PermissionPolicies.SsoView)]
    [HttpGet("clients")]
    public async Task<ActionResult<IReadOnlyList<SsoClientSummary>>> ListClients(CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.ListClientsAsync(tenantNId, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>查询平台 SSO Client 详情。</summary>
    [Authorize(Policy = PermissionPolicies.SsoView)]
    [HttpGet("clients/{clientNId}")]
    public async Task<ActionResult<SsoClientSummary>> GetClient(string clientNId, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.GetClientAsync(tenantNId, clientNId, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>创建平台 SSO Client。</summary>
    [Authorize(Policy = PermissionPolicies.SsoManage)]
    [HttpPost("clients")]
    public async Task<ActionResult<SsoClientSummary>> CreateClient(CreateSsoClientRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.CreateClientAsync(tenantNId, actorUserNId, request, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>更新平台 SSO Client。</summary>
    [Authorize(Policy = PermissionPolicies.SsoManage)]
    [HttpPut("clients/{clientNId}")]
    public async Task<ActionResult<SsoClientSummary>> UpdateClient(string clientNId, UpdateSsoClientRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.UpdateClientAsync(tenantNId, actorUserNId, clientNId, request, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>启用/停用平台 SSO Client。</summary>
    [Authorize(Policy = PermissionPolicies.SsoManage)]
    [HttpPut("clients/{clientNId}/enabled")]
    public async Task<ActionResult<SsoClientSummary>> SetClientEnabled(string clientNId, SetSsoClientEnabledRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.SetClientEnabledAsync(tenantNId, actorUserNId, clientNId, request, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>登记 Client 端点。</summary>
    [Authorize(Policy = PermissionPolicies.SsoManage)]
    [HttpPost("clients/{clientNId}/endpoints")]
    public async Task<ActionResult<SsoClientSummary>> AddClientEndpoint(string clientNId, AddSsoEndpointRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.AddClientEndpointAsync(tenantNId, actorUserNId, clientNId, request, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>启用/停用 Client 端点。</summary>
    [Authorize(Policy = PermissionPolicies.SsoManage)]
    [HttpPut("clients/{clientNId}/endpoints/{endpointNId}/enabled")]
    public async Task<ActionResult<SsoClientSummary>> SetClientEndpointEnabled(string clientNId, string endpointNId, SetSsoEndpointEnabledRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.SetClientEndpointEnabledAsync(tenantNId, actorUserNId, clientNId, endpointNId, request, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>移除 Client 端点(幂等);双版本随查询串提交。</summary>
    [Authorize(Policy = PermissionPolicies.SsoManage)]
    [HttpDelete("clients/{clientNId}/endpoints/{endpointNId}")]
    public async Task<IActionResult> RemoveClientEndpoint(
        string clientNId,
        string endpointNId,
        [FromQuery] long expectedOptimisticVersion,
        [FromQuery] Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out var actorUserNId))
        {
            return UnauthorizedEnvelope();
        }

        try
        {
            return Ok(await _service.RemoveClientEndpointAsync(
                tenantNId, actorUserNId, clientNId, endpointNId, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (ManagementException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }
}
