using IndustrialPlatform.SharedKernel.Events;

namespace IndustrialPlatform.Identity.Domain.UserGroups;

/// <summary>
/// 用户组角色变更事件(§29A.6,契约 <c>Identity.UserGroupRolesChanged.v1</c>)。
/// 载荷使用业务 NId;受影响用户为组内全部成员,事件以 GroupNId 作为批次引用,
/// 平台自身的授权版本推进/缓存失效/会话撤销由应用层同步完成。
/// </summary>
public sealed class UserGroupRolesChangedEvent : IDomainEvent
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>用户组业务标识。</summary>
    public string GroupNId { get; }

    /// <summary>事件发生时间。</summary>
    public DateTimeOffset OccurredOn { get; }

    /// <summary>创建事件并记录发生时间。</summary>
    public UserGroupRolesChangedEvent(string tenantNId, string groupNId)
    {
        TenantNId = tenantNId;
        GroupNId = groupNId;
        OccurredOn = DateTimeOffset.UtcNow;
    }
}
