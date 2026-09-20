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

[SugarTable("collaboration_embedded_session")]
public sealed class EmbeddedSessionTable
{
    [SugarColumn(ColumnName = "session_token_hash", IsPrimaryKey = true)] public string SessionTokenHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "browser_binding_hash")] public string BrowserBindingHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "user_n_id")] public string UserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "session_n_id")] public string SessionNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "security_version")] public string SecurityVersion { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "source_n_id")] public string SourceNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "external_tenant_n_id")] public string ExternalTenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "external_subject")] public string ExternalSubject { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "display_name")] public string DisplayName { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "account_n_id", IsNullable = true)] public string? AccountNId { get; set; }
    [SugarColumn(ColumnName = "epoch")] public long Epoch { get; set; }
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset ExpiresOn { get; set; }
    [SugarColumn(ColumnName = "revoked_on", IsNullable = true)] public DateTimeOffset? RevokedOn { get; set; }
}
