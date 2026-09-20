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

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Models;

/// <summary>已映射的平台身份，同时保留外部来源和用户信息用于后续会话校验。</summary>
public sealed record EmbeddedIdentity(string TenantNId, string UserNId, string SessionNId, string SecurityVersion)
{
    public string? AccountNId { get; init; }
    public string SourceNId { get; init; } = string.Empty;
    public string ExternalTenantNId { get; init; } = string.Empty;
    public string ExternalSubject { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}
