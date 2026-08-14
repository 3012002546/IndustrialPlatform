using IndustrialPlatform.SharedKernel.Events;

namespace IndustrialPlatform.Identity.Domain.Users;

/// <summary>
/// 用户墓碑恢复事件(§29A.6,契约 <c>Identity.UserRestored.v1</c>)。
/// 载荷使用业务 NId,不含数据库主键;恢复后账号保持 Disabled 且不恢复授权。
/// </summary>
public sealed class UserRestoredEvent : IDomainEvent
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>用户业务标识。</summary>
    public string UserNId { get; }

    /// <summary>事件发生时间。</summary>
    public DateTimeOffset OccurredOn { get; }

    /// <summary>创建事件并记录发生时间。</summary>
    public UserRestoredEvent(string tenantNId, string userNId)
    {
        TenantNId = tenantNId;
        UserNId = userNId;
        OccurredOn = DateTimeOffset.UtcNow;
    }
}
