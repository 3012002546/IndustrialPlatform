using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Contracts.ControlPlane;
using IndustrialPlatform.SystemData.Domain.ControlPlane;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Api.Controllers;

[ApiController]
[Route("resources")]
[Authorize]
public sealed class ResourcesController : SystemDataControllerBase
{
    private readonly IResourceNavigationService _service;
    private readonly ControlPlaneOptions _options;
    public ResourcesController(IResourceNavigationService service, ICurrentUser currentUser, IOptions<ControlPlaneOptions>? options = null) : base(currentUser) { _service = service; _options = options?.Value ?? new ControlPlaneOptions(); }

    [HttpGet]
    [Authorize(Policy = SystemDataPermissionPolicies.ResourceView)]
    public async Task<ActionResult<IReadOnlyCollection<UiResourceResponse>>> List(CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _)) return UnauthorizedEnvelope();
        return Ok(await _service.ListResourcesAsync(tenantNId, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationManage)]
    public async Task<ActionResult<UiResourceResponse>> Register(RegisterUiResourceRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenantNId, out _)) return UnauthorizedEnvelope();
        try { return Ok(await _service.RegisterResourceAsync(tenantNId, request, cancellationToken)); }
        catch (ValidationException ex) { return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message); }
        catch (ArgumentException ex) { return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message); }
    }

    [HttpPost("manifest")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationManage)]
    public async Task<ActionResult<IReadOnlyCollection<ModuleManifestResponse>>> RegisterManifest(RegisterModuleManifestRequest request, CancellationToken cancellationToken)
    {
        if (!_options.EnableRemoteManifestRegistration) return StatusCodeEnvelope(403, "SD_MANIFEST_REGISTRATION_DISABLED", "Remote module manifest registration is disabled; use trusted SystemData seeds.");
        if (!TryGetActorContext(out var tenantNId, out _)) return UnauthorizedEnvelope();
        try { return Ok(await _service.RegisterManifestAsync(tenantNId, request, cancellationToken)); }
        catch (ValidationException ex) { return StatusCodeEnvelope(409, "SD_MANIFEST_CONFLICT", ex.Message); }
        catch (ArgumentException ex) { return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message); }
    }
}

[ApiController]
[Route("navigation")]
[Authorize]
public sealed class NavigationController : SystemDataControllerBase
{
    private readonly IResourceNavigationService _service;
    public NavigationController(IResourceNavigationService service, ICurrentUser currentUser) : base(currentUser) => _service = service;

