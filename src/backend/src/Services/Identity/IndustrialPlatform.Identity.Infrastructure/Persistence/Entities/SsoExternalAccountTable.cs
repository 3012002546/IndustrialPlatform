using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// identity_sso_external_account 表的持久化模型(§26.3,§6 公共列 + 业务列,snake_case)。
/// 通过复合外键引用 Provider 与 User 的 (Id, IsDeleted);external_subject 为 IdP 侧主体标识。
/// </summary>
[SugarTable("identity_sso_external_account")]
public sealed class SsoExternalAccountTable : ISsoLifecycleRow
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

    [SugarColumn(ColumnName = "n_id")]
    public string NId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "normalized_n_id")]
    public string NormalizedNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "sso_provider_id")]
    public Guid SsoProviderId { get; set; }

    [SugarColumn(ColumnName = "sso_provider_is_deleted")]
    public bool SsoProviderIsDeleted { get; set; }

    [SugarColumn(ColumnName = "external_subject")]
    public string ExternalSubject { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "user_id")]
    public Guid UserId { get; set; }

    [SugarColumn(ColumnName = "user_is_deleted")]
    public bool UserIsDeleted { get; set; }

    [SugarColumn(ColumnName = "external_name")]
    public string? ExternalName { get; set; }

    [SugarColumn(ColumnName = "external_email")]
    public string? ExternalEmail { get; set; }

    [SugarColumn(ColumnName = "last_login_on")]
    public DateTimeOffset? LastLoginOn { get; set; }
}
