using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.Identity.Domain.UserGroups;

/// <summary>
/// 用户组-角色关系实体(§29A.2),UserGroup 聚合子实体。无业务 NId,
/// 唯一性靠 <c>TenantNId+UserGroup_Id+Role_Id</c> 组合与关系自身软删除状态;
/// 用户组只允许分配角色,不允许直接分配权限(权限只能经 Role 授予)。
/// </summary>
public sealed class UserGroupRole : Entity
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>所属用户组主键。</summary>
    public Guid UserGroupId { get; }

    /// <summary>创建时所属用户组软删除状态快照,由持久化层在父级删除后批量更新。</summary>
    public bool UserGroupIsDeleted { get; }

    /// <summary>关联角色主键。</summary>
    public Guid RoleId { get; }

    /// <summary>创建时角色软删除状态快照,由持久化层在角色删除后批量更新。</summary>
    public bool RoleIsDeleted { get; }

    /// <summary>由 UserGroup 聚合在分配角色时创建。</summary>
    internal UserGroupRole(string tenantNId, Guid userGroupId, bool userGroupIsDeleted, Guid roleId, bool roleIsDeleted)
    {
        TenantNId = tenantNId;
        UserGroupId = userGroupId;
        UserGroupIsDeleted = userGroupIsDeleted;
        RoleId = roleId;
        RoleIsDeleted = roleIsDeleted;
    }

    /// <summary>持久化层重建专用构造,恢复全部业务字段与生命周期状态。</summary>
    internal UserGroupRole(
        Guid id,
        string tenantNId,
        Guid userGroupId,
        bool userGroupIsDeleted,
        Guid roleId,
        bool roleIsDeleted,
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
        TenantNId = tenantNId;
        UserGroupId = userGroupId;
        UserGroupIsDeleted = userGroupIsDeleted;
        RoleId = roleId;
        RoleIsDeleted = roleIsDeleted;
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
