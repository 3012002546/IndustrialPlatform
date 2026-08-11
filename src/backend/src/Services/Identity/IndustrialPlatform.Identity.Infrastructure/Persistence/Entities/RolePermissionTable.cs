using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// identity_role_permission 表的持久化模型(§6 公共列 + §11 业务列,snake_case)。
/// 复合外键影子列 RoleIsDeleted/PermissionIsDeleted 由数据库 ON UPDATE CASCADE 同步;
/// 仓储负责 POCO ↔ <see cref="IndustrialPlatform.Identity.Domain.Roles.RolePermission"/> 双向转换。
/// </summary>
[SugarTable("identity_role_permission")]
public sealed class RolePermissionTable
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

    [SugarColumn(ColumnName = "role_id")]
    public Guid RoleId { get; set; }

    [SugarColumn(ColumnName = "role_is_deleted")]
    public bool RoleIsDeleted { get; set; }

    [SugarColumn(ColumnName = "permission_id")]
    public Guid PermissionId { get; set; }

    [SugarColumn(ColumnName = "permission_is_deleted")]
    public bool PermissionIsDeleted { get; set; }
}
