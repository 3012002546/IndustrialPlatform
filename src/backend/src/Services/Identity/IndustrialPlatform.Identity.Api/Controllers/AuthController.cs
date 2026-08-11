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

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? GetUserAgent() => Request.Headers.UserAgent.ToString();

    private static ObjectResult BadRequestEnvelope(string code, string message) =>
        StatusCodeEnvelope(StatusCodes.Status400BadRequest, code, message);

    private static ObjectResult StatusCodeEnvelope(int statusCode, string code, string message) =>
        new(ApiResult.Fail<object?>(code, message)) { StatusCode = statusCode };
}
