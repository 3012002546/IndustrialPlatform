using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// <c>identity_bootstrap_credential</c> bootstrap 凭据交付记录(§29A.4)。
/// 只保存一次性领取/恢复引用的 SHA-256 哈希与状态机,明文临时密码绝不入库;
/// 临时密码只在初始化/恢复成功结果的受保护响应中出现一次。
/// </summary>
[SugarTable("identity_bootstrap_credential")]
public sealed class BootstrapCredentialTable
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

    [SugarColumn(ColumnName = "user_n_id")]
    public string UserNId { get; set; } = string.Empty;

    /// <summary>交付状态(0=Pending,1=Delivered,2=Recovered,3=Revoked)。</summary>
    [SugarColumn(ColumnName = "state")]
    public int State { get; set; }

    /// <summary>一次性领取引用哈希(SHA-256);引用本身只存在于内存/受保护响应。</summary>
    [SugarColumn(ColumnName = "delivery_reference_hash")]
    public string? DeliveryReferenceHash { get; set; }

    /// <summary>一次性恢复引用哈希(SHA-256);紧急恢复门禁凭此授权。</summary>
    [SugarColumn(ColumnName = "recovery_reference_hash")]
    public string RecoveryReferenceHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "delivered_on")]
    public DateTimeOffset? DeliveredOn { get; set; }

    [SugarColumn(ColumnName = "recovered_on")]
    public DateTimeOffset? RecoveredOn { get; set; }
}
