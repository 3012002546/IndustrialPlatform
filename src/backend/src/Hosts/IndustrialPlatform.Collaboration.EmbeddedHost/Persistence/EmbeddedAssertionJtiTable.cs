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

[SugarTable("collaboration_embedded_assertion_jti")]
public sealed class EmbeddedAssertionJtiTable
{
    [SugarColumn(ColumnName = "jti", IsPrimaryKey = true)] public string Jti { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset ExpiresOn { get; set; }
}
