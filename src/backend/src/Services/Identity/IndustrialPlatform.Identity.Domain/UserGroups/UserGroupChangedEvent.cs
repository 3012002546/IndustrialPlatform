using IndustrialPlatform.SharedKernel.Events;

namespace IndustrialPlatform.Identity.Domain.UserGroups;

/// <summary>
/// 用户组资料/状态变更事件(§29A.6,契约 <c>Identity.UserGroupChanged.v1</c>)。
/// 载荷使用业务 NId 与非敏感资料摘要,不含数据库主键。
/// </summary>
public sealed class UserGroupChangedEvent : IDomainEvent
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>用户组业务标识。</summary>
    public string GroupNId { get; }

    /// <summary>用户组名称。</summary>
    public string Name { get; }

    /// <summary>用户组状态。</summary>
    public UserGroupStatus Status { get; }

    /// <summary>事件发生时间。</summary>
    public DateTimeOffset OccurredOn { get; }

    /// <summary>创建事件并记录发生时间。</summary>
    public UserGroupChangedEvent(string tenantNId, string groupNId, string name, UserGroupStatus status)
    {
        TenantNId = tenantNId;
        GroupNId = groupNId;
        Name = name;
        Status = status;
        OccurredOn = DateTimeOffset.UtcNow;
    }
}
