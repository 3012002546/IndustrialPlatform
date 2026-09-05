using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.Web.Initialization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.ReferenceData.Api.Controllers;

[ApiController]
[Route("internal/initialization/referencedata")]
public sealed class InternalInitializationController : ControllerBase
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);
    private readonly ReferenceDataServiceInitializer _initializer;
    private readonly IConfiguration _configuration;
    private readonly IndustrialPlatform.ReferenceData.Api.Initialization.ReferenceDataHostContext _contextFactory;

    public InternalInitializationController(
        ReferenceDataServiceInitializer initializer,
        IConfiguration configuration,
        IndustrialPlatform.ReferenceData.Api.Initialization.ReferenceDataHostContext contextFactory)
    {
        _initializer = initializer;
        _configuration = configuration;
        _contextFactory = contextFactory;
    }

    [HttpPost("inspect")]
    public async Task<IActionResult> Inspect([FromBody] InternalInitializationRequest request, CancellationToken cancellationToken)
    {
        if (!Authorized(request)) return Unauthorized();
        return Raw(await _initializer.InspectAsync(Context(request), cancellationToken));
    }

    [HttpPost("plan")]
    public async Task<IActionResult> Plan([FromBody] InternalInitializationRequest request, CancellationToken cancellationToken)
    {
        if (!Authorized(request)) return Unauthorized();
        if (request.Inspection is null) return BadRequest();
        return Raw(await _initializer.PlanAsync(Context(request), request.Inspection, cancellationToken));
    }

    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] InternalInitializationRequest request, CancellationToken cancellationToken)
    {
        if (!Authorized(request)) return Unauthorized();
        if (request.Plan is null) return BadRequest();
        return Raw(await _initializer.ApplyAsync(Context(request), request.Plan, cancellationToken));
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] InternalInitializationRequest request, CancellationToken cancellationToken)
    {
        if (!Authorized(request)) return Unauthorized();
        return Raw(await _initializer.VerifyAsync(Context(request), cancellationToken));
    }

    private ServiceInitializationContext Context(InternalInitializationRequest request) =>
        _contextFactory.Create(request.OperationNId, request.TraceId, request.TenantNId, request.DesiredVersion, request.Policy);

    // The initialization protocol returns raw states/plans; it is not a business ApiResult endpoint.
    private ContentResult Raw(object value) => Content(System.Text.Json.JsonSerializer.Serialize(value, JsonOptions), "application/json");

    private bool Authorized(InternalInitializationRequest request) =>
        request is not null
        && !string.IsNullOrWhiteSpace(request.OperationNId)
        && InternalInitializationAuthentication.IsAuthorized(Request, InternalInitializationAuthentication.GetConfiguredKey(_configuration));

}
