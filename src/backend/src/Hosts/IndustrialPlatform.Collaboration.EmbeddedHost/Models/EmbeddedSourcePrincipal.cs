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

/// <summary>MES 服务端返回的当前登录信息，字段不能由浏览器自行填写。</summary>
public sealed record EmbeddedSourcePrincipal(
    string SourceNId,
    string ExternalTenantNId,
    string ExternalSubject,
    string DisplayName,
    string SecurityVersion,
    string SessionNId,
    string? AccountNId = null);
