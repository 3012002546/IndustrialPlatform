using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_position 表的持久化模型(9 公共列 + 业务列,snake_case)。
/// 岗位专属于一个组织:organization_id/organization_is_deleted 为组织复合外键快照;
/// normalized_name 承载同组织名称大小写不敏感唯一。创建后 OrganizationNId 不可变。
/// </summary>
[SugarTable("system_data_position")]
public sealed class PositionTable
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

    [SugarColumn(ColumnName = "organization_n_id")]
    public string OrganizationNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "organization_id")]
    public Guid OrganizationId { get; set; }

    [SugarColumn(ColumnName = "organization_is_deleted")]
    public bool OrganizationIsDeleted { get; set; }

    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "normalized_name")]
    public string NormalizedName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "description")]
    public string? Description { get; set; }

    [SugarColumn(ColumnName = "display_order")]
    public int DisplayOrder { get; set; }

    [SugarColumn(ColumnName = "status")]
    public int Status { get; set; }
}
