using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Collaboration.EmbeddedHost;

/// <summary>
/// 嵌入登录接口：创建挑战、签发上游身份断言、交换会话，以及查询、续期和撤销会话。
/// 允许匿名访问是握手入口要求，具体来源、上游登录和浏览器绑定由握手服务逐项校验。
/// </summary>
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

    [HttpPost("session/heartbeat")]
    public async Task<ActionResult<object>> Heartbeat(CancellationToken cancellationToken)
    {
        try
        {
            var session = await service.KeepAliveAsync(HttpContext, cancellationToken);
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

    [HttpGet("session")]
    public async Task<ActionResult<object>> Current(CancellationToken cancellationToken)
    {
        try
        {
            var token = EmbeddedHostHandshakeService.ReadSessionToken(HttpContext, options.CookieName);
            var binding = EmbeddedHostHandshakeService.ReadBrowserBinding(HttpContext, options.BrowserBindingCookieName);
            var session = await service.GetSessionAsync(token, binding, cancellationToken);
            if (session is not null)
                EmbeddedAccountQuery.EnsureMatches(session.Identity, EmbeddedAccountQuery.Read(HttpContext));
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

    private static object Projection(EmbeddedIdentity identity)
    {
        return new
        {
            identity.TenantNId,
            identity.UserNId,
            identity.DisplayName,
            identity.SecurityVersion,
            permissions = EmbeddedCollaborationPermissionCatalog.Permissions,
            roles = new[] { "embedded-collaboration-user" },
        };
    }

    private ObjectResult Failure(EmbeddedHandshakeException exception) => StatusCode(exception.StatusCode, new { code = exception.Code });
}
