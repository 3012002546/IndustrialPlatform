using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// <c>identity_write_idempotency</c> 写请求幂等记录(§29A.5)。
/// 同一 (租户, 执行者, Idempotency-Key) 记录请求内容哈希;只存哈希,不含请求体/响应。
/// </summary>
[SugarTable("identity_write_idempotency")]
public sealed class IdentityWriteIdempotencyTable
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

    [SugarColumn(ColumnName = "actor_user_n_id")]
    public string ActorUserNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "idempotency_key")]
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>请求内容 SHA-256 哈希(不含请求体明文)。</summary>
    [SugarColumn(ColumnName = "request_hash")]
    public string RequestHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "completed")]
    public bool Completed { get; set; }

    [SugarColumn(ColumnName = "completed_on")]
    public DateTimeOffset? CompletedOn { get; set; }
}
