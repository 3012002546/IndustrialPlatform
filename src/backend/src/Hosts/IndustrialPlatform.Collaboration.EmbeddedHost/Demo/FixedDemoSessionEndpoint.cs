using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Demo;

/// <summary>
/// 独立演示账户的本地入口。它只在 Program.cs 明确启用 FixedDemo 时映射，
/// 通过现有“挑战、身份断言、会话交换”握手建立 HttpOnly 会话，不伪造访问令牌。
/// </summary>
public static class FixedDemoSessionEndpoint
{
    public static IEndpointRouteBuilder MapFixedDemoSession(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/embedded/demo/session", StartAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> StartAsync(
        HttpContext context,
        EmbeddedHostHandshakeService service,
        EmbeddedHostHandshakeOptions options,
        FixedDemoAccountCatalog accounts,
        CancellationToken cancellationToken)
    {
        var accountValues = context.Request.Query["account"];
        if (accountValues.Count > 1)
            return Results.BadRequest(new { code = "EMBEDDED_DEMO_ACCOUNT_AMBIGUOUS", message = "演示 account 参数只能出现一次。" });
        var account = accounts.Resolve(accountValues.Count == 0 ? null : accountValues[0]);
        if (account is null)
            return Results.BadRequest(new { code = "EMBEDDED_DEMO_ACCOUNT_INVALID", message = "演示 account 仅支持配置白名单中的账户。" });

        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
            origin = options.AllowedParentOrigins.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(origin))
            return Results.Problem("FixedDemo 未配置允许的父页面来源。", statusCode: StatusCodes.Status503ServiceUnavailable);

        // 演示端点在服务端预置参考 cookie；浏览器无需手工复制 MES cookie。
        // 后续请求只使用握手生成的 HttpOnly industrial_embedded_session。
        var binding = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        context.Request.Headers.Origin = origin;
        context.Request.Headers.Cookie = $"{options.SourceSessionCookieName}={account.AccountNId}; {options.BrowserBindingCookieName}={binding}";

        try
        {
            var challenge = await service.CreateChallengeAsync(origin, context, cancellationToken);
            var assertion = await service.IssueAssertionAsync(challenge.Nonce, context, cancellationToken);
            var session = await service.CompleteAsync(assertion, context, cancellationToken);
            context.Response.Cookies.Append(options.BrowserBindingCookieName, binding, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/",
                Expires = session.ExpiresOn,
            });
            return Results.Ok(new
            {
                mode = "FixedDemo",
                account = account.AccountNId,
                currentUser = account.ExternalSubject,
                session.ExpiresOn,
                pageSession = new { token = session.SessionToken, binding },
                identity = new
                {
                    session.Identity.TenantNId,
                    session.Identity.UserNId,
                    session.Identity.DisplayName,
                    session.Identity.SourceNId,
                    session.Identity.ExternalSubject,
                },
            });
        }
        catch (EmbeddedHandshakeException exception)
        {
            return Results.Json(new { code = exception.Code, message = exception.Message }, statusCode: exception.StatusCode);
        }
        catch (EmbeddedPersistenceException)
        {
            return Results.Problem("FixedDemo SQLite 会话存储不可用。", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
