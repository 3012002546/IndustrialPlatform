using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Collaboration.EmbeddedHost;

[ApiController]
[AllowAnonymous]
[Route("embedded")]
public sealed class EmbeddedHandshakeController(
    EmbeddedHostHandshakeService service,
    EmbeddedHostHandshakeOptions options) : ControllerBase
{
    [HttpPost("challenges")]
    public async Task<ActionResult<EmbeddedHandshakeChallenge>> CreateChallenge(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.CreateChallengeAsync(Request.Headers.Origin.ToString(), HttpContext, cancellationToken));
        }
        catch (EmbeddedHandshakeException exception)
        {
            return Failure(exception);
        }
        catch (EmbeddedPersistenceException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "EMBEDDED_HANDSHAKE_STORE_UNAVAILABLE" });
        }
    }

    [HttpGet("assertions")]
    public async Task<ActionResult<object>> GetAssertion([FromQuery] string nonce, CancellationToken cancellationToken)
    {
        try
        {
            var assertion = await service.IssueAssertionAsync(nonce, HttpContext, cancellationToken);
            return Ok(new { assertion, expiresOn = DateTimeOffset.UtcNow.AddSeconds(60) });
        }
        catch (EmbeddedHandshakeException exception)
        {
            return Failure(exception);
        }
        catch (EmbeddedPersistenceException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "EMBEDDED_HANDSHAKE_STORE_UNAVAILABLE" });
        }
    }

    [HttpPost("exchanges")]
    public async Task<ActionResult<object>> Exchange([FromBody] EmbeddedHandshakeCompletion request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await service.CompleteAsync(request.Assertion, HttpContext, cancellationToken);
            return Ok(new { session.ExpiresOn, session.Epoch, identity = Projection(session.Identity) });
        }
        catch (EmbeddedHandshakeException exception)
        {
            return Failure(exception);
        }
        catch (EmbeddedPersistenceException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "EMBEDDED_HANDSHAKE_STORE_UNAVAILABLE" });
        }
    }

    [HttpPost("session/renew")]
    public async Task<ActionResult<object>> Renew([FromBody] EmbeddedHandshakeCompletion request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await service.RenewAsync(request.Assertion, HttpContext, cancellationToken);
            return Ok(new { session.ExpiresOn, session.Epoch, identity = Projection(session.Identity) });
        }
        catch (EmbeddedHandshakeException exception)
        {
            return Failure(exception);
        }
        catch (EmbeddedPersistenceException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "EMBEDDED_HANDSHAKE_STORE_UNAVAILABLE" });
        }
    }

    [HttpPost("session/revoke")]
    public async Task<IActionResult> Revoke(CancellationToken cancellationToken)
    {
        try
        {
            await service.RevokeAsync(HttpContext, cancellationToken);
            return NoContent();
        }
        catch (EmbeddedHandshakeException exception)
        {
            return Failure(exception);
        }
        catch (EmbeddedPersistenceException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "EMBEDDED_HANDSHAKE_STORE_UNAVAILABLE" });
        }
    }

    [HttpGet("session")]
    public async Task<ActionResult<object>> Current(CancellationToken cancellationToken)
    {
        try
        {
            var token = EmbeddedHostHandshakeService.ReadSessionToken(HttpContext, options.CookieName);
            var binding = EmbeddedHostHandshakeService.ReadBrowserBinding(HttpContext, options.BrowserBindingCookieName);
            var session = await service.GetSessionAsync(token, binding, cancellationToken);
            return session is null
                ? Unauthorized(new { code = "EMBEDDED_SESSION_INVALID" })
                : Ok(new { session.ExpiresOn, session.Epoch, identity = Projection(session.Identity) });
        }
        catch (EmbeddedHandshakeException exception)
        {
            return Failure(exception);
        }
        catch (EmbeddedPersistenceException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "EMBEDDED_HANDSHAKE_STORE_UNAVAILABLE" });
        }
    }

    private static object Projection(EmbeddedIdentity identity) => new
    {
        identity.TenantNId,
        identity.UserNId,
        identity.DisplayName,
        identity.SecurityVersion,
    };

    private ObjectResult Failure(EmbeddedHandshakeException exception) => StatusCode(exception.StatusCode, new { code = exception.Code });
}
