using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.Identity.Domain.Roles;

/// <summary>
/// 角色-权限关系实体(§9.3),Role 聚合子实体。无业务 NId,
/// 唯一性靠父级组合与关系自身软删除状态;复合外键影子列由持久化层维护。
/// </summary>
public sealed class RolePermission : Entity
{
    /// <summary>所属角色主键。</summary>
    public Guid RoleId { get; }

    /// <summary>创建时所属角色软删除状态快照,由持久化层在父级删除后批量更新。</summary>
    public bool RoleIsDeleted { get; }

    /// <summary>关联权限主键。</summary>
    public Guid PermissionId { get; }

    /// <summary>创建时权限软删除状态快照,由持久化层在权限删除后批量更新。</summary>
    public bool PermissionIsDeleted { get; }

    /// <summary>由 Role 聚合在分配权限时创建。</summary>
    internal RolePermission(Guid roleId, bool roleIsDeleted, Guid permissionId, bool permissionIsDeleted)
    {
        RoleId = roleId;
        RoleIsDeleted = roleIsDeleted;
        PermissionId = permissionId;
        PermissionIsDeleted = permissionIsDeleted;
    }

    /// <summary>持久化层重建专用构造,恢复全部业务字段与生命周期状态。</summary>
    internal RolePermission(
        Guid id,
        Guid roleId,
        bool roleIsDeleted,
        Guid permissionId,
        bool permissionIsDeleted,
        bool isFrozen,
        bool isLocked,
        bool isDeleted,
        string entityType,
        DateTimeOffset createdOn,
        DateTimeOffset lastUpdatedOn,
        long optimisticVersion,
        Guid concurrencyVersion)
        : base(id)
    {
        RoleId = roleId;
        RoleIsDeleted = roleIsDeleted;
        PermissionId = permissionId;
        PermissionIsDeleted = permissionIsDeleted;
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }
}
