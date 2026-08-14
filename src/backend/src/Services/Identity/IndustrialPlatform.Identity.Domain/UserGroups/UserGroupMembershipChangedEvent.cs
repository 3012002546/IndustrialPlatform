using IndustrialPlatform.SharedKernel.Events;

namespace IndustrialPlatform.Identity.Domain.UserGroups;

/// <summary>
/// 用户组成员变更事件(§29A.6,契约 <c>Identity.UserGroupMembershipChanged.v1</c>)。
/// 载荷使用业务 NId;UserNId 为该次加入/移出的受影响用户,不含数据库主键。
/// </summary>
public sealed class UserGroupMembershipChangedEvent : IDomainEvent
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>用户组业务标识。</summary>
    public string GroupNId { get; }

    /// <summary>受影响的成员用户业务标识。</summary>
    public string UserNId { get; }

    /// <summary>事件发生时间。</summary>
    public DateTimeOffset OccurredOn { get; }

    /// <summary>创建事件并记录发生时间。</summary>
    public UserGroupMembershipChangedEvent(string tenantNId, string groupNId, string userNId)
    {
        TenantNId = tenantNId;
        GroupNId = groupNId;
        UserNId = userNId;
        OccurredOn = DateTimeOffset.UtcNow;
    }
}
