using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.Identity.Domain.UserGroups;

/// <summary>
/// 用户组成员关系实体(§29A.2),UserGroup 聚合子实体。无业务 NId,
/// 唯一性靠 <c>TenantNId+User_Id+UserGroup_Id</c> 组合与关系自身软删除状态;
/// 成员只能是用户(禁止用户组嵌套),复合外键影子列由持久化层维护。
/// </summary>
public sealed class UserGroupMembership : Entity
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>所属用户组主键。</summary>
    public Guid UserGroupId { get; }

    /// <summary>创建时所属用户组软删除状态快照,由持久化层在父级删除后批量更新。</summary>
    public bool UserGroupIsDeleted { get; }

    /// <summary>成员用户主键(用户组禁止嵌套,成员只能是用户)。</summary>
    public Guid UserId { get; }

    /// <summary>创建时成员用户软删除状态快照,由持久化层在用户删除后批量更新。</summary>
    public bool UserIsDeleted { get; }

    /// <summary>由 UserGroup 聚合在加入成员时创建。</summary>
    internal UserGroupMembership(string tenantNId, Guid userGroupId, bool userGroupIsDeleted, Guid userId, bool userIsDeleted)
    {
        TenantNId = tenantNId;
        UserGroupId = userGroupId;
        UserGroupIsDeleted = userGroupIsDeleted;
        UserId = userId;
        UserIsDeleted = userIsDeleted;
    }

    /// <summary>持久化层重建专用构造,恢复全部业务字段与生命周期状态。</summary>
    internal UserGroupMembership(
        Guid id,
        string tenantNId,
        Guid userGroupId,
        bool userGroupIsDeleted,
        Guid userId,
        bool userIsDeleted,
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
        UserId = userId;
        UserIsDeleted = userIsDeleted;
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
