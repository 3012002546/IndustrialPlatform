using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_organization 表的持久化模型(9 公共列 + 业务列,snake_case)。
/// 统一有类型树:公司仅根、同租户多根、父子同租户、自引用复合外键 (parent_organization_id, parent_organization_is_deleted)。
/// 树修订号 organization_revision 供移动预览过期校验;normalized_name 承载同父名称大小写不敏感唯一。
/// </summary>
[SugarTable("system_data_organization")]
public sealed class AdministrativeOrganizationTable
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

    [SugarColumn(ColumnName = "normalized_name")]
    public string NormalizedName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "type")]
    public int Type { get; set; }

    [SugarColumn(ColumnName = "parent_organization_n_id")]
    public string? ParentOrganizationNId { get; set; }

    [SugarColumn(ColumnName = "parent_organization_id")]
    public Guid? ParentOrganizationId { get; set; }

    [SugarColumn(ColumnName = "parent_organization_is_deleted")]
    public bool ParentOrganizationIsDeleted { get; set; }

    [SugarColumn(ColumnName = "display_order")]
    public int DisplayOrder { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }

    [SugarColumn(ColumnName = "organization_revision")]
    public long OrganizationRevision { get; set; }
}
