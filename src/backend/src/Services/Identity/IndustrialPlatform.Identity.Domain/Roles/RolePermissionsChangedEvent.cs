using IndustrialPlatform.SharedKernel.Events;

namespace IndustrialPlatform.Identity.Domain.Roles;

/// <summary>
/// 角色权限变更事件(§20,契约 <c>Identity.RolePermissionsChanged.v1</c>)。
/// 权限缓存失效信号,载荷使用业务 NId,不含数据库主键或完整权限列表。
/// </summary>
public sealed class RolePermissionsChangedEvent : IDomainEvent
{
    /// <summary>租户编码。</summary>
    public string TenantNId { get; }

    /// <summary>角色业务标识。</summary>
    public string RoleNId { get; }

    /// <summary>权限业务标识。</summary>
    public string PermissionNId { get; }

    /// <summary>事件发生时间。</summary>
    public DateTimeOffset OccurredOn { get; }

    /// <summary>创建事件并记录发生时间。</summary>
    public RolePermissionsChangedEvent(string tenantNId, string roleNId, string permissionNId)
    {
        TenantNId = tenantNId;
        RoleNId = roleNId;
        PermissionNId = permissionNId;
        OccurredOn = DateTimeOffset.UtcNow;
    }
}
