using System.Security.Claims;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Contracts.Authentication;
using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.Identity.Api.Controllers;

/// <summary>
/// 登录与当前用户端点(§15.1/§15.2):/api/v1/auth/login、/api/v1/auth/me。
/// 错误统一映射为 ApiResult 信封,不暴露密码/Token/用户是否存在。
/// </summary>
[ApiController]
[Route("auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthenticationService _service;

    public AuthController(IAuthenticationService service)
    {
        _service = service;
    }

    /// <summary>登录:校验凭证并签发 Access/Refresh Token(§15.1)。</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthSession>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _service.LoginAsync(request, GetClientIp(), GetUserAgent(), cancellationToken);
            return session;
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (AuthenticationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>当前用户(§15.2):读取 token sub(=UserNId),用户已删除/不存在返回 401 会话失效。</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthUser>> Me(CancellationToken cancellationToken)
    {
        var userNId = User.FindFirstValue(ClaimConstants.UserId);
        if (string.IsNullOrWhiteSpace(userNId))
        {
            return StatusCodeEnvelope(StatusCodes.Status401Unauthorized, "401", "登录已失效，请重新登录。");
        }

        try
        {
            var user = await _service.GetCurrentUserAsync(userNId, cancellationToken);
            return user;
        }
        catch (AuthenticationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>刷新(§15.3):旋转 Refresh Token(同 Family),校验过期/撤销/重放;成功返回完整新 AuthSession,失败 401 前端不得继续重试。</summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthSession>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _service.RefreshAsync(request, GetClientIp(), GetUserAgent(), cancellationToken);
            return session;
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (AuthenticationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>单会话注销(§13/§15.4):要求 Bearer Access Token,撤销 Refresh Token 所在 Family,并把 sid 写入撤销键直到 Access Token 到期。幂等。</summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _service.LogoutAsync(request, User.FindFirstValue("sid"), AccessTokenRemaining(), cancellationToken);
            return OkEnvelope();
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (AuthenticationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>全部会话注销(§13):撤销该用户全部刷新会话并推进 AuthVersion,所有已签发 Access/Refresh Token 失效。</summary>
    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        var userNId = User.FindFirstValue(ClaimConstants.UserId);
        if (string.IsNullOrWhiteSpace(userNId))
        {
            return StatusCodeEnvelope(StatusCodes.Status401Unauthorized, "401", "登录已失效，请重新登录。");
        }

        try
        {
            await _service.LogoutAllAsync(userNId, cancellationToken);
            return OkEnvelope();
        }
        catch (AuthenticationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

    /// <summary>修改密码(§13):校验当前密码与复杂度,更新哈希并撤销全部会话,前端需重新登录。</summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userNId = User.FindFirstValue(ClaimConstants.UserId);
        if (string.IsNullOrWhiteSpace(userNId))
        {
            return StatusCodeEnvelope(StatusCodes.Status401Unauthorized, "401", "登录已失效，请重新登录。");
        }

        try
        {
            await _service.ChangePasswordAsync(request, userNId, cancellationToken);
            return OkEnvelope();
        }
        catch (ValidationException ex)
        {
            return BadRequestEnvelope("ID_VALIDATION_FAILED", ex.Message);
        }
        catch (AuthenticationException ex)
        {
            return StatusCodeEnvelope(ex.StatusCode, ex.Code, ex.Message);
        }
    }

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

        // [Authorize] 已保证 Token 未过期;缺失/异常时保守取最小 TTL(数据库会话撤销为权威)。
        return TimeSpan.Zero;
    }

    private static ObjectResult OkEnvelope() =>
        new(ApiResult.Ok()) { StatusCode = StatusCodes.Status200OK };

    private static ObjectResult BadRequestEnvelope(string code, string message) =>
        StatusCodeEnvelope(StatusCodes.Status400BadRequest, code, message);

    private static ObjectResult StatusCodeEnvelope(int statusCode, string code, string message) =>
        new(ApiResult.Fail<object?>(code, message)) { StatusCode = statusCode };
}
