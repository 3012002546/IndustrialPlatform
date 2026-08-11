using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// identity_role 表的持久化模型(§6 公共列 + §11 业务列,snake_case)。
/// 仅承载表结构映射,业务不变量由 <see cref="IndustrialPlatform.Identity.Domain.Roles.Role"/> 聚合维护;
/// 仓储负责 POCO ↔ 聚合双向转换。
/// </summary>
[SugarTable("identity_role")]
public sealed class RoleTable
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

    [SugarColumn(ColumnName = "description")]
    public string? Description { get; set; }

    [SugarColumn(ColumnName = "is_system")]
    public bool IsSystem { get; set; }
}
