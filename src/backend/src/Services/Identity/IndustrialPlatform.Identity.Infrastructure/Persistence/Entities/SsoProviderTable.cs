using IndustrialPlatform.Identity.Domain.Sso;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// identity_sso_provider 表的持久化模型(§26.3,§6 公共列 + 业务列,snake_case)。
/// 只存密钥/证书引用(secret_or_certificate_reference),绝不保存明文 Secret 或证书私钥;
/// 列表字段以 JSON 文本存储。
/// </summary>
[SugarTable("identity_sso_provider")]
public sealed class SsoProviderTable : ISsoLifecycleRow
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

    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "protocol")]
    public SsoProtocol Protocol { get; set; }

    [SugarColumn(ColumnName = "authority_or_metadata_url")]
    public string AuthorityOrMetadataUrl { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "client_id_or_entity_id")]
    public string ClientIdOrEntityId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "secret_or_certificate_reference")]
    public string? SecretOrCertificateReference { get; set; }

    [SugarColumn(ColumnName = "callback_path")]
    public string CallbackPath { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; }

    [SugarColumn(ColumnName = "auto_redirect")]
    public bool AutoRedirect { get; set; }

    [SugarColumn(ColumnName = "provisioning_mode")]
    public SsoProvisioningMode ProvisioningMode { get; set; }

    [SugarColumn(ColumnName = "logout_mode")]
    public SsoLogoutMode LogoutMode { get; set; }

    [SugarColumn(ColumnName = "allowed_email_domains")]
    public string AllowedEmailDomainsJson { get; set; } = "[]";

    [SugarColumn(ColumnName = "jit_default_role_n_ids")]
    public string JitDefaultRoleNIdsJson { get; set; } = "[]";
}
