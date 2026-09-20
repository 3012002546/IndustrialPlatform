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

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Persistence;

[SugarTable("collaboration_embedded_challenge")]
public sealed class EmbeddedChallengeTable
{
    [SugarColumn(ColumnName = "challenge")] public string Challenge { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "nonce", IsPrimaryKey = true)] public string Nonce { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "browser_binding_hash")] public string BrowserBindingHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "parent_origin")] public string ParentOrigin { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset ExpiresOn { get; set; }
    [SugarColumn(ColumnName = "consumed_on", IsNullable = true)] public DateTimeOffset? ConsumedOn { get; set; }
}
