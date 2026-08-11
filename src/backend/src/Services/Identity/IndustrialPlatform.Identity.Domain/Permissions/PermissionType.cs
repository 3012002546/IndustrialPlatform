namespace IndustrialPlatform.Identity.Domain.Permissions;

/// <summary>
/// 权限类型(§9.2)。Menu 为菜单、Page 为页面、Action 为操作、Api 为接口。
/// </summary>
public enum PermissionType
{
    /// <summary>菜单。</summary>
    Menu = 0,

    /// <summary>页面。</summary>
    Page = 1,

    /// <summary>操作。</summary>
    Action = 2,

    /// <summary>接口。</summary>
    Api = 3,
}
