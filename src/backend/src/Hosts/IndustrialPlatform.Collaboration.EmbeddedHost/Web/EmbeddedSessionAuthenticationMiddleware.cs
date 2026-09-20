using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Web;

/// <summary>根据页级凭据或 Cookie 恢复独立会话身份；失效时拒绝认证。</summary>
public sealed class EmbeddedSessionAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IEmbeddedHandshakeStore store,
        EmbeddedHostHandshakeOptions options,
        IEmbeddedSubjectIdentityMapper mapper,
        IEmbeddedSourcePrincipalResolver? sourceResolver = null)
    {
        // FixedDemo is an explicit, anonymous account-switching entry point. Its
        // handler replaces the source cookie server-side before starting a new
        // handshake, so an old page cookie must not preempt it with a mismatch.
        if (context.Request.Path == "/embedded/demo/session"
            || context.Request.Path == "/embedded/standalone/session")
        {
            await next(context);
            return;
        }
        if (context.User.Identity?.IsAuthenticated != true)
        {
            if (EmbeddedHostHandshakeService.TryReadSessionCredential(context, options.CookieName, options.BrowserBindingCookieName, out var token, out var browserBinding))
            {
                try
                {
                    var session = await store.GetSessionAsync(EmbeddedSessionToken.Hash(token), EmbeddedSessionBinding.Hash(browserBinding), context.RequestAborted);
                    if (session is not null && session.RevokedOn is null && session.ExpiresOn > DateTimeOffset.UtcNow)
                    {
                        string? requestedAccountNId;
                        try
                        {
                            requestedAccountNId = EmbeddedAccountQuery.Read(context);
                        }
                        catch (EmbeddedHandshakeException exception)
                        {
                            context.Response.StatusCode = exception.StatusCode;
                            await context.Response.WriteAsJsonAsync(new { code = exception.Code }, context.RequestAborted);
                            return;
                        }
                        if (requestedAccountNId is not null && !string.Equals(session.Identity.AccountNId, requestedAccountNId, StringComparison.Ordinal))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsJsonAsync(new { code = "EMBEDDED_ACCOUNT_MISMATCH" }, context.RequestAborted);
                            return;
                        }
                        // The stored page credential is the identity boundary for
                        // same-origin A/B pages; do not consult a shared source
                        // cookie here, or one page could invalidate another.
                        // KeepAlive and the Hub session validator re-check the
                        // live MES source before extending/using the session.
                        if (await mapper.IsCurrentAsync(session, context.RequestAborted))
                        {
                            var claims = new[]
                            {
                                new Claim(ClaimTypes.NameIdentifier, session.Identity.UserNId),
                                new Claim(ClaimTypes.Name, session.Identity.DisplayName),
                                new Claim(JwtRegisteredClaimNames.Sub, session.Identity.UserNId),
                                new Claim("tenant_id", session.Identity.TenantNId),
                                new Claim("sid", session.Identity.SessionNId),
                                new Claim("ver", session.Identity.SecurityVersion),
                                new Claim("source_n_id", session.Identity.SourceNId),
                                new Claim("external_tenant_n_id", session.Identity.ExternalTenantNId),
                                new Claim("external_subject", session.Identity.ExternalSubject),
                                new Claim("source_session_n_id", session.Identity.SessionNId),
                                new Claim("display_name", session.Identity.DisplayName),
                                new Claim("account_n_id", session.Identity.AccountNId ?? string.Empty),
                                new Claim("embedded_session", "true"),
                                new Claim("embedded_epoch", session.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                                new Claim("exp", new DateTimeOffset(session.ExpiresOn.UtcDateTime).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
                            };
                            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "EmbeddedSession"));
                        }
                        else
                        {
                            await store.RevokeSessionAsync(session.SessionTokenHash, session.BrowserBindingHash, DateTimeOffset.UtcNow, context.RequestAborted);
                        }
                    }
                }
                catch (EmbeddedPersistenceException)
                {
                    // 持久化失败时不放行认证，不能回退到未校验的身份。
                }
            }
        }
        await next(context);
    }

    private static bool IsCurrentSource(EmbeddedIdentity identity, EmbeddedSourcePrincipal? source) =>
        source is not null
        && string.Equals(source.SourceNId, identity.SourceNId, StringComparison.Ordinal)
        && string.Equals(source.ExternalTenantNId, identity.ExternalTenantNId, StringComparison.Ordinal)
        && string.Equals(source.ExternalSubject, identity.ExternalSubject, StringComparison.Ordinal)
        && string.Equals(source.SessionNId, identity.SessionNId, StringComparison.Ordinal)
        && string.Equals(source.SecurityVersion, identity.SecurityVersion, StringComparison.Ordinal)
        && (source.AccountNId is null || string.Equals(source.AccountNId, identity.AccountNId, StringComparison.Ordinal));

}