    [HttpGet("draft")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationView)]
    public async Task<ActionResult<NavigationDraftResponse>> Draft(CancellationToken cancellationToken) => await Execute(async tenant => await _service.GetDraftAsync(tenant, cancellationToken));

    [HttpPost("draft/nodes")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationManage)]
    public async Task<ActionResult<NavigationNodeResponse>> Add(CreateNavigationNodeRequest request, CancellationToken cancellationToken) => await Execute(async tenant => await _service.AddNodeAsync(tenant, request, cancellationToken));

    [HttpPut("draft/nodes/{nodeNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationManage)]
    public async Task<ActionResult<NavigationNodeResponse>> Update(string nodeNId, UpdateNavigationNodeRequest request, CancellationToken cancellationToken) => await Execute(async tenant => await _service.UpdateNodeAsync(tenant, nodeNId, request, cancellationToken));

    [HttpDelete("draft/nodes/{nodeNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationManage)]
    public async Task<ActionResult> Delete(string nodeNId, CancellationToken cancellationToken) { if (!TryGetActorContext(out var tenant, out _)) return UnauthorizedEnvelope(); try { await _service.DeleteNodeAsync(tenant, nodeNId, cancellationToken); return NoContent(); } catch (ValidationException ex) { return StatusCodeEnvelope(409, "SD_NAVIGATION_DELETE_BLOCKED", ex.Message); } }

    [HttpPost("validate")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationManage)]
    public async Task<ActionResult<NavigationValidationResponse>> Validate(CancellationToken cancellationToken) => await Execute(async tenant => await _service.ValidateAsync(tenant, cancellationToken));

    [HttpPost("publish")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationPublish)]
    public async Task<ActionResult<object>> Publish(CancellationToken cancellationToken) => await Execute(async tenant => new { Revision = await _service.PublishAsync(tenant, Actor(), cancellationToken) });

    [HttpPost("rollback")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationRollback)]
    public async Task<ActionResult<object>> Rollback(CancellationToken cancellationToken) => await Execute(async tenant => new { Revision = await _service.RollbackAsync(tenant, Actor(), cancellationToken) });

    [HttpGet("/runtime/navigation")]
    [Authorize]
    public async Task<ActionResult<NavigationRuntimeResponse>> Runtime([FromQuery] string terminal = "Pc", CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenant, out _)) return UnauthorizedEnvelope();
        if (!Enum.TryParse<UiTerminal>(terminal, true, out var parsed)) return BadRequestEnvelope("SD_VALIDATION_FAILED", "Invalid terminal.");
        try { var result = await _service.RuntimeAsync(tenant, parsed, cancellationToken); if (IsNotModified(result.Revision)) return StatusCode(StatusCodes.Status304NotModified); return Ok(result); } catch (ValidationException ex) { return StatusCodeEnvelope(503, "SD_NAVIGATION_NOT_READY", ex.Message); }
    }

    private string Actor() => TryGetActorContext(out _, out var actor) ? actor : string.Empty;
    private async Task<ActionResult<T>> Execute<T>(Func<string, Task<T>> action)
    {
        if (!TryGetActorContext(out var tenant, out _)) return UnauthorizedEnvelope();
        try { return Ok(await action(tenant)); }
        catch (ValidationException ex) { return StatusCodeEnvelope(409, "SD_NAVIGATION_INVALID", ex.Message); }
        catch (ArgumentException ex) { return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message); }
    }
}

[ApiController]
[Route("features")]
[Authorize]
public sealed class FeaturesController : SystemDataControllerBase
{
    private readonly IFeatureControlService _service;
    public FeaturesController(IFeatureControlService service, ICurrentUser currentUser) : base(currentUser) => _service = service;
    [HttpPost]
    [Authorize(Policy = SystemDataPermissionPolicies.FeatureManage)]
    public async Task<ActionResult<FeatureDefinitionResponse>> Register(RegisterFeatureDefinitionRequest request, CancellationToken cancellationToken) => await Execute(t => _service.RegisterAsync(t, request, cancellationToken));
    [HttpGet]
    [Authorize(Policy = SystemDataPermissionPolicies.FeatureView)]
    public async Task<ActionResult<IReadOnlyCollection<FeatureDefinitionResponse>>> List(CancellationToken cancellationToken) => await Execute(t => _service.ListAsync(t, cancellationToken));
    [HttpPut("{featureNId}/override")]
    [Authorize(Policy = SystemDataPermissionPolicies.FeatureManage)]
    public async Task<ActionResult<FeatureDefinitionResponse>> Override(string featureNId, SetFeatureOverrideRequest request, CancellationToken cancellationToken) => await Execute(t => _service.SetOverrideAsync(t, featureNId, request, cancellationToken));
    [HttpGet("/runtime/features")]
    [Authorize]
    public async Task<ActionResult<FeatureRuntimeResponse>> Runtime(CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out _)) return UnauthorizedEnvelope();
        try { var result = await _service.RuntimeAsync(tenant, cancellationToken); if (IsNotModified(result.Revision)) return StatusCode(StatusCodes.Status304NotModified); return Ok(result); } catch (ValidationException ex) { return StatusCodeEnvelope(503, "SD_FEATURES_NOT_READY", ex.Message); }
    }
    private async Task<ActionResult<T>> Execute<T>(Func<string, Task<T>> action) { if (!TryGetActorContext(out var t, out _)) return UnauthorizedEnvelope(); try { return Ok(await action(t)); } catch (ValidationException ex) { return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message); } }
}

