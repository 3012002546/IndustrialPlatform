using System.Security.Claims;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Sso;
using IndustrialPlatform.Identity.Contracts.Sso;
using IndustrialPlatform.Identity.Domain.Sso;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// 企业级联合登录端点(§26.4/§26.5/§26.9):发现、授权、回调、票据交换与登出。
/// 浏览器 SSO 会话句柄只存在于 HttpOnly + Secure + SameSite=Lax Cookie,绝不进入响应体;
/// 回调/复用成功后 302 跳转前端 <c>/auth/sso/callback?ticket=…</c>。
/// </summary>
[ApiController]
[Route("sso")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SsoController : ControllerBase
{
    private readonly ISsoService _service;
    private readonly IOptions<SsoOptions> _ssoOptions;
    private readonly IOptions<AuthenticationOptions> _authOptions;

    /// <summary>初始化 SSO 控制器。</summary>
    public SsoController(
        ISsoService service,
        IOptions<SsoOptions> ssoOptions,
        IOptions<AuthenticationOptions> authOptions)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(ssoOptions);
        ArgumentNullException.ThrowIfNull(authOptions);
        _service = service;
        _ssoOptions = ssoOptions;
        _authOptions = authOptions;
    }

    /// <summary>发现启用中的企业登录源(§26.4);connection 为可选名称过滤。</summary>
    [AllowAnonymous]
    [HttpGet("discovery")]
    public async Task<ActionResult<IReadOnlyList<SsoDiscoveryProvider>>> Discovery(
        [FromQuery] string? connection,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.DiscoveryAsync(connection, cancellationToken));
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>开始授权(§26.4):先尝试复用浏览器 SSO 会话,否则跳转/选择 Provider。</summary>
    [AllowAnonymous]
    [HttpGet("authorize")]
    public async Task<ActionResult<SsoBeginResponse>> Authorize(
        [FromQuery] string? clientId,
        [FromQuery] string? returnUrl,
        [FromQuery] string? providerNId,
        CancellationToken cancellationToken)
    {
        var tenantNId = _authOptions.Value.DefaultTenantNId;
        var sessionHandle = Request.Cookies[_ssoOptions.Value.SessionCookieName];
        try
        {
            if (!string.IsNullOrWhiteSpace(sessionHandle))
            {
                var reused = await _service.ReuseBrowserSessionAsync(sessionHandle, tenantNId, clientId, returnUrl, cancellationToken);
                if (reused.Reused)
                {
                    return reused;
                }
            }

            return await _service.BeginAuthorizeAsync(tenantNId, clientId, returnUrl, providerNId, GetRequestBaseUrl(), cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>指定 Provider 开始授权(§26.4,登录页选择后的直达入口)。</summary>
    [AllowAnonymous]
    [HttpGet("authorize/{providerNId}")]
    public async Task<ActionResult<SsoBeginResponse>> AuthorizeProvider(
        string providerNId,
        [FromQuery] string? clientId,
        [FromQuery] string? returnUrl,
        CancellationToken cancellationToken)
    {
        var tenantNId = _authOptions.Value.DefaultTenantNId;
        var sessionHandle = Request.Cookies[_ssoOptions.Value.SessionCookieName];
        try
        {
            if (!string.IsNullOrWhiteSpace(sessionHandle))
            {
                var reused = await _service.ReuseBrowserSessionAsync(sessionHandle, tenantNId, clientId, returnUrl, cancellationToken);
                if (reused.Reused)
                {
                    return reused;
                }
            }

            return await _service.BeginAuthorizeAsync(tenantNId, clientId, returnUrl, providerNId, GetRequestBaseUrl(), cancellationToken);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>OIDC 回调(§26.5):校验 code/state/nonce/PKCE,写入浏览器会话 Cookie 并跳转前端。</summary>
    [AllowAnonymous]
    [HttpGet("callback/oidc/{providerNId}")]
    public async Task<IActionResult> CallbackOidc(string providerNId, CancellationToken cancellationToken)
    {
        var parameters = Request.Query
            .Where(kv => !string.IsNullOrEmpty(kv.Value.ToString()))
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        try
        {
            var result = await _service.HandleOidcCallbackAsync(providerNId, parameters, cancellationToken);
            return RedirectToFrontend(result);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>SAML 回调(§26.5,HTTP-POST 绑定):校验 RelayState/签名/断言,写入 Cookie 并跳转前端。</summary>
    [AllowAnonymous]
    [HttpPost("callback/saml/{providerNId}")]
    public async Task<IActionResult> CallbackSaml(string providerNId, CancellationToken cancellationToken)
    {
        var parameters = Request.Form
            .Where(kv => !string.IsNullOrEmpty(kv.Value.ToString()))
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        try
        {
            var result = await _service.HandleSamlCallbackAsync(providerNId, parameters, cancellationToken);
            return RedirectToFrontend(result);
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>一次性票据交换(§26.5):消费票据签发完整认证会话,刷新 Cookie 期限后返回会话(句柄不回传)。</summary>
    [AllowAnonymous]
    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange(SsoExchangeRequest request, CancellationToken cancellationToken)
    {
        var ticket = request.Ticket;
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", "登录票据不能为空。");
        }

        try
        {
            var result = await _service.ExchangeTicketAsync(ticket, GetClientIp(), GetUserAgent(), cancellationToken);
            SetSsoSessionCookie(result.BrowserSessionHandle);
            return Ok(new { result.Session, result.ReturnUrl });
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>登出(§26.9):撤销刷新会话族/sid/浏览器会话,删除 Cookie;Federated 时返回 IdP 跳转地址。</summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<ActionResult<SsoLogoutResponse>> Logout(SsoLogoutRequest request, CancellationToken cancellationToken)
    {
        var sessionHandle = Request.Cookies[_ssoOptions.Value.SessionCookieName];
        var sid = User.FindFirstValue("sid");
        try
        {
            var response = await _service.LogoutAsync(
                sessionHandle,
                request.RefreshToken,
                request.ClientId,
                request.PostLogoutRedirectUri,
                sid,
                AccessTokenRemaining(),
                GetClientIp(),
                GetUserAgent(),
                cancellationToken);
            DeleteSsoSessionCookie();
            return response;
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (SsoException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>回调成功后写入浏览器会话 Cookie(HttpOnly + SameSite=Lax)并 302 跳转前端票据页。</summary>
    private RedirectResult RedirectToFrontend(SsoCallbackResult result)
    {
        SetSsoSessionCookie(result.BrowserSessionHandle);
        var baseUrl = _ssoOptions.Value.FrontendBaseUrl.TrimEnd('/');
        var uri = $"{baseUrl}/auth/sso/callback?ticket={Uri.EscapeDataString(result.Ticket)}";
        if (!string.IsNullOrWhiteSpace(result.ReturnUrl))
        {
            uri += "&returnUrl=" + Uri.EscapeDataString(result.ReturnUrl);
        }

        return Redirect(uri);
    }

    private void SetSsoSessionCookie(string sessionHandle)
    {
        var options = _ssoOptions.Value;
        Response.Cookies.Append(options.SessionCookieName, sessionHandle, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.UseSecureCookies,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = IdentitySsoBrowserSession.DefaultAbsoluteLifetime,
            IsEssential = true,
        });
    }

    private void DeleteSsoSessionCookie()
    {
        var options = _ssoOptions.Value;
        Response.Cookies.Delete(options.SessionCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.UseSecureCookies,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
    }

    private string GetRequestBaseUrl() => $"{Request.Scheme}://{Request.Host}";

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? GetUserAgent() => Request.Headers.UserAgent.ToString();

    /// <summary>按 Access Token 的 exp(Unix 秒)计算剩余有效期,作为 sid 撤销键 TTL(§13)。</summary>
    private TimeSpan AccessTokenRemaining()
    {
        var exp = User.FindFirstValue("exp");
        if (long.TryParse(exp, out var epochSeconds))
        {
            var remaining = DateTimeOffset.FromUnixTimeSeconds(epochSeconds) - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                return remaining;
            }
        }

        return TimeSpan.Zero;
    }

    private static ObjectResult BadRequestEnvelope(string code, string message) =>
        StatusCodeEnvelope(StatusCodes.Status400BadRequest, code, message);

    private static ObjectResult StatusCodeEnvelope(int statusCode, string code, string message) =>
        new(ApiResult.Fail<object?>(code, message)) { StatusCode = statusCode };
}
