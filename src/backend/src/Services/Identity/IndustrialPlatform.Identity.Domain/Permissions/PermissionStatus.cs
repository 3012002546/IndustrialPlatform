namespace IndustrialPlatform.Identity.Domain.Permissions;

/// <summary>
/// 权限状态(§11)。创建即 Active,由平台管理维护启用/禁用。
/// </summary>
public enum PermissionStatus
{
    /// <summary>启用。</summary>
    Active = 0,

    /// <summary>禁用。</summary>
    Disabled = 1,
}
