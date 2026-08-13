using IndustrialPlatform.Identity.Domain.Sso;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// identity_sso_browser_session 表的持久化模型(§26.6,§6 公共列 + 业务列,snake_case)。
/// 只存会话句柄的 SHA-256 哈希(session_handle_hash),明文句柄仅在 Cookie 与内存流转;
/// provider_n_id 记录建立会话的 Provider(联邦注销目标)。
/// </summary>
[SugarTable("identity_sso_browser_session")]
public sealed class SsoBrowserSessionTable : ISsoLifecycleRow
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

    [SugarColumn(ColumnName = "normalized_n_id")]
    public string NormalizedNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "provider_n_id")]
    public string ProviderNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "user_id")]
    public Guid UserId { get; set; }

    [SugarColumn(ColumnName = "user_is_deleted")]
    public bool UserIsDeleted { get; set; }

    [SugarColumn(ColumnName = "session_handle_hash")]
    public string SessionHandleHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "auth_version")]
    public int AuthVersion { get; set; }

    [SugarColumn(ColumnName = "last_activity_on")]
    public DateTimeOffset LastActivityOn { get; set; }

    [SugarColumn(ColumnName = "expires_on")]
    public DateTimeOffset ExpiresOn { get; set; }

    [SugarColumn(ColumnName = "revoked_on")]
    public DateTimeOffset? RevokedOn { get; set; }

    [SugarColumn(ColumnName = "revoke_reason")]
    public SsoBrowserSessionRevokeReason? RevokeReason { get; set; }
}
