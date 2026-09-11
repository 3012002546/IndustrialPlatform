using IndustrialPlatform.Web.Results;
using IndustrialPlatform.Identity.Contracts.Collaboration;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>PF05 受信目录入口。普通用户 Bearer 绝不可用作服务调用凭据。</summary>
[ApiController]
[AllowAnonymous]
[Route("~/internal/pf05/identity")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CollaborationDirectoryController : ControllerBase
{
    private readonly TrustedServiceCallValidator _trustedCalls;
    private readonly ITrustedServiceCallNonceStore _nonceStore;
    private readonly ICollaborationIdentityDirectory _directory;

    public CollaborationDirectoryController(
        TrustedServiceCallValidator trustedCalls,
        ITrustedServiceCallNonceStore nonceStore,
        ICollaborationIdentityDirectory directory)
    {
        _trustedCalls = trustedCalls;
        _nonceStore = nonceStore;
        _directory = directory;
    }

    [HttpGet("users")]
    public async Task<IActionResult> Search(
        [FromQuery] string? keyword,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var validation = await _trustedCalls.ValidateAsync(
            Request,
            "identity.pf05",
            "directory.search",
            _nonceStore,
            cancellationToken);
        if (validation.Status != TrustedServiceCallValidationStatus.Valid)
            return Failure(validation.Status);

        try
        {
            var result = await _directory.SearchAsync(
                validation.Call!,
                new CollaborationUserSearchRequest(keyword ?? string.Empty, cursor, limit),
                cancellationToken);
            return Ok(ApiResult.Ok(result));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResult.Fail<object?>("ID_VALIDATION_FAILED", exception.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status401Unauthorized,
                ApiResult.Fail<object?>("PF05_SERVICE_CALL_INVALID", "The trusted actor is no longer active."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResult.Fail<object?>("COLLAB_IDENTITY_DIRECTORY_UNAVAILABLE", "Collaboration directory is unavailable."));
        }
    }

    [HttpGet("users/{userNId}")]
    public async Task<IActionResult> Get(string userNId, CancellationToken cancellationToken)
    {
        var validation = await _trustedCalls.ValidateAsync(
            Request,
            "identity.pf05",
            "directory.get",
            _nonceStore,
            cancellationToken);
        if (validation.Status != TrustedServiceCallValidationStatus.Valid)
            return Failure(validation.Status);

        try
        {
            var result = await _directory.GetAsync(validation.Call!, userNId, cancellationToken);
            return Ok(ApiResult.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResult.Fail<object?>("ID_RESOURCE_NOT_FOUND", "User was not found."));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResult.Fail<object?>("ID_VALIDATION_FAILED", exception.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status401Unauthorized,
                ApiResult.Fail<object?>("PF05_SERVICE_CALL_INVALID", "The trusted actor is no longer active."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResult.Fail<object?>("COLLAB_IDENTITY_DIRECTORY_UNAVAILABLE", "Collaboration directory is unavailable."));
        }
    }

    private ObjectResult Failure(TrustedServiceCallValidationStatus status) => status switch
    {
        TrustedServiceCallValidationStatus.Replayed => StatusCode(StatusCodes.Status401Unauthorized,
            ApiResult.Fail<object?>("PF05_SERVICE_CALL_REPLAYED", "PF05 service assertion has already been used.")),
        TrustedServiceCallValidationStatus.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable,
            ApiResult.Fail<object?>("PF05_SERVICE_AUTH_UNAVAILABLE", "PF05 service assertion validation is unavailable.")),
        _ => StatusCode(StatusCodes.Status401Unauthorized,
            ApiResult.Fail<object?>("PF05_SERVICE_CALL_INVALID", "PF05 service assertion is invalid.")),
    };
}
