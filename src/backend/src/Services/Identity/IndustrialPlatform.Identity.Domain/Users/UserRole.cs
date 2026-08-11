using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.Identity.Domain.Users;

/// <summary>
/// 用户-角色关系实体(§9.3),User 聚合子实体。无业务 NId,
/// 唯一性靠父级组合与关系自身软删除状态;复合外键影子列由持久化层维护。
/// </summary>
public sealed class UserRole : Entity
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>所属用户主键。</summary>
    public Guid UserId { get; }

    /// <summary>创建时所属用户软删除状态快照,由持久化层在用户删除后批量更新。</summary>
    public bool UserIsDeleted { get; }

    /// <summary>关联角色主键。</summary>
    public Guid RoleId { get; }

    /// <summary>创建时角色软删除状态快照,由持久化层在角色删除后批量更新。</summary>
    public bool RoleIsDeleted { get; }

    /// <summary>由 User 聚合在分配角色时创建。</summary>
    internal UserRole(string tenantNId, Guid userId, bool userIsDeleted, Guid roleId, bool roleIsDeleted)
    {
        TenantNId = tenantNId;
        UserId = userId;
        UserIsDeleted = userIsDeleted;
        RoleId = roleId;
        RoleIsDeleted = roleIsDeleted;
    }
}
