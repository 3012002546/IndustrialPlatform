using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// identity_refresh_session 表的持久化模型(§6 公共列 + §13 业务列,snake_case)。
/// 只存 token 的 SHA-256 哈希(token_hash),原始 Refresh Token 仅在内存流转,
/// 用户/会话删除由复合外键 ON UPDATE CASCADE 同步 user_is_deleted / replaced_by_session_is_deleted。
/// </summary>
[SugarTable("identity_refresh_session")]
public sealed class RefreshSessionTable
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

    [SugarColumn(ColumnName = "n_id")]
    public string NId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "family_n_id")]
    public string FamilyNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "user_id")]
    public Guid UserId { get; set; }

    [SugarColumn(ColumnName = "user_is_deleted")]
    public bool UserIsDeleted { get; set; }

    [SugarColumn(ColumnName = "token_hash")]
    public string TokenHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "expires_on")]
    public DateTimeOffset ExpiresOn { get; set; }

    [SugarColumn(ColumnName = "used_on")]
    public DateTimeOffset? UsedOn { get; set; }

    [SugarColumn(ColumnName = "revoked_on")]
    public DateTimeOffset? RevokedOn { get; set; }

    [SugarColumn(ColumnName = "revoke_reason")]
    public string? RevokeReason { get; set; }

    [SugarColumn(ColumnName = "replaced_by_session_n_id")]
    public string? ReplacedBySessionNId { get; set; }

    [SugarColumn(ColumnName = "replaced_by_session_id")]
    public Guid? ReplacedBySessionId { get; set; }

    [SugarColumn(ColumnName = "replaced_by_session_is_deleted")]
    public bool? ReplacedBySessionIsDeleted { get; set; }

    [SugarColumn(ColumnName = "user_agent_hash")]
    public string? UserAgentHash { get; set; }

    [SugarColumn(ColumnName = "ip_address_hash")]
    public string? IpAddressHash { get; set; }
}
