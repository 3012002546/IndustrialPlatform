using IndustrialPlatform.Identity.Domain.Permissions;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// identity_permission 表的持久化模型(§6 公共列 + §11 业务列,snake_case)。
/// 仅承载表结构映射,业务不变量由 <see cref="IndustrialPlatform.Identity.Domain.Permissions.Permission"/> 聚合维护;
/// 仓储负责 POCO ↔ 聚合双向转换。
/// </summary>
[SugarTable("identity_permission")]
public sealed class PermissionTable
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

    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "type")]
    public PermissionType Type { get; set; }

    [SugarColumn(ColumnName = "parent_permission_n_id")]
    public string? ParentPermissionNId { get; set; }

    [SugarColumn(ColumnName = "parent_permission_id")]
    public Guid? ParentPermissionId { get; set; }

    [SugarColumn(ColumnName = "parent_permission_is_deleted")]
    public bool? ParentPermissionIsDeleted { get; set; }

    [SugarColumn(ColumnName = "description")]
    public string? Description { get; set; }

    [SugarColumn(ColumnName = "status")]
    public PermissionStatus Status { get; set; }
}
