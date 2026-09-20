using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Web;

/// <summary>独立服务根据 MES 人员列表核对 account 并建立自己的页面会话。</summary>
public static class StandaloneSessionEndpoint
{
    public static IEndpointRouteBuilder MapStandaloneSession(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/embedded/standalone/session", StartAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> StartAsync(
        HttpContext context,
        IEmbeddedSourcePrincipalResolver users,
        IEmbeddedHandshakeStore store,
        EmbeddedHostHandshakeOptions options,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        string? account;
        try
        {
            account = EmbeddedAccountQuery.Read(context);
        }
        catch (EmbeddedHandshakeException exception)
        {
            return Results.Json(new { code = exception.Code, message = exception.Message }, statusCode: exception.StatusCode);
        }
        if (account is null)
            return Results.BadRequest(new { code = "EMBEDDED_ACCOUNT_REQUIRED", message = "独立协作入口需要 account。" });
        var origin = context.Request.Headers.Origin.ToString();
        if (!options.IsAllowedOrigin(origin))
            return Results.Json(new { code = "EMBEDDED_ORIGIN_FORBIDDEN", message = "页面来源不在允许列表中。" }, statusCode: 403);
        var tenant = configuration["EmbeddedCollaboration:PlatformTenantNId"];
        if (string.IsNullOrWhiteSpace(tenant))
            return Results.Problem("独立协作租户未配置。", statusCode: 503);
        var user = await users.ResolveAsync(context, account, cancellationToken);
        if (user is null)
            return Results.Json(new { code = "EMBEDDED_ACCOUNT_NOT_FOUND", message = "account 不在 MES 用户结果集中。" }, statusCode: 403);

        var identity = new EmbeddedIdentity(tenant, user.ExternalSubject, user.SessionNId, user.SecurityVersion)
        {
            AccountNId = account,
            SourceNId = user.SourceNId,
            ExternalTenantNId = user.ExternalTenantNId,
            ExternalSubject = user.ExternalSubject,
            DisplayName = user.DisplayName,
        };
        var binding = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiresOn = DateTimeOffset.UtcNow.Add(options.SessionLifetime);
        try
        {
            var session = await store.CreateSessionAsync(identity, EmbeddedSessionBinding.Hash(binding), EmbeddedSessionToken.Hash(token), expiresOn, cancellationToken);
            context.Response.Cookies.Append(options.CookieName, token, CookieOptions(session.ExpiresOn));
            context.Response.Cookies.Append(options.BrowserBindingCookieName, binding, CookieOptions(session.ExpiresOn));
            return Results.Ok(new
            {
                mode = "Standalone",
                account,
                session.ExpiresOn,
                pageSession = new { token, binding },
                identity = new
                {
                    session.Identity.TenantNId,
                    session.Identity.UserNId,
                    session.Identity.DisplayName,
                    session.Identity.SecurityVersion,
                },
            });
        }
        catch (EmbeddedPersistenceException)
        {
            return Results.Problem("独立协作会话存储不可用。", statusCode: 503);
        }
    }

    private static CookieOptions CookieOptions(DateTimeOffset expiresOn) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None,
        IsEssential = true,
        Path = "/",
        Expires = expiresOn,
    };

}
