namespace IndustrialPlatform.Identity.Domain.UserGroups;

/// <summary>
/// 用户组状态(§29A.2)。禁用组立即停止贡献角色,但保留成员与角色配置以便恢复。
/// </summary>
public enum UserGroupStatus
{
    /// <summary>正常:成员与组角色持续贡献有效角色。</summary>
    Active = 0,

    /// <summary>已禁用:不再贡献任何角色,成员与角色配置保留。</summary>
    Disabled = 1,
}
