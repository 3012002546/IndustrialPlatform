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

[SugarTable("collaboration_embedded_browser_epoch")]
public sealed class EmbeddedBrowserEpochTable
{
    [SugarColumn(ColumnName = "browser_binding_hash", IsPrimaryKey = true)] public string BrowserBindingHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "epoch")] public long Epoch { get; set; }
}
