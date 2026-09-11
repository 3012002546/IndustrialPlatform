using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>PF05 step-up grant ledger. Proof material is stored only as a SHA-256 hash.</summary>
[SugarTable("pf05_step_up_grant")]
public sealed class StepUpGrantTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id", Length = 128)] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "actor_user_n_id", Length = 128)] public string ActorUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "actor_session_n_id", Length = 128)] public string ActorSessionNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "actor_security_version", Length = 64)] public string ActorSecurityVersion { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "action", Length = 96)] public string Action { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "request_n_id", Length = 64)] public string RequestNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "scope_checksum", Length = 64)] public string ScopeChecksum { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "request_hash", Length = 64)] public string RequestHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "binding_hash", Length = 64)] public string BindingHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "proof_hash", Length = 64)] public string ProofHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "issued_on")] public DateTimeOffset IssuedOn { get; set; }
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset ExpiresOn { get; set; }
    [SugarColumn(ColumnName = "consumed_on")] public DateTimeOffset? ConsumedOn { get; set; }
    [SugarColumn(ColumnName = "consumed_by_service", Length = 64)] public string? ConsumedByService { get; set; }
    [SugarColumn(ColumnName = "consumed_receipt_n_id", Length = 128)] public string? ConsumedReceiptNId { get; set; }
}
