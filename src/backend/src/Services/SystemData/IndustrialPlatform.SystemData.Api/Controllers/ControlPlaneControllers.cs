using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Api.Export;
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

    [HttpGet("defaults/preview")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationManage)]
    public async Task<ActionResult<NavigationDefaultImportPreviewResponse>> PreviewDefaults(CancellationToken cancellationToken) => await Execute(tenant => _service.PreviewDefaultImportAsync(tenant, cancellationToken));

    [HttpPost("defaults/import")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationManage)]
    public async Task<ActionResult<NavigationDefaultImportPreviewResponse>> ImportDefaults(ImportNavigationDefaultsRequest request, CancellationToken cancellationToken) => await Execute(tenant => _service.ImportDefaultsAsync(tenant, request, cancellationToken));

    [HttpPut("draft/nodes/{nodeNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationManage)]
    public async Task<ActionResult<NavigationNodeResponse>> Update(string nodeNId, UpdateNavigationNodeRequest request, CancellationToken cancellationToken) => await Execute(async tenant => await _service.UpdateNodeAsync(tenant, nodeNId, request, cancellationToken));

    [HttpDelete("draft/nodes/{nodeNId}")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationManage)]
    public async Task<ActionResult> Delete(string nodeNId, [FromQuery] long? expectedDraftRevision, CancellationToken cancellationToken) { if (!TryGetActorContext(out var tenant, out _)) return UnauthorizedEnvelope(); try { await _service.DeleteNodeAsync(tenant, nodeNId, expectedDraftRevision, cancellationToken); return NoContent(); } catch (ConcurrencyException ex) { return StatusCodeEnvelope(409, "SD_NAVIGATION_CONFLICT", ex.Message); } catch (ValidationException ex) { return StatusCodeEnvelope(409, "SD_NAVIGATION_DELETE_BLOCKED", ex.Message); } }

    [HttpPost("draft/nodes/{nodeNId}/restore")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationManage)]
    public async Task<ActionResult<NavigationNodeResponse>> Restore(string nodeNId, [FromQuery] long? expectedDraftRevision, CancellationToken cancellationToken) => await Execute(async tenant => await _service.RestoreNodeAsync(tenant, nodeNId, expectedDraftRevision, cancellationToken));

    [HttpPost("validate")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationManage)]
    public async Task<ActionResult<NavigationValidationResponse>> Validate(CancellationToken cancellationToken) => await Execute(async tenant => await _service.ValidateAsync(tenant, cancellationToken));

    [HttpPost("publish")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationPublish)]
    public async Task<ActionResult<object>> Publish([FromQuery] long? expectedDraftRevision, CancellationToken cancellationToken) => await Execute(async tenant => new { Revision = await _service.PublishAsync(tenant, Actor(), expectedDraftRevision, cancellationToken) });

    [HttpPost("rollback")]
    [Authorize(Policy = SystemDataPermissionPolicies.NavigationRollback)]
    public async Task<ActionResult<object>> Rollback([FromQuery] long? expectedDraftRevision, CancellationToken cancellationToken) => await Execute(async tenant => new { Revision = await _service.RollbackAsync(tenant, Actor(), expectedDraftRevision, cancellationToken) });

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
        catch (ConcurrencyException ex) { return StatusCodeEnvelope(409, "SD_NAVIGATION_CONFLICT", ex.Message); }
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
    [HttpGet("export")]
    [Authorize(Policy = SystemDataPermissionPolicies.FeatureView)]
    public IActionResult Export([FromQuery] string? search, [FromQuery] string quantity = "10000", [FromQuery] string? columns = null, [FromQuery] string? sortField = null, [FromQuery] string? sortOrder = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenant, out _)) return UnauthorizedEnvelope();
        var maxRows = StreamingXlsxExport.ParseQuantity(quantity);
        return StreamingXlsxExport.Start("systemdata-features.xlsx", async (output, ct) =>
        {
            var items = (await _service.ListAsync(tenant, ct)).Where(x => string.IsNullOrWhiteSpace(search) || x.FeatureNId.Contains(search, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || x.OwnerModuleNId.Contains(search, StringComparison.OrdinalIgnoreCase));
            items = string.Equals(sortField, "name", StringComparison.OrdinalIgnoreCase) ? (string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase) ? items.OrderBy(x => x.Name) : items.OrderByDescending(x => x.Name)) : items.OrderBy(x => x.FeatureNId);
            var workbook = await StreamingXlsxWriter.CreateAsync(output, ct);
            var selectedColumns = StreamingXlsxExport.SelectColumns(columns,
                new StreamingXlsxExport.Column<FeatureDefinitionResponse>("featureNId", "FeatureNId", item => item.FeatureNId),
                new StreamingXlsxExport.Column<FeatureDefinitionResponse>("ownerModuleNId", "模块", item => item.OwnerModuleNId),
                new StreamingXlsxExport.Column<FeatureDefinitionResponse>("name", "名称", item => item.Name),
                new StreamingXlsxExport.Column<FeatureDefinitionResponse>("defaultEnabled", "默认启用", item => item.DefaultEnabled ? "Enabled" : "Disabled"),
                new StreamingXlsxExport.Column<FeatureDefinitionResponse>("effectiveEnabled", "最终启用", item => item.EffectiveEnabled ? "Enabled" : "Disabled"),
                new StreamingXlsxExport.Column<FeatureDefinitionResponse>("status", "状态", item => item.Status));
            await workbook.WriteRowAsync(selectedColumns.Select(column => column.Title), ct);
            foreach (var item in items.Take(maxRows)) await workbook.WriteRowAsync(selectedColumns.Select(column => column.Value(item)), ct);
            await workbook.DisposeAsync();
        }, cancellationToken);
    }
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
    private async Task<ActionResult<T>> Execute<T>(Func<string, Task<T>> action) { if (!TryGetActorContext(out var t, out _)) return UnauthorizedEnvelope(); try { return Ok(await action(t)); } catch (ConcurrencyException ex) { return StatusCodeEnvelope(409, "SD_THEME_POLICY_CONFLICT", ex.Message); } catch (ValidationException ex) { return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message); } }
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
    [HttpGet("export")]
    [Authorize(Policy = SystemDataPermissionPolicies.ServiceCatalogView)]
    public IActionResult Export([FromQuery] string? kind, [FromQuery] string? search, [FromQuery] string quantity = "10000", [FromQuery] string? columns = null, [FromQuery] string? sortField = null, [FromQuery] string? sortOrder = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetActorContext(out var tenant, out _)) return UnauthorizedEnvelope();
        var maxRows = StreamingXlsxExport.ParseQuantity(quantity);
        return StreamingXlsxExport.Start("systemdata-services.xlsx", async (output, ct) =>
        {
            var items = (await _service.ListAsync(tenant, ct)).Where(x => (string.IsNullOrWhiteSpace(kind) || x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(search) || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || x.ServiceNId.Contains(search, StringComparison.OrdinalIgnoreCase)));
            items = string.Equals(sortField, "name", StringComparison.OrdinalIgnoreCase) ? (string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase) ? items.OrderBy(x => x.Name) : items.OrderByDescending(x => x.Name)) : items.OrderBy(x => x.ServiceNId);
            var workbook = await StreamingXlsxWriter.CreateAsync(output, ct);
            var selectedColumns = StreamingXlsxExport.SelectColumns(columns,
                new StreamingXlsxExport.Column<ServiceCatalogResponse>("serviceNId", "ServiceNId", item => item.ServiceNId),
                new StreamingXlsxExport.Column<ServiceCatalogResponse>("kind", "种类", item => item.Kind),
                new StreamingXlsxExport.Column<ServiceCatalogResponse>("name", "名称", item => item.Name),
                new StreamingXlsxExport.Column<ServiceCatalogResponse>("entryPoint", "入口", item => item.EntryPoint),
                new StreamingXlsxExport.Column<ServiceCatalogResponse>("healthPath", "健康地址", item => item.HealthPath),
                new StreamingXlsxExport.Column<ServiceCatalogResponse>("status", "状态", item => item.Status));
            await workbook.WriteRowAsync(selectedColumns.Select(column => column.Title), ct);
            foreach (var item in items.Take(maxRows)) await workbook.WriteRowAsync(selectedColumns.Select(column => column.Value(item)), ct);
            await workbook.DisposeAsync();
        }, cancellationToken);
    }
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
    private async Task<ActionResult<T>> Execute<T>(Func<string, Task<T>> action) { if (!TryGetActorContext(out var t, out _)) return UnauthorizedEnvelope(); try { return Ok(await action(t)); } catch (ConcurrencyException ex) { return StatusCodeEnvelope(409, "SD_THEME_POLICY_CONFLICT", ex.Message); } catch (ValidationException ex) { return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message); } catch (ArgumentException ex) { return BadRequestEnvelope("SD_VALIDATION_FAILED", ex.Message); } }
}
