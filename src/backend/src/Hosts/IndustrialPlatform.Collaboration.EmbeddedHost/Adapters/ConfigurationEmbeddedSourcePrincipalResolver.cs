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

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Adapters;

/// <summary>演示配置的登录解析器：根据上游 Cookie 查找固定用户及登录会话。</summary>
public sealed class ConfigurationEmbeddedSourcePrincipalResolver(IConfiguration configuration, EmbeddedHostHandshakeOptions options) : IEmbeddedSourcePrincipalResolver
{
    public Task<EmbeddedSourcePrincipal?> ResolveAsync(HttpContext context, string? requestedAccountNId, CancellationToken cancellationToken)
    {
        var sourceCookie = context.Request.Cookies[options.SourceSessionCookieName];
        if (!string.IsNullOrWhiteSpace(sourceCookie))
        {
            var sourceSession = configuration.GetSection($"EmbeddedCollaboration:SourceSessions:{sourceCookie}");
            var configured = IsActive(sourceSession) ? Read(sourceSession) : null;
            if (configured is not null && (requestedAccountNId is null
                || string.Equals(sourceCookie, requestedAccountNId, StringComparison.Ordinal)))
                return Task.FromResult<EmbeddedSourcePrincipal?>(configured with { AccountNId = requestedAccountNId });
        }

        if (context.User.Identity?.IsAuthenticated == true
            && (string.Equals(context.User.FindFirstValue("embedded_source_authenticated"), "true", StringComparison.Ordinal)
                || string.Equals(context.User.FindFirstValue("embedded_session"), "true", StringComparison.Ordinal)))
        {
            var sourceNId = context.User.FindFirstValue("source_n_id") ?? configuration["EmbeddedCollaboration:DefaultSourceNId"];
            var externalTenantNId = context.User.FindFirstValue("external_tenant_n_id");
            var externalSubject = context.User.FindFirstValue("external_subject")
                ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sessionNId = context.User.FindFirstValue("source_session_n_id") ?? context.User.FindFirstValue("sid");
            var securityVersion = context.User.FindFirstValue("security_version") ?? context.User.FindFirstValue("ver");
            if (sourceNId is not null && externalTenantNId is not null && externalSubject is not null && sessionNId is not null && securityVersion is not null)
                return Task.FromResult<EmbeddedSourcePrincipal?>(new EmbeddedSourcePrincipal(sourceNId, externalTenantNId, externalSubject, context.User.Identity?.Name ?? externalSubject, securityVersion, sessionNId, requestedAccountNId));
        }

        return Task.FromResult<EmbeddedSourcePrincipal?>(null);
    }

    private static EmbeddedSourcePrincipal? Read(IConfigurationSection section)
    {
        var source = section["SourceNId"];
        var tenant = section["ExternalTenantNId"];
        var subject = section["ExternalSubject"];
        var session = section["SessionNId"];
        var version = section["SecurityVersion"];
        return string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(version)
            ? null
            : new EmbeddedSourcePrincipal(source, tenant, subject, section["DisplayName"] ?? subject, version, session);
    }

    private static bool IsActive(IConfigurationSection section)
    {
        var status = section["Status"];
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
            return false;

        var enabled = section["Enabled"];
        return string.IsNullOrWhiteSpace(enabled) || bool.TryParse(enabled, out var value) && value;
    }
}
