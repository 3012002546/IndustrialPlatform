using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// <c>identity_seed_ledger</c> 种子账本(§29A.4)。三层种子(SystemCatalog/TenantSecurity/BootstrapAdmin)
/// 每次应用都追加记账:同 (tenant, seed, version) 幂等,同版本不同 checksum 视为 drift 拒绝;
/// 版本升级新增 ledger 版本。绝不记录种子内容、明文密码或 Secret。
/// </summary>
[SugarTable("identity_seed_ledger")]
public sealed class SeedLedgerTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public Guid Id { get; set; }

    [SugarColumn(ColumnName = "is_frozen")]
    public bool IsFrozen { get; set; }

    [SugarColumn(ColumnName = "is_locked")]
    public bool IsLocked { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }

    [SugarColumn(ColumnName = "entity_type")]
    public string EntityType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "created_on")]
    public DateTimeOffset CreatedOn { get; set; }

    [SugarColumn(ColumnName = "last_updated_on")]
    public DateTimeOffset LastUpdatedOn { get; set; }

    [SugarColumn(ColumnName = "optimistic_version")]
    public long OptimisticVersion { get; set; }

    [SugarColumn(ColumnName = "concurrency_version")]
    public Guid ConcurrencyVersion { get; set; }

    [SugarColumn(ColumnName = "tenant_n_id")]
    public string TenantNId { get; set; } = string.Empty;

    /// <summary>稳定种子键(SeedKey)。</summary>
    [SugarColumn(ColumnName = "seed_n_id")]
    public string SeedNId { get; set; } = string.Empty;

    /// <summary>不可变种子版本。</summary>
    [SugarColumn(ColumnName = "seed_version")]
    public string SeedVersion { get; set; } = string.Empty;

    /// <summary>种子类别枚举名(SystemBaseline/TenantBaseline/SecretBootstrap)。</summary>
    [SugarColumn(ColumnName = "seed_class")]
    public string SeedClass { get; set; } = string.Empty;

    /// <summary>种子作用域(默认 <c>system</c>)。</summary>
    [SugarColumn(ColumnName = "scope")]
    public string Scope { get; set; } = string.Empty;

    /// <summary>种子内容校验和(SHA-256 十六进制)。</summary>
    [SugarColumn(ColumnName = "checksum")]
    public string Checksum { get; set; } = string.Empty;

    /// <summary>种子状态枚举名(Applied/Pending/Failed/Skipped)。</summary>
    [SugarColumn(ColumnName = "status")]
    public string Status { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "applied_on")]
    public DateTimeOffset? AppliedOn { get; set; }

    /// <summary>关联 SystemData Operation 业务标识(非敏感)。</summary>
    [SugarColumn(ColumnName = "system_data_operation_n_id")]
    public string? SystemDataOperationNId { get; set; }

    /// <summary>关联追踪标识。</summary>
    [SugarColumn(ColumnName = "trace_id")]
    public string? TraceId { get; set; }
}
