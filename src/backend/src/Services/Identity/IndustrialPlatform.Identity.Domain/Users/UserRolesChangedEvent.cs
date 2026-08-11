using IndustrialPlatform.SharedKernel.Events;

namespace IndustrialPlatform.Identity.Domain.Users;

/// <summary>
/// 用户角色变更事件(§20,契约 <c>Identity.UserRolesChanged.v1</c>)。
/// 权限缓存失效信号,载荷使用业务 NId,不含数据库主键。
/// </summary>
public sealed class UserRolesChangedEvent : IDomainEvent
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>用户业务标识。</summary>
    public string UserNId { get; }

    /// <summary>角色业务标识。</summary>
    public string RoleNId { get; }

    /// <summary>事件发生时间。</summary>
    public DateTimeOffset OccurredOn { get; }

    /// <summary>创建事件并记录发生时间。</summary>
    public UserRolesChangedEvent(string tenantNId, string userNId, string roleNId)
    {
        TenantNId = tenantNId;
        UserNId = userNId;
        RoleNId = roleNId;
        OccurredOn = DateTimeOffset.UtcNow;
    }
}
