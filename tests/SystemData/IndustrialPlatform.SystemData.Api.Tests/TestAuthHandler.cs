using System.Security.Claims;
using System.Text.Encodings.Web;
using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Api.Tests;

/// <summary>
/// 测试认证方案常量(TASK-SD-006)。API 测试以测试方案替换 JwtBearer,
/// 由 PrincipalFactory 按用例生成身份;401/403 信封与生产 JwtBearer 事件输出一致。
/// </summary>
public static class TestAuthDefaults
{
    /// <summary>测试认证方案名。</summary>
    public const string Scheme = "Test";
}

/// <summary>测试认证处理器选项。</summary>
public sealed class TestAuthHandlerOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// 由测试工厂注入的身份工厂;<c>null</c> 结果视为未认证(401 信封)。
    /// </summary>
    public Func<HttpContext, ClaimsPrincipal?> PrincipalFactory { get; set; } = _ => null;
}

/// <summary>
/// 测试认证处理器(TASK-SD-006):按用例构造 ClaimsPrincipal,
/// 未认证 401 信封、已认证无权限 403 信封,与生产输出形状一致。
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<TestAuthHandlerOptions>
{
    /// <summary>初始化测试认证处理器。</summary>
    public TestAuthHandler(
        IOptionsMonitor<TestAuthHandlerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var principal = Options.PrincipalFactory(Context);
        if (principal is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = principal.Identities.FirstOrDefault(i => i.IsAuthenticated);
        return Task.FromResult(identity is null
            ? AuthenticateResult.NoResult()
            : AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    /// <inheritdoc />
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json; charset=utf-8";
        await Response.WriteAsJsonAsync(ApiResult.Fail<object?>("401", "未认证或登录已失效，请重新登录。"));
    }

    /// <inheritdoc />
    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json; charset=utf-8";
        await Response.WriteAsJsonAsync(ApiResult.Fail<object?>("SD_PERMISSION_DENIED", "无权限执行此操作。"));
    }

    /// <summary>由测试用户构造 ClaimsPrincipal(声明名与 Identity 签发令牌对齐)。</summary>
    public static ClaimsPrincipal BuildPrincipal(TestUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimConstants.UserNId, user.UserNId),
            new(ClaimConstants.UserName, user.UserName),
            new(ClaimConstants.TenantId, user.TenantNId),
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimConstants.Role, role)));
        claims.AddRange(user.PermissionNIds.Select(permission => new Claim(SystemDataClaimTypes.PermissionNId, permission)));
        var identity = new ClaimsIdentity(claims, TestAuthDefaults.Scheme, ClaimConstants.UserName, ClaimConstants.Role);
        return new ClaimsPrincipal(identity);
    }
}

/// <summary>测试身份(租户/用户/角色/权限)。</summary>
public sealed record TestUser
{
    /// <summary>用户业务标识。</summary>
    public string UserNId { get; init; } = "user-actor";

    /// <summary>用户名。</summary>
    public string UserName { get; init; } = "api-tester";

    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; init; } = "tenant-001";

    /// <summary>角色。</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>权限 NId 集合。</summary>
    public IReadOnlyList<string> PermissionNIds { get; init; } = [];
}
