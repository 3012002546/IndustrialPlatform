using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// identity_user_role 表的持久化模型(§6 公共列 + §11 业务列,snake_case)。
/// 复合外键影子列 UserIsDeleted/RoleIsDeleted 由数据库 ON UPDATE CASCADE 同步;
/// 仓储负责 POCO ↔ <see cref="IndustrialPlatform.Identity.Domain.Users.UserRole"/> 双向转换。
/// </summary>
[SugarTable("identity_user_role")]
public sealed class UserRoleTable
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

    [SugarColumn(ColumnName = "user_id")]
    public Guid UserId { get; set; }

    [SugarColumn(ColumnName = "user_is_deleted")]
    public bool UserIsDeleted { get; set; }

    [SugarColumn(ColumnName = "role_id")]
    public Guid RoleId { get; set; }

    [SugarColumn(ColumnName = "role_is_deleted")]
    public bool RoleIsDeleted { get; set; }
}
