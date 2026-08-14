using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// identity_user_group_membership 表的持久化模型(§6 公共列 + §29A.2 业务列,snake_case)。
/// 成员只能是用户(禁止用户组嵌套);复合外键影子列 UserGroupIsDeleted/UserIsDeleted
/// 由数据库 ON UPDATE CASCADE 同步;仓储负责 POCO ↔
/// <see cref="IndustrialPlatform.Identity.Domain.UserGroups.UserGroupMembership"/> 双向转换。
/// </summary>
[SugarTable("identity_user_group_membership")]
public sealed class UserGroupMembershipTable
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

    [SugarColumn(ColumnName = "user_group_id")]
    public Guid UserGroupId { get; set; }

    [SugarColumn(ColumnName = "user_group_is_deleted")]
    public bool UserGroupIsDeleted { get; set; }

    [SugarColumn(ColumnName = "user_id")]
    public Guid UserId { get; set; }

    [SugarColumn(ColumnName = "user_is_deleted")]
    public bool UserIsDeleted { get; set; }
}
