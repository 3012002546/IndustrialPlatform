using IndustrialPlatform.Identity.Domain.Sso;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// identity_sso_client_endpoint 表的持久化模型(§26.7,§6 公共列 + 业务列,snake_case)。
/// 复合外键引用 Client 的 (Id, IsDeleted);同一 Client 下 (endpoint_type, normalized_uri) 唯一。
/// </summary>
[SugarTable("identity_sso_client_endpoint")]
public sealed class SsoClientEndpointTable : ISsoLifecycleRow
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

    [SugarColumn(ColumnName = "sso_client_id")]
    public Guid SsoClientId { get; set; }

    [SugarColumn(ColumnName = "sso_client_is_deleted")]
    public bool SsoClientIsDeleted { get; set; }

    [SugarColumn(ColumnName = "n_id")]
    public string NId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "endpoint_type")]
    public SsoClientEndpointType EndpointType { get; set; }

    [SugarColumn(ColumnName = "uri")]
    public string Uri { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "normalized_uri")]
    public string NormalizedUri { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; }
}
