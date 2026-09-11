using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>PF05 service assertion replay ledger. The nonce is never a browser-provided idempotency key.</summary>
[SugarTable("pf05_service_call_nonce")]
public sealed class TrustedServiceCallNonceTable
{
    [SugarColumn(ColumnName = "issuer", IsPrimaryKey = true, Length = 256)]
    public string Issuer { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "nonce", IsPrimaryKey = true, Length = 128)]
    public string Nonce { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "expires_on")]
    public DateTimeOffset ExpiresOn { get; set; }

    [SugarColumn(ColumnName = "created_on")]
    public DateTimeOffset CreatedOn { get; set; }
}
