using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.Web.Initialization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.ReferenceData.Api.Controllers;

[ApiController]
[Route("internal/initialization/referencedata")]
public sealed class InternalInitializationController : ControllerBase
{
    private readonly ReferenceDataServiceInitializer _initializer;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public InternalInitializationController(
        ReferenceDataServiceInitializer initializer,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _initializer = initializer;
        _configuration = configuration;
        _environment = environment;
    }

    [HttpPost("inspect")]
    public async Task<IActionResult> Inspect([FromBody] InternalInitializationRequest request, CancellationToken cancellationToken)
    {
        if (!Authorized(request)) return Unauthorized();
        return Ok(await _initializer.InspectAsync(Context(request), cancellationToken));
    }

    [HttpPost("plan")]
    public async Task<IActionResult> Plan([FromBody] InternalInitializationRequest request, CancellationToken cancellationToken)
    {
        if (!Authorized(request)) return Unauthorized();
        if (request.Inspection is null) return BadRequest();
        return Ok(await _initializer.PlanAsync(Context(request), request.Inspection, cancellationToken));
    }

    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] InternalInitializationRequest request, CancellationToken cancellationToken)
    {
        if (!Authorized(request)) return Unauthorized();
        if (request.Plan is null) return BadRequest();
        return Ok(await _initializer.ApplyAsync(Context(request), request.Plan, cancellationToken));
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] InternalInitializationRequest request, CancellationToken cancellationToken)
    {
        if (!Authorized(request)) return Unauthorized();
        return Ok(await _initializer.VerifyAsync(Context(request), cancellationToken));
    }

    private ServiceInitializationContext Context(InternalInitializationRequest request) =>
        InternalInitializationAuthentication.BuildContext(request, "referencedata", "referencedata", _environment.EnvironmentName);

    private bool Authorized(InternalInitializationRequest request) =>
        request is not null
        && !string.IsNullOrWhiteSpace(request.OperationNId)
        && InternalInitializationAuthentication.IsAuthorized(Request, InternalInitializationAuthentication.GetConfiguredKey(_configuration));

}
