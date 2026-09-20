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

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Services;

/// <summary>Reads the optional page account selector with strict ambiguity rules.</summary>
public static class EmbeddedAccountQuery
{
    public static string? Read(HttpContext context)
    {
        var values = context.Request.Query["account"];
        if (values.Count > 1)
            throw new EmbeddedHandshakeException(StatusCodes.Status400BadRequest, "EMBEDDED_ACCOUNT_AMBIGUOUS", "account 参数只能出现一次。");
        if (values.Count == 0) return null;
        var value = values[0];
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Length > 128)
            throw new EmbeddedHandshakeException(StatusCodes.Status400BadRequest, "EMBEDDED_ACCOUNT_INVALID", "account 参数不能为空且必须是单值。");
        return value;
    }

    public static void EnsureMatches(EmbeddedIdentity identity, string? requestedAccountNId)
    {
        if (requestedAccountNId is not null && !string.Equals(identity.AccountNId, requestedAccountNId, StringComparison.Ordinal))
            throw new EmbeddedHandshakeException(StatusCodes.Status403Forbidden, "EMBEDDED_ACCOUNT_MISMATCH", "account 与当前页面会话账户不匹配。");
    }
}