[ApiController]
[Route("service-catalog")]
[Authorize]
public sealed class ServiceCatalogController : SystemDataControllerBase
{
    private readonly IServiceCatalogControlService _service;
    public ServiceCatalogController(IServiceCatalogControlService service, ICurrentUser currentUser) : base(currentUser) => _service = service;
    [HttpGet]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceCatalogView)]
    public async Task<ActionResult<IReadOnlyCollection<ServiceCatalogResponse>>> List(CancellationToken cancellationToken) => await Execute(t => _service.ListAsync(t, cancellationToken));
    [HttpPost]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceCatalogManage)]
    public async Task<ActionResult<ServiceCatalogResponse>> Create(CreateExternalServiceCatalogRequest request, CancellationToken cancellationToken) => await Execute(t => _service.CreateExternalAsync(t, request, cancellationToken));
    [HttpPut("{serviceNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceCatalogManage)]
    public async Task<ActionResult<ServiceCatalogResponse>> Update(string serviceNId, UpdateServiceCatalogRequest request, CancellationToken cancellationToken) => await Execute(t => _service.UpdateAsync(t, serviceNId, request, cancellationToken));
    [HttpPut("{serviceNId}/status")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceCatalogManage)]
    public async Task<ActionResult<ServiceCatalogResponse>> Status(string serviceNId, SetCatalogStatusRequest request, CancellationToken cancellationToken) => await Execute(t => _service.SetStatusAsync(t, serviceNId, request, cancellationToken));
    [HttpGet("/runtime/service-catalog")]
    [Authorize]
    public async Task<ActionResult<ServiceCatalogRuntimeResponse>> Runtime(CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out _)) return UnauthorizedEnvelope();
        try { var result = await _service.RuntimeAsync(tenant, cancellationToken); if (IsNotModified(result.Revision)) return StatusCode(StatusCodes.Status304NotModified); return Ok(result); } catch (ValidationException ex) { return StatusCodeEnvelope(503, "SD_SERVICE_CATALOG_NOT_READY", ex.Message); }
    }
    private async Task<ActionResult<T>> Execute<T>(Func<string, Task<T>> action) { if (!TryGetActorContext(out var t, out _)) return UnauthorizedEnvelope(); try { return Ok(await action(t)); } catch (IdentityDirectoryUnavailableException ex) { return StatusCodeEnvelope(503, "SD_IDENTITY_DIRECTORY_UNAVAILABLE", ex.Message); } catch (ValidationException ex) { return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message); } catch (ArgumentException ex) { return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message); } }
}

[ApiController]
[Route("theme-policy")]
[Authorize]
public sealed class ThemePolicyController : SystemDataControllerBase
{
    private readonly IThemePolicyControlService _service;
    public ThemePolicyController(IThemePolicyControlService service, ICurrentUser currentUser) : base(currentUser) => _service = service;
    [HttpGet]
    [Authorize(Policy = SystemDataPermissionPolicies.ThemePolicyView)]
    public async Task<ActionResult<ThemePolicyResponse>> Get(CancellationToken cancellationToken) => await Execute(t => _service.GetAsync(t, cancellationToken));
    [HttpPut]
    [Authorize(Policy = SystemDataPermissionPolicies.ThemePolicyManage)]
    public async Task<ActionResult<ThemePolicyResponse>> Put(ThemePolicyRequest request, CancellationToken cancellationToken) => await Execute(t => _service.UpdateAsync(t, request, cancellationToken));
    [HttpGet("/runtime/theme-policy")]
    [Authorize]
    public async Task<ActionResult<ThemePolicyResponse>> Runtime(CancellationToken cancellationToken)
    {
        if (!TryGetActorContext(out var tenant, out _)) return UnauthorizedEnvelope();
        try { var result = await _service.RuntimeAsync(tenant, cancellationToken); if (IsNotModified(result.PolicyRevision)) return StatusCode(StatusCodes.Status304NotModified); return Ok(result); } catch (ValidationException ex) { return StatusCodeEnvelope(503, "SD_THEME_POLICY_NOT_READY", ex.Message); }
    }
    private async Task<ActionResult<T>> Execute<T>(Func<string, Task<T>> action) { if (!TryGetActorContext(out var t, out _)) return UnauthorizedEnvelope(); try { return Ok(await action(t)); } catch (ValidationException ex) { return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message); } }
}
