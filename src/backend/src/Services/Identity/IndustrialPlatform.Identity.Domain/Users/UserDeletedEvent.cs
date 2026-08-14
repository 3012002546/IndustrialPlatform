using IndustrialPlatform.SharedKernel.Events;

namespace IndustrialPlatform.Identity.Domain.Users;

/// <summary>
/// 用户安全删除(墓碑)事件(§29A.6,契约 <c>Identity.UserDeleted.v1</c>)。
/// 载荷使用业务 NId 与删除后的安全版本,不含数据库主键。
/// </summary>
public sealed class UserDeletedEvent : IDomainEvent
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>用户业务标识。</summary>
    public string UserNId { get; }

    /// <summary>删除后的安全版本(已递增,旧会话失效)。</summary>
    public int AuthVersion { get; }

    /// <summary>事件发生时间。</summary>
    public DateTimeOffset OccurredOn { get; }

    /// <summary>创建事件并记录发生时间。</summary>
    public UserDeletedEvent(string tenantNId, string userNId, int authVersion)
    {
        TenantNId = tenantNId;
        UserNId = userNId;
        AuthVersion = authVersion;
        OccurredOn = DateTimeOffset.UtcNow;
    }
}
