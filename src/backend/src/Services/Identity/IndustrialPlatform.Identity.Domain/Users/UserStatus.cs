namespace IndustrialPlatform.Identity.Domain.Users;

/// <summary>
/// 用户状态。
/// </summary>
public enum UserStatus
{
    /// <summary>正常状态,可登录。</summary>
    Active = 0,

    /// <summary>已禁用,禁止登录。</summary>
    Disabled = 1,
}
